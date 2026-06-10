# V3027 — Use Before Null-Check (Design Spec)

**Bead:** `ovs-2qi.14`
**Rule code:** V3027 (CWE-476, LEVEL-1)
**Date:** 2026-06-10
**Status:** Approved

## 1. Problem

PVS-Studio V3027: *"The variable was utilized in the logical expression before it was
verified against null in the same logical expression."*

A variable is unconditionally dereferenced in one operand of a short-circuit logical
expression (`&&` / `||`) and then compared against `null` in a **later** operand of the
**same** expression. The null comparison is either dead (the dereference would already have
thrown) or signals that the dereference is unguarded. Either way it is a defect.

```csharp
if (a.Length > 0 && a != null) { }   // V3027 — 'a' dereferenced before the != null check
if (s.Name == "" || s == null) { }   // V3027 — 's' dereferenced before the == null check
if (a.b != null && a == null) { }    // V3027 — 'a' dereferenced (a.b) before a == null

if (a != null && a.Length > 0) { }   // OK — guard precedes dereference
if (s == null || s.Name == "") { }   // OK — guard precedes dereference
```

The defect is confined to a **single logical expression tree**. No cross-statement data
flow is involved, so the rule is a syntactic AST analysis, not a `DataFlowRule`. (The bead
labelled it `DataFlowRule<NullStateLattice>`; that was superseded during brainstorming in
favour of an `AstRule` per KISS — a worklist solver adds no precision for a single
expression.)

## 2. Architecture

- **Assembly:** `OpenVulScan.Rules.Ast`
- **File:** `src/OpenVulScan.Rules.Ast/V3027UseBeforeNullCheck.cs`
- **Base:** `AstRule`, overrides `OnBinaryExpression`.
- **Trigger filter:** acts only when `context.Node.Kind()` is `LogicalAndExpression` or
  `LogicalOrExpression`. `OnBinaryExpression` fans out over ~22 syntax kinds, so the filter
  is required.
- **Root-only processing:** a logical node is processed only when it is the **root** of its
  logical chain — i.e. the first ancestor that is not a `ParenthesizedExpressionSyntax` is
  not itself a `LogicalAndExpression`/`LogicalOrExpression`. This guarantees each chain is
  analysed exactly once (the dispatcher visits every nested logical node).
- **Symbol resolution:** `context.SemanticModel` resolves identifiers/`this.field` to
  `ISymbol`; symbols are compared with `SymbolEqualityComparer.Default`.

## 3. Algorithm

The traversal order of leaf operands left→right equals the short-circuit evaluation order
(both `&&` and `||` evaluate the left operand first). One uniform criterion handles both
operators and even negations, because only **position** matters — not truth polarity.

1. **Flatten** the logical tree into an ordered list of leaf operands `[op₀ … opₙ]`:
   in-order traversal — for a node that is `&&`/`||` (after stripping parentheses), recurse
   Left then Right; otherwise the node is a leaf operand.
2. For each operand index `i`, collect:
   - **`deref(i)`** — symbols **unconditionally** dereferenced in `opᵢ`.
   - **`nullCheck(i)`** — symbols compared against `null` in `opᵢ`.
3. For each symbol `S`, compute `firstDeref(S)` and `firstNullCheck(S)` (smallest operand
   index in each set). If both exist and `firstDeref(S) < firstNullCheck(S)`, report **V3027**
   at the location of the first null-check of `S`.

### 3.1 What counts as a dereference of `S`

A `MemberAccessExpressionSyntax` (`S.Member`) or `ElementAccessExpressionSyntax` (`S[i]`)
whose receiver expression is:

- a simple `IdentifierNameSyntax`, **or**
- a `this.<field>` access (`MemberAccessExpressionSyntax` with `ThisExpressionSyntax`
  receiver),

and whose resolved symbol is a `Local`, `Parameter`, `Field`, or `Property`.

**Not** a dereference:

- null-conditional access `S?.Member` / `S?[i]` — the receiver is null-safe. (In Roslyn the
  member of `a?.b` is a `MemberBindingExpressionSyntax`, not a `MemberAccessExpressionSyntax`,
  so it is naturally excluded.)
- merely passing `S` as an argument, or `S == null` itself.

Receivers more complex than identifier / `this.field` (e.g. `a.b.c`) are out of scope to
avoid the `a?.b.c` false-positive trap and member-chain symbol ambiguity.

### 3.2 What counts as a null-check of `S`

- `S == null`, `null == S`, `S != null`, `null != S`
  (`BinaryExpressionSyntax`, kind `EqualsExpression`/`NotEqualsExpression`, one side a
  `null` literal, the other resolving to `S`).
- `S is null`, `S is not null` (`IsPatternExpressionSyntax` with `ConstantPatternSyntax`
  null, optionally under `NotPatternSyntax`).

Polarity is irrelevant: a null comparison at operand position `j` marks `firstNullCheck(S)`
regardless of `==`/`!=`/`is`/`is not` or any enclosing `!`.

### 3.3 De-duplication

At most one diagnostic per symbol per logical expression, even if `S` is dereferenced in
multiple operands.

## 4. Diagnostic

```
Id:        "V3027"
Title:     "Variable used before null-check in the same logical expression"
Message:   "Variable '{0}' was used in the logical expression before it was verified against null"
Category:  "GeneralAnalysis"
Severity:  Warning
Location:  the first null-check node of the offending symbol
```

`{0}` is the variable's identifier text.

## 5. Testing

`tests/OpenVulScan.Rules.Tests/V3027UseBeforeNullCheckTests.cs`, snapshot assertions via
`SnapshotTestHarness.RunRuleSnapshotAsync("V3027", testCase, source)`. ≥10 cases:

**Positive (must flag):**
1. `a.Length > 0 && a != null` — `&&`, member-access deref.
2. `s.Name == "" || s == null` — `||`, deref before `== null`.
3. `a[0] > 0 && a != null` — element-access deref.
4. `a.b != null && a == null` — deref `a` (via `a.b`) then `a == null`.
5. `obj.ToString() != "" && obj != null` — invocation-receiver deref.
6. `a != null && b.X > 0 && b == null` — multi-operand, `b` deref before `b` check.
7. `s.Length > 0 && s is null` — `is null` form.
8. `s.Length > 0 && s is not null` — `is not null` form.

**Negative (must NOT flag):**
9. `a != null && a.Length > 0` — correct `&&` guard.
10. `s == null || s.Name == ""` — correct `||` guard.
11. `a?.Length > 0 && a != null` — null-conditional access is not a dereference.
12. `a != null && b != null` — no dereference at all.
13. `x > 0 && y < 0` — no nulls involved.

## 6. Acceptance Criteria

- All ≥10 snapshot tests committed and green.
- No regressions in the existing suite (`dotnet test --configuration Release`).
- Build clean under `TreatWarningsAsErrors=true` (CA analyzers).
- Rule discoverable via `dotnet run --project src/OpenVulScan.Cli -- rules list`.

## 7. Out of Scope

- Cross-statement null flow (covered by `ovs-2qi.15` null-deref set).
- Member-chain receivers beyond `this.field`.
- Assignment-as-null-check (`(a = X) != null`).
- Type-pattern guards (`S is SomeType t`) — only explicit null comparisons and
  `is null` / `is not null`.
