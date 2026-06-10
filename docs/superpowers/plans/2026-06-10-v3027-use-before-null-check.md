# V3027 Use-Before-Null-Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement PVS-Studio rule V3027 — a variable dereferenced in a logical expression before it is verified against null in the same expression.

**Architecture:** A single `AstRule` in `OpenVulScan.Rules.Ast` hooks `OnBinaryExpression`, filters to `&&`/`||`, processes only the root of each logical chain, flattens operands into short-circuit evaluation order, and flags any symbol whose first dereference precedes its first null-check (`firstDeref < firstNullCheck`). Symbols are resolved with `SemanticModel` and compared with `SymbolEqualityComparer.Default`.

**Tech Stack:** C# / .NET 10, Roslyn syntax & semantic APIs, xUnit + Verify snapshot tests.

**Spec:** `docs/superpowers/specs/2026-06-10-v3027-use-before-null-check-design.md`

---

## Background the engineer needs

- **Rule discovery is automatic.** Adding a `[Rule(...)]`-decorated class to `OpenVulScan.Rules.Ast` is enough — `RuleRegistry.Scan` reflects over the assembly. No csproj edits, no manual registration.
- **`AstRule` dispatch** is reflection over `protected override void On<Kind>(SyntaxNodeContext)`. `OnBinaryExpression` is a fan-out that fires for ~22 binary syntax kinds (including `LogicalAndExpression` and `LogicalOrExpression`), so the handler MUST filter by `node.Kind()`.
- **`SyntaxNodeContext`** exposes `Node`, `SemanticModel`, `Compilation`, `CancellationToken`, and `ReportDiagnostic(Diagnostic)`.
- **Implicit usings are on** (see `V3001IdenticalSubExpressions.cs`: it uses `ArgumentNullException` with no `using System;`). `System`, `System.Collections.Generic`, `System.Threading` are implicitly available. Only the Roslyn namespaces need explicit `using`.
- **`TreatWarningsAsErrors=true`** with `AnalysisLevel=latest-all`. Notably RS1024 requires `ISymbol` dictionaries/sets to be constructed with `SymbolEqualityComparer.Default`. Always pass it.
- **Snapshot workflow (Verify):** `SnapshotTestHarness.RunRuleSnapshotAsync(ruleCode, testCaseName, source)` runs the full scheduler, filters diagnostics to `ruleCode`, sorts by location, and verifies an anonymous object. The verified file is named `V3027.<TestCase>.verified.txt` and lives next to the test file. **Verify omits empty collections** — a case that produces no diagnostics serialises to just `{ RuleCode, TestCase, Source }` with no `Diagnostics:` block. To accept a new/changed snapshot, rename the generated `*.received.txt` to `*.verified.txt`.
- **Test snippets must declare their variables** (as typed parameters), because the rule resolves symbols via `SemanticModel`. Undeclared identifiers resolve to `null` symbol and are (correctly) ignored. The test compilation only references core lib (`typeof(object).Assembly`), so use `string`, arrays, and `object`, or declare small helper classes inline.

---

## File Structure

- **Create:** `src/OpenVulScan.Rules.Ast/V3027UseBeforeNullCheck.cs` — the rule, self-contained (descriptor, handler, private static helpers). One responsibility: detect V3027 within a single logical expression.
- **Create:** `tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs` — xUnit test class, ≥13 snapshot cases.
- **Create (generated, committed):** `tests/OpenVulScan.Rules.Tests/V3027.*.verified.txt` — accepted snapshots.

---

## Task 1: Implement the V3027 rule, driven by the first positive case

The algorithm is one cohesive unit; this task implements it in full, driven by a single positive snapshot test.

**Files:**
- Create: `src/OpenVulScan.Rules.Ast/V3027UseBeforeNullCheck.cs`
- Create: `tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs`:

```csharp
namespace OpenVulScan.Tests;

public class V3027UseBeforeNullCheckTests
{
    [Fact]
    public Task AndChain_MemberAccessBeforeNotNull_Flags()
    {
        const string source = "class C { void M(int[] a) { var r = a.Length > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "AndChain_MemberAccessBeforeNotNull_Flags", source);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: FAIL — compile error (`V3027UseBeforeNullCheck` does not exist yet) OR, once the rule file is added empty, a Verify "pending" failure because there is no verified snapshot.

- [ ] **Step 3: Implement the rule**

Create `src/OpenVulScan.Rules.Ast/V3027UseBeforeNullCheck.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3027", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3027UseBeforeNullCheck : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3027",
        "Variable used before null-check in the same logical expression",
        "Variable '{0}' was used in the logical expression before it was verified against null",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnBinaryExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not BinaryExpressionSyntax binary || !IsLogical(binary))
        {
            return;
        }

        // Analyse each logical chain once, from its root. Nested logical nodes are skipped
        // because their first non-parenthesised ancestor is itself a logical expression.
        if (IsLogical(StripParenthesesUp(binary.Parent)))
        {
            return;
        }

        var operands = new List<ExpressionSyntax>();
        FlattenOperands(binary, operands);

        var model = context.SemanticModel;
        var ct = context.CancellationToken;
        var firstDeref = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        var firstNullCheck = new Dictionary<ISymbol, NullCheck>(SymbolEqualityComparer.Default);

        for (var i = 0; i < operands.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            CollectDerefs(operands[i], model, i, firstDeref, ct);
            CollectNullChecks(operands[i], model, i, firstNullCheck, ct);
        }

        foreach (var (symbol, check) in firstNullCheck)
        {
            if (firstDeref.TryGetValue(symbol, out var derefIndex) && derefIndex < check.Index)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    check.Node.GetLocation(),
                    check.Name));
            }
        }
    }

    private readonly record struct NullCheck(int Index, SyntaxNode Node, string Name);

    private static bool IsLogical(SyntaxNode? node)
        => node is BinaryExpressionSyntax b
           && b.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression;

    private static SyntaxNode? StripParenthesesUp(SyntaxNode? node)
    {
        while (node is ParenthesizedExpressionSyntax paren)
        {
            node = paren.Parent;
        }

        return node;
    }

    private static ExpressionSyntax StripParenthesesDown(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }

    private static void FlattenOperands(ExpressionSyntax expr, List<ExpressionSyntax> operands)
    {
        var stripped = StripParenthesesDown(expr);
        if (stripped is BinaryExpressionSyntax b && IsLogical(b))
        {
            FlattenOperands(b.Left, operands);
            FlattenOperands(b.Right, operands);
        }
        else
        {
            operands.Add(stripped);
        }
    }

    private static void CollectDerefs(
        ExpressionSyntax operand,
        SemanticModel model,
        int index,
        Dictionary<ISymbol, int> firstDeref,
        CancellationToken ct)
    {
        foreach (var node in operand.DescendantNodesAndSelf())
        {
            ct.ThrowIfCancellationRequested();

            ExpressionSyntax? receiver = node switch
            {
                MemberAccessExpressionSyntax m when m.Kind() == SyntaxKind.SimpleMemberAccessExpression => m.Expression,
                ElementAccessExpressionSyntax e => e.Expression,
                _ => null
            };

            if (receiver is null)
            {
                continue;
            }

            var symbol = ResolveTrackedSymbol(receiver, model);
            if (symbol is not null)
            {
                firstDeref.TryAdd(symbol, index);
            }
        }
    }

    private static void CollectNullChecks(
        ExpressionSyntax operand,
        SemanticModel model,
        int index,
        Dictionary<ISymbol, NullCheck> firstNullCheck,
        CancellationToken ct)
    {
        foreach (var node in operand.DescendantNodesAndSelf())
        {
            ct.ThrowIfCancellationRequested();

            var checkedExpr = TryGetNullCheckedExpression(node, out var checkNode);
            if (checkedExpr is null)
            {
                continue;
            }

            var symbol = ResolveTrackedSymbol(checkedExpr, model);
            if (symbol is not null)
            {
                firstNullCheck.TryAdd(symbol, new NullCheck(index, checkNode, GetName(checkedExpr)));
            }
        }
    }

    private static ISymbol? ResolveTrackedSymbol(ExpressionSyntax receiver, SemanticModel model)
    {
        var isTrackedShape = receiver is IdentifierNameSyntax
            || (receiver is MemberAccessExpressionSyntax ma
                && ma.Kind() == SyntaxKind.SimpleMemberAccessExpression
                && ma.Expression is ThisExpressionSyntax);

        if (!isTrackedShape)
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(receiver).Symbol;
        return symbol is { Kind: SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field or SymbolKind.Property }
            ? symbol
            : null;
    }

    private static ExpressionSyntax? TryGetNullCheckedExpression(SyntaxNode node, out SyntaxNode checkNode)
    {
        checkNode = node;

        if (node is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
        {
            if (IsNullLiteral(binary.Right))
            {
                return binary.Left;
            }

            if (IsNullLiteral(binary.Left))
            {
                return binary.Right;
            }
        }
        else if (node is IsPatternExpressionSyntax isPattern && IsNullPattern(isPattern.Pattern))
        {
            return isPattern.Expression;
        }

        return null;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
        => expr is LiteralExpressionSyntax lit && lit.Kind() == SyntaxKind.NullLiteralExpression;

    private static bool IsNullPattern(PatternSyntax pattern)
    {
        return pattern switch
        {
            ConstantPatternSyntax constant => IsNullLiteral(constant.Expression),
            UnaryPatternSyntax unary when unary.Kind() == SyntaxKind.NotPattern => IsNullPattern(unary.Pattern),
            _ => false
        };
    }

    private static string GetName(ExpressionSyntax expr)
        => expr switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => expr.ToString()
        };
}
```

- [ ] **Step 4: Build to confirm it compiles clean**

Run: `dotnet build src/OpenVulScan.Rules.Ast/OpenVulScan.Rules.Ast.csproj --configuration Release`
Expected: Build succeeded, 0 warnings (warnings are errors).

- [ ] **Step 5: Run the test and accept the snapshot**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: FAIL with a Verify mismatch and a generated `V3027.AndChain_MemberAccessBeforeNotNull_Flags.received.txt`.

Open the `.received.txt`. Confirm it contains exactly one diagnostic:
- `Id: V3027`
- `Message: Variable 'a' was used in the logical expression before it was verified against null`
- `Severity: Warning`
- a `Location` block pointing at the `a != null` sub-expression (StartLine 1).

If correct, accept it by renaming:

```bash
mv tests/OpenVulScan.Rules.Tests/V3027.AndChain_MemberAccessBeforeNotNull_Flags.received.txt tests/OpenVulScan.Rules.Tests/V3027.AndChain_MemberAccessBeforeNotNull_Flags.verified.txt
```

- [ ] **Step 6: Re-run the test to verify it passes**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: PASS (1 test).

- [ ] **Step 7: Commit**

```bash
git add src/OpenVulScan.Rules.Ast/V3027UseBeforeNullCheck.cs tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs tests/OpenVulScan.Rules.Tests/V3027.AndChain_MemberAccessBeforeNotNull_Flags.verified.txt
git commit -m "feat(rules): add V3027 use-before-null-check (ovs-2qi.14)"
```

---

## Task 2: Positive coverage — remaining flagging cases

Add the rest of the cases that MUST flag. These exercise `||`, element access, invocation receivers, multi-operand chains, `a.b`-style dereference, and `is null` / `is not null`. If any does not flag as expected, the algorithm has a gap — fix the rule before accepting snapshots.

**Files:**
- Modify: `tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs`

- [ ] **Step 1: Add the positive test methods**

Append these methods inside the `V3027UseBeforeNullCheckTests` class:

```csharp
    [Fact]
    public Task OrChain_MemberAccessBeforeEqualsNull_Flags()
    {
        const string source = "class C { void M(string s) { var r = s.Length == 0 || s == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "OrChain_MemberAccessBeforeEqualsNull_Flags", source);
    }

    [Fact]
    public Task ElementAccessBeforeNotNull_Flags()
    {
        const string source = "class C { void M(int[] a) { var r = a[0] > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "ElementAccessBeforeNotNull_Flags", source);
    }

    [Fact]
    public Task MemberOfMemberBeforeEqualsNull_Flags()
    {
        const string source = "class N { public N b; } class C { void M(N a) { var r = a.b != null && a == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MemberOfMemberBeforeEqualsNull_Flags", source);
    }

    [Fact]
    public Task InvocationReceiverBeforeNotNull_Flags()
    {
        const string source = "class C { void M(object obj) { var r = obj.ToString() != \"\" && obj != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "InvocationReceiverBeforeNotNull_Flags", source);
    }

    [Fact]
    public Task MultiOperand_SecondVariableDerefBeforeCheck_Flags()
    {
        const string source = "class C { void M(object a, int[] b) { var r = a != null && b.Length > 0 && b == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MultiOperand_SecondVariableDerefBeforeCheck_Flags", source);
    }

    [Fact]
    public Task MemberAccessBeforeIsNull_Flags()
    {
        const string source = "class C { void M(string s) { var r = s.Length > 0 && s is null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MemberAccessBeforeIsNull_Flags", source);
    }

    [Fact]
    public Task MemberAccessBeforeIsNotNull_Flags()
    {
        const string source = "class C { void M(string s) { var r = s.Length > 0 && s is not null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MemberAccessBeforeIsNotNull_Flags", source);
    }
```

- [ ] **Step 2: Run the new tests and inspect received snapshots**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: the 7 new tests FAIL with Verify "pending" mismatches, producing `*.received.txt` files.

For each received file confirm exactly one V3027 diagnostic with the right variable name in the message:
- `OrChain_...` → `Variable 's' ...`
- `ElementAccessBeforeNotNull_...` → `Variable 'a' ...`
- `MemberOfMemberBeforeEqualsNull_...` → `Variable 'a' ...` (only `a`; `b` is accessed but not dereferenced)
- `InvocationReceiverBeforeNotNull_...` → `Variable 'obj' ...`
- `MultiOperand_...` → `Variable 'b' ...` (only `b`; `a` is checked before any use)
- `MemberAccessBeforeIsNull_...` → `Variable 's' ...`
- `MemberAccessBeforeIsNotNull_...` → `Variable 's' ...`

If any received file is empty (no `Diagnostics:` block) or names the wrong variable, the rule has a defect — fix `V3027UseBeforeNullCheck.cs`, rebuild, re-run, and re-inspect before accepting.

- [ ] **Step 3: Accept the snapshots**

```bash
cd tests/OpenVulScan.Rules.Tests
for f in V3027.OrChain_MemberAccessBeforeEqualsNull_Flags V3027.ElementAccessBeforeNotNull_Flags V3027.MemberOfMemberBeforeEqualsNull_Flags V3027.InvocationReceiverBeforeNotNull_Flags V3027.MultiOperand_SecondVariableDerefBeforeCheck_Flags V3027.MemberAccessBeforeIsNull_Flags V3027.MemberAccessBeforeIsNotNull_Flags; do mv "$f.received.txt" "$f.verified.txt"; done
cd ../..
```

(On PowerShell: `Get-ChildItem tests/OpenVulScan.Rules.Tests/V3027.*.received.txt | ForEach-Object { Rename-Item $_ ($_.Name -replace '\.received\.txt$','.verified.txt') }`.)

- [ ] **Step 4: Re-run to verify all pass**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs tests/OpenVulScan.Rules.Tests/V3027.*.verified.txt src/OpenVulScan.Rules.Ast/V3027UseBeforeNullCheck.cs
git commit -m "test(rules): V3027 positive coverage (||, element/invocation, is null/not null, multi-operand)"
```

---

## Task 3: Negative coverage — must-not-flag cases

Add cases that MUST NOT flag: correct guard ordering, null-conditional access, no dereference, no nulls. Each accepted snapshot is the 4-line object with **no** `Diagnostics:` block (Verify omits empty collections).

**Files:**
- Modify: `tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs`

- [ ] **Step 1: Add the negative test methods**

Append these methods inside the `V3027UseBeforeNullCheckTests` class:

```csharp
    [Fact]
    public Task AndChain_NullCheckBeforeMemberAccess_DoesNotFlag()
    {
        const string source = "class C { void M(int[] a) { var r = a != null && a.Length > 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "AndChain_NullCheckBeforeMemberAccess_DoesNotFlag", source);
    }

    [Fact]
    public Task OrChain_NullCheckBeforeMemberAccess_DoesNotFlag()
    {
        const string source = "class C { void M(string s) { var r = s == null || s.Length == 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "OrChain_NullCheckBeforeMemberAccess_DoesNotFlag", source);
    }

    [Fact]
    public Task ConditionalAccessBeforeNotNull_DoesNotFlag()
    {
        const string source = "class C { void M(int[] a) { var r = a?.Length > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "ConditionalAccessBeforeNotNull_DoesNotFlag", source);
    }

    [Fact]
    public Task TwoNullChecksNoDeref_DoesNotFlag()
    {
        const string source = "class C { void M(object a, object b) { var r = a != null && b != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "TwoNullChecksNoDeref_DoesNotFlag", source);
    }

    [Fact]
    public Task NoNullsInvolved_DoesNotFlag()
    {
        const string source = "class C { void M(int x, int y) { var r = x > 0 && y < 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "NoNullsInvolved_DoesNotFlag", source);
    }
```

- [ ] **Step 2: Run the new tests and inspect received snapshots**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: the 5 new tests FAIL with Verify "pending", producing `*.received.txt`.

Confirm each received file has **no** `Diagnostics:` block (just `RuleCode`, `TestCase`, `Source`). If any contains a diagnostic, that is a false positive — fix `V3027UseBeforeNullCheck.cs`, rebuild, re-run, re-inspect before accepting.

- [ ] **Step 3: Accept the snapshots**

```bash
cd tests/OpenVulScan.Rules.Tests
for f in V3027.AndChain_NullCheckBeforeMemberAccess_DoesNotFlag V3027.OrChain_NullCheckBeforeMemberAccess_DoesNotFlag V3027.ConditionalAccessBeforeNotNull_DoesNotFlag V3027.TwoNullChecksNoDeref_DoesNotFlag V3027.NoNullsInvolved_DoesNotFlag; do mv "$f.received.txt" "$f.verified.txt"; done
cd ../..
```

(PowerShell equivalent as in Task 2.)

- [ ] **Step 4: Re-run to verify all pass**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3027UseBeforeNullCheckTests"`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs tests/OpenVulScan.Rules.Tests/V3027.*.verified.txt
git commit -m "test(rules): V3027 negative coverage (guards, ?. access, no-deref, no-nulls)"
```

---

## Task 4: Full verification and rule listing

**Files:** none (verification only).

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test --configuration Release`
Expected: all tests pass (the prior baseline was 429 passing / 0 failing / 1 skipped; V3027 adds 13 → 442 passing). 0 failures.

- [ ] **Step 2: Confirm a clean Release build (warnings-as-errors)**

Run: `dotnet build --configuration Release --no-restore`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Confirm the rule is discoverable**

Run: `dotnet run --project src/OpenVulScan.Cli -- rules list`
Expected: output includes a `V3027` entry.

- [ ] **Step 4: Commit only if anything changed**

If steps produced no file changes, there is nothing to commit. Otherwise:

```bash
git add -A
git commit -m "chore(rules): V3027 verification"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- §2 architecture (AstRule, OnBinaryExpression filter, root-only, SemanticModel) → Task 1 rule code.
- §3 algorithm (flatten, firstDeref/firstNullCheck, `<` criterion) → Task 1 rule code.
- §3.1 deref shape (identifier/`this.field`, member/element access, exclude `?.`) → `ResolveTrackedSymbol` + `CollectDerefs` (Task 1); `?.` exclusion validated by Task 3 case 11.
- §3.2 null-check shape (`== null`/`!= null`/`is null`/`is not null`) → `TryGetNullCheckedExpression`/`IsNullPattern` (Task 1); validated by Task 2 cases 7–8.
- §3.3 de-duplication → one entry per symbol via `firstNullCheck` dictionary keyed by symbol.
- §4 diagnostic (id/message/severity/location) → `s_descriptor` + report at `check.Node` (Task 1).
- §5 testing (≥10 cases) → 8 positive + 5 negative = 13 across Tasks 1–3.
- §6 acceptance (snapshots green, no regressions, clean build, rules list) → Task 4.

**Placeholder scan:** none — every code step contains full code; every command has expected output.

**Type consistency:** `IsLogical`, `StripParenthesesUp`/`StripParenthesesDown`, `FlattenOperands`, `CollectDerefs`, `CollectNullChecks`, `ResolveTrackedSymbol`, `TryGetNullCheckedExpression`, `IsNullLiteral`, `IsNullPattern`, `GetName`, and the `NullCheck` record are all defined once in Task 1 and referenced consistently. Dictionaries are `Dictionary<ISymbol, int>` and `Dictionary<ISymbol, NullCheck>`, both constructed with `SymbolEqualityComparer.Default`.
