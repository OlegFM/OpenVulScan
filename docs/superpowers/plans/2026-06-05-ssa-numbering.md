# SSA Numbering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a semi-pruned SSA numbering layer on top of Roslyn's `ControlFlowGraph` so that `NullState` / `Constant` lattices and downstream DFA rules track locals, parameters, `this.field`, and Roslyn flow captures by `(TrackedKey, Version)` instead of by name.

**Architecture:**
- Pure pre-pass `SsaBuilder` runs over `ControlFlowGraph` + `SemanticModel`, produces immutable `SsaIndex`.
- Semi-pruned SSA: φ placed on blocks with ≥2 predecessors for "global" variables (≥2 def-sites). No dominance frontiers.
- Lattice transfers (`NullState`, `Constant`) re-key from `string` to `SsaId` and consume the index. `WorklistSolver` is untouched.
- `DataFlowRuleDispatcher` builds index per method, passes it through `DataFlowContext` to rules.

**Tech Stack:** .NET 10, Roslyn (`Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.FlowAnalysis`), xUnit, Verify (already present for snapshot tests).

**Spec:** `docs/superpowers/specs/2026-06-05-ssa-numbering-design.md`

**Beads issue:** `ovs-2qi.9`

---

## File Structure

### New files

```
src/OpenVulScan.Core/Ssa/
  TrackedKey.cs            — abstract record + 3 sealed records
  SsaId.cs                 — readonly record struct
  Phi.cs                   — Phi + PhiOperand
  SsaIndex.cs              — immutable lookup, populated by SsaBuilder
  SsaBuilder.cs            — three-pass construction

src/OpenVulScan.Core/Lattice/
  NullStateSsaTransfer.cs  — replaces NullStateMapTransfer
  ConstantSsaTransfer.cs   — replaces ConstantMapTransfer

tests/OpenVulScan.Core.Tests/Ssa/
  CfgTestHarness.cs        — shared helper: compile snippet, return (cfg, model)
  TrackedKeyEqualityTests.cs
  SsaIdTests.cs
  SsaBuilderStraightLineTests.cs
  SsaBuilderIfElseTests.cs
  SsaBuilderLoopTests.cs
  SsaBuilderSwitchTests.cs
  SsaBuilderNestedTests.cs
  SsaBuilderFieldKillTests.cs
  SsaBuilderCaptureTests.cs
  SsaBuilderShadowingTests.cs
  NullStateSsaTransferTests.cs
  ConstantSsaTransferTests.cs
```

### Modified files

```
src/OpenVulScan.RuleEngine/DataFlowContext.cs        — add SsaIndex property
src/OpenVulScan.RuleEngine/DataFlowRule.cs           — add CreateTransfer(SsaIndex), keep Transfer as fallback during migration
src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs — build SsaIndex per method, prefer CreateTransfer
src/OpenVulScan.Rules.DataFlow/V3022AlwaysTrueFalse.cs   — switch to CreateTransfer(SsaIndex) returning ConstantSsaTransfer
src/OpenVulScan.Rules.DataFlow/V3063PartialAlwaysTrueFalse.cs — same
```

### Files to delete (final migration task)

```
src/OpenVulScan.Core/Lattice/NullStateMapTransfer.cs
src/OpenVulScan.Core/Lattice/ConstantMapTransfer.cs
tests/OpenVulScan.Core.Tests/Lattice/NullStateMapTransferTests.cs   (if present)
tests/OpenVulScan.Core.Tests/Lattice/ConstantMapTransferTests.cs    (if present — verify)
```

---

## Tasks

### Task 1: Shared CFG test harness

**Files:**
- Create: `tests/OpenVulScan.Core.Tests/Ssa/CfgTestHarness.cs`

- [ ] **Step 1: Write the harness file**

```csharp
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan.Tests.Ssa;

internal static class CfgTestHarness
{
    public static (ControlFlowGraph Cfg, SemanticModel Model, IMethodBodyOperation Body) Compile(string snippet)
    {
        var tree = CSharpSyntaxTree.ParseText(snippet);
        var compilation = CSharpCompilation.Create(
            "Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var op = model.GetOperation(method) ?? throw new InvalidOperationException("No IOperation for method.");
        if (op is not IMethodBodyOperation body)
        {
            throw new InvalidOperationException($"Expected IMethodBodyOperation, got {op.GetType().Name}.");
        }

        var cfg = ControlFlowGraph.Create(body)
            ?? throw new InvalidOperationException("Failed to build CFG.");
        return (cfg, model, body);
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```
git add tests/OpenVulScan.Core.Tests/Ssa/CfgTestHarness.cs
git commit -m "test(ssa): add CfgTestHarness helper for SSA tests"
```

---

### Task 2: TrackedKey records

**Files:**
- Create: `src/OpenVulScan.Core/Ssa/TrackedKey.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/TrackedKeyEqualityTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class TrackedKeyEqualityTests
{
    [Fact]
    public void Symbol_EqualByUnderlyingSymbol_UsingSymbolEqualityComparer()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x = 1;
    }
}");
        var local = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().First();
        var symbol = model.GetDeclaredSymbol(local)!;

        var k1 = new TrackedKey.Symbol(symbol);
        var k2 = new TrackedKey.Symbol(symbol);

        Assert.Equal(k1, k2);
        Assert.Equal(k1.GetHashCode(), k2.GetHashCode());
    }

    [Fact]
    public void Symbol_NotEqualToInstanceField()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        int x = 0;
    }
}");
        var local = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().First();
        var localSymbol = (ISymbol)model.GetDeclaredSymbol(local)!;
        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!.GetMembers("f").First();

        TrackedKey k1 = new TrackedKey.Symbol(localSymbol);
        TrackedKey k2 = new TrackedKey.InstanceField(field);

        Assert.NotEqual(k1, k2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~TrackedKeyEqualityTests" --no-build`
Expected: Compile failure ("TrackedKey not found").

- [ ] **Step 3: Implement TrackedKey**

```csharp
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public abstract record TrackedKey
{
    public sealed record Symbol(ISymbol Variable) : TrackedKey
    {
        public bool Equals(Symbol? other)
            => other is not null && SymbolEqualityComparer.Default.Equals(Variable, other.Variable);

        public override int GetHashCode()
            => SymbolEqualityComparer.Default.GetHashCode(Variable);
    }

    public sealed record InstanceField(IFieldSymbol Field) : TrackedKey
    {
        public bool Equals(InstanceField? other)
            => other is not null && SymbolEqualityComparer.Default.Equals(Field, other.Field);

        public override int GetHashCode()
            => SymbolEqualityComparer.Default.GetHashCode(Field);
    }

    public sealed record Capture(CaptureId Id) : TrackedKey;
}
```

- [ ] **Step 4: Build + test**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~TrackedKeyEqualityTests"
```
Expected: 2 tests passed.

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.Core/Ssa/TrackedKey.cs tests/OpenVulScan.Core.Tests/Ssa/TrackedKeyEqualityTests.cs
git commit -m "feat(core): TrackedKey records for SSA (Symbol/InstanceField/Capture)"
```

---

### Task 3: SsaId record struct

**Files:**
- Create: `src/OpenVulScan.Core/Ssa/SsaId.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaIdTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaIdTests
{
    [Fact]
    public void EqualKeyAndVersion_AreEqual()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C { void M() { int x = 0; } }");
        var decl = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().First();
        var symbol = (ISymbol)model.GetDeclaredSymbol(decl)!;
        var key = new TrackedKey.Symbol(symbol);

        var a = new SsaId(key, 0);
        var b = new SsaId(key, 0);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentVersions_AreNotEqual()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C { void M() { int x = 0; } }");
        var decl = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().First();
        var symbol = (ISymbol)model.GetDeclaredSymbol(decl)!;
        var key = new TrackedKey.Symbol(symbol);

        var a = new SsaId(key, 0);
        var b = new SsaId(key, 1);

        Assert.NotEqual(a, b);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaIdTests" --no-build`
Expected: Compile failure ("SsaId not found").

- [ ] **Step 3: Implement SsaId**

```csharp
namespace OpenVulScan;

public readonly record struct SsaId(TrackedKey Key, int Version);
```

- [ ] **Step 4: Build + test**

Run:
```
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaIdTests"
```
Expected: 2 tests passed.

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.Core/Ssa/SsaId.cs tests/OpenVulScan.Core.Tests/Ssa/SsaIdTests.cs
git commit -m "feat(core): SsaId(TrackedKey, version) record struct"
```

---

### Task 4: Phi + PhiOperand

**Files:**
- Create: `src/OpenVulScan.Core/Ssa/Phi.cs`

- [ ] **Step 1: Implement Phi**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed record Phi(SsaId Result, ImmutableArray<PhiOperand> Operands);

public readonly record struct PhiOperand(BasicBlock PredecessorBlock, SsaId Version);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/OpenVulScan.Core/Ssa/Phi.cs
git commit -m "feat(core): Phi record + PhiOperand for SSA"
```

Note: equality of `Phi` records is structural; `ImmutableArray<>` equality is reference-based, which is acceptable here because each `Phi` instance is built once by `SsaBuilder` and never compared by value across builds.

---

### Task 5: SsaIndex skeleton (empty)

**Files:**
- Create: `src/OpenVulScan.Core/Ssa/SsaIndex.cs`

- [ ] **Step 1: Implement the empty/lookup skeleton**

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed class SsaIndex
{
    private readonly ImmutableDictionary<IOperation, SsaId> _definitions;
    private readonly ImmutableDictionary<(IOperation Op, TrackedKey Key), SsaId> _uses;
    private readonly ImmutableDictionary<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>> _entryVersions;
    private readonly ImmutableDictionary<BasicBlock, ImmutableArray<Phi>> _phis;
    private readonly ImmutableDictionary<TrackedKey, ImmutableArray<SsaId>> _allVersions;

    internal SsaIndex(
        ImmutableDictionary<IOperation, SsaId> definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId> uses,
        ImmutableDictionary<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>> entryVersions,
        ImmutableDictionary<BasicBlock, ImmutableArray<Phi>> phis,
        ImmutableDictionary<TrackedKey, ImmutableArray<SsaId>> allVersions)
    {
        _definitions = definitions;
        _uses = uses;
        _entryVersions = entryVersions;
        _phis = phis;
        _allVersions = allVersions;
    }

    public static SsaIndex Empty { get; } = new(
        ImmutableDictionary<IOperation, SsaId>.Empty,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Empty,
        ImmutableDictionary<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>>.Empty,
        ImmutableDictionary<BasicBlock, ImmutableArray<Phi>>.Empty,
        ImmutableDictionary<TrackedKey, ImmutableArray<SsaId>>.Empty);

    public SsaId? DefinitionAt(IOperation op)
        => _definitions.TryGetValue(op, out var id) ? id : null;

    public SsaId? UseAt(IOperation op, TrackedKey key)
        => _uses.TryGetValue((op, key), out var id) ? id : null;

    public IReadOnlyDictionary<TrackedKey, SsaId> EntryVersions(BasicBlock block)
        => _entryVersions.TryGetValue(block, out var dict)
            ? (IReadOnlyDictionary<TrackedKey, SsaId>)dict
            : ImmutableDictionary<TrackedKey, SsaId>.Empty;

    public IReadOnlyList<Phi> PhisAt(BasicBlock block)
        => _phis.TryGetValue(block, out var arr)
            ? (IReadOnlyList<Phi>)arr
            : ImmutableArray<Phi>.Empty;

    public IReadOnlyList<SsaId> AllVersions(TrackedKey key)
        => _allVersions.TryGetValue(key, out var arr)
            ? (IReadOnlyList<SsaId>)arr
            : ImmutableArray<SsaId>.Empty;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/OpenVulScan.Core/Ssa/SsaIndex.cs
git commit -m "feat(core): SsaIndex immutable lookup skeleton"
```

---

### Task 6: SsaBuilder — straight-line versioning (Pass 1 + minimal Pass 2)

**Files:**
- Create: `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderStraightLineTests.cs`

This task covers Pass 1 (def collection) and a minimal Pass 2 for blocks with ≤1 predecessor. φ placement and back-edges are added in later tasks.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderStraightLineTests
{
    [Fact]
    public void TwoSequentialDefs_GetDistinctVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x = 1;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var defs = cfg.Blocks
            .SelectMany(b => b.Operations)
            .SelectMany(EnumerateAllOps)
            .Select(op => index.DefinitionAt(op))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        Assert.Equal(2, defs.Count);
        Assert.Equal(0, defs[0].Version);
        Assert.Equal(1, defs[1].Version);
        Assert.Equal(defs[0].Key, defs[1].Key);
    }

    [Fact]
    public void ParameterIsDefinedAtEntry_Version0()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(string s)
    {
        var len = s.Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var param = (IParameterSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("M").OfType<IMethodSymbol>().First().Parameters[0];

        var versions = index.AllVersions(new TrackedKey.Symbol(param));
        Assert.NotEmpty(versions);
        Assert.Equal(0, versions[0].Version);
    }

    private static System.Collections.Generic.IEnumerable<IOperation> EnumerateAllOps(IOperation op)
    {
        yield return op;
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            foreach (var d in EnumerateAllOps(child))
                yield return d;
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderStraightLineTests" --no-build`
Expected: Compile failure ("SsaBuilder not found").

- [ ] **Step 3: Implement SsaBuilder for straight-line code**

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public static class SsaBuilder
{
    public static SsaIndex Build(ControlFlowGraph cfg, SemanticModel model)
    {
        var definitions = ImmutableDictionary.CreateBuilder<IOperation, SsaId>();
        var uses = ImmutableDictionary.CreateBuilder<(IOperation, TrackedKey), SsaId>();
        var entryVersions = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>>();
        var phis = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableArray<Phi>>();
        var allVersions = new Dictionary<TrackedKey, List<SsaId>>();

        var nextVersion = new Dictionary<TrackedKey, int>();
        SsaId NewVersion(TrackedKey key)
        {
            if (!nextVersion.TryGetValue(key, out var v)) v = 0;
            nextVersion[key] = v + 1;
            var id = new SsaId(key, v);
            if (!allVersions.TryGetValue(key, out var list))
            {
                list = new List<SsaId>();
                allVersions[key] = list;
            }
            list.Add(id);
            return id;
        }

        // Pass 0: define parameters at entry block, version 0.
        var current = new Dictionary<TrackedKey, SsaId>();
        var entryBlock = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Entry);
        var methodSymbol = (IMethodSymbol?)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .First());
        if (methodSymbol is not null)
        {
            foreach (var p in methodSymbol.Parameters)
            {
                var key = new TrackedKey.Symbol((ISymbol)p);
                var id = NewVersion(key);
                current[key] = id;
            }
        }
        entryVersions[entryBlock] = ImmutableDictionary.CreateRange(current);

        // Pass 1+2: visit blocks in CFG order. φ handling is added in Task 7+.
        var blockEntry = new Dictionary<BasicBlock, Dictionary<TrackedKey, SsaId>>
        {
            [entryBlock] = current,
        };

        foreach (var block in cfg.Blocks)
        {
            if (block.Kind == BasicBlockKind.Entry) continue;

            // Single-predecessor: inherit out-state.
            if (block.Predecessors.Length == 1
                && blockEntry.TryGetValue(block.Predecessors[0].Source, out var predOut))
            {
                current = new Dictionary<TrackedKey, SsaId>(predOut);
            }
            else
            {
                current = new Dictionary<TrackedKey, SsaId>();
            }

            entryVersions[block] = ImmutableDictionary.CreateRange(current);

            foreach (var op in block.Operations.SelectMany(EnumerateAllOps))
            {
                ProcessOperation(op, current, NewVersion, definitions, uses);
            }
            if (block.BranchValue is not null)
            {
                foreach (var op in EnumerateAllOps(block.BranchValue))
                {
                    ProcessOperation(op, current, NewVersion, definitions, uses);
                }
            }

            blockEntry[block] = current;
        }

        var allVersionsImmutable = allVersions.ToImmutableDictionary(
            kv => kv.Key,
            kv => kv.Value.ToImmutableArray());

        return new SsaIndex(
            definitions.ToImmutable(),
            uses.ToImmutable(),
            entryVersions.ToImmutable(),
            phis.ToImmutable(),
            allVersionsImmutable);
    }

    private static void ProcessOperation(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        System.Func<TrackedKey, SsaId> newVersion,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses)
    {
        switch (op)
        {
            case IVariableDeclaratorOperation varDecl when varDecl.Symbol is ILocalSymbol local:
            {
                var key = new TrackedKey.Symbol((ISymbol)local);
                var id = newVersion(key);
                current[key] = id;
                definitions[op] = id;
                break;
            }
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation lref }:
            {
                var key = new TrackedKey.Symbol((ISymbol)lref.Local);
                var id = newVersion(key);
                current[key] = id;
                definitions[op] = id;
                break;
            }
            case ISimpleAssignmentOperation { Target: IParameterReferenceOperation pref }:
            {
                var key = new TrackedKey.Symbol((ISymbol)pref.Parameter);
                var id = newVersion(key);
                current[key] = id;
                definitions[op] = id;
                break;
            }
            case ICompoundAssignmentOperation { Target: ILocalReferenceOperation lref }:
            {
                var key = new TrackedKey.Symbol((ISymbol)lref.Local);
                var id = newVersion(key);
                current[key] = id;
                definitions[op] = id;
                break;
            }
            case ILocalReferenceOperation lref:
            {
                var key = new TrackedKey.Symbol((ISymbol)lref.Local);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IParameterReferenceOperation pref:
            {
                var key = new TrackedKey.Symbol((ISymbol)pref.Parameter);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
        }
    }

    private static IEnumerable<IOperation> EnumerateAllOps(IOperation op)
    {
        yield return op;
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            foreach (var d in EnumerateAllOps(child))
                yield return d;
        }
    }
}
```

- [ ] **Step 4: Build + test**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderStraightLineTests"
```
Expected: 2 tests passed.

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.Core/Ssa/SsaBuilder.cs tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderStraightLineTests.cs
git commit -m "feat(core): SsaBuilder straight-line versioning + parameter entry"
```

---

### Task 7: SsaBuilder — if-else with φ placement

**Files:**
- Modify: `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderIfElseTests.cs`

This task adds the semi-pruned φ placement (Pass 1 globals + Pass 2 entry φ + Pass 3 operand binding).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderIfElseTests
{
    [Fact]
    public void IfElse_PlacesPhiAtMerge_WithBothBranchVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool cond)
    {
        int x = 0;
        if (cond) x = 1;
        else x = 2;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Find merge block: ≥2 predecessors, not entry.
        var mergeBlock = cfg.Blocks.First(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry);
        var phis = index.PhisAt(mergeBlock);
        Assert.Single(phis);

        var phi = phis[0];
        Assert.Equal(2, phi.Operands.Length);
        // φ result version must be greater than both operand versions.
        Assert.All(phi.Operands, o => Assert.True(o.Version.Version < phi.Result.Version));
    }

    [Fact]
    public void IfWithoutElse_PlacesPhi_WithEntryAndIfBranchVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool cond)
    {
        int x = 0;
        if (cond) x = 1;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var mergeBlock = cfg.Blocks.First(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry);
        var phis = index.PhisAt(mergeBlock);
        Assert.Single(phis);
        Assert.Equal(2, phis[0].Operands.Length);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderIfElseTests" --no-build`
Expected: 2 tests failed (empty `PhisAt`).

- [ ] **Step 3: Replace SsaBuilder.cs with the full three-pass implementation**

Replace the entire `src/OpenVulScan.Core/Ssa/SsaBuilder.cs` file:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public static class SsaBuilder
{
    public static SsaIndex Build(ControlFlowGraph cfg, SemanticModel model)
    {
        // --------- Pass 1: collect def-sites and globals ---------
        var defSites = new Dictionary<TrackedKey, int>(); // count of def-sites per key
        var perBlockDefs = new Dictionary<BasicBlock, List<TrackedKey>>();

        foreach (var block in cfg.Blocks)
        {
            var list = new List<TrackedKey>();
            foreach (var op in EnumerateBlockOps(block))
            {
                var key = TryGetDefinitionKey(op);
                if (key is null) continue;
                list.Add(key);
                defSites[key] = defSites.GetValueOrDefault(key, 0) + 1;
            }
            perBlockDefs[block] = list;
        }

        // Globals: keys with ≥2 def-sites (semi-pruned criterion).
        var globals = new HashSet<TrackedKey>(defSites.Where(kv => kv.Value >= 2).Select(kv => kv.Key));

        // --------- Pass 2: versioning + φ placement ---------
        var definitions = ImmutableDictionary.CreateBuilder<IOperation, SsaId>();
        var uses = ImmutableDictionary.CreateBuilder<(IOperation, TrackedKey), SsaId>();
        var entryVersions = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>>();
        var phisBuilder = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableArray<Phi>>();
        var allVersions = new Dictionary<TrackedKey, List<SsaId>>();

        var nextVersion = new Dictionary<TrackedKey, int>();
        SsaId NewVersion(TrackedKey key)
        {
            var v = nextVersion.GetValueOrDefault(key, 0);
            nextVersion[key] = v + 1;
            var id = new SsaId(key, v);
            if (!allVersions.TryGetValue(key, out var list))
            {
                list = new List<SsaId>();
                allVersions[key] = list;
            }
            list.Add(id);
            return id;
        }

        // Entry block: define parameters at version 0.
        var blockOut = new Dictionary<BasicBlock, Dictionary<TrackedKey, SsaId>>();
        var phisToBind = new List<(BasicBlock Block, TrackedKey Key, SsaId Result)>();

        var entryBlock = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Entry);
        var current = new Dictionary<TrackedKey, SsaId>();
        var methodSymbol = TryGetMethodSymbol(model);
        if (methodSymbol is not null)
        {
            foreach (var p in methodSymbol.Parameters)
            {
                var key = new TrackedKey.Symbol((ISymbol)p);
                var id = NewVersion(key);
                current[key] = id;
            }
        }
        entryVersions[entryBlock] = ImmutableDictionary.CreateRange(current);
        blockOut[entryBlock] = current;

        // Visit non-entry blocks in CFG order (Roslyn's Blocks list is already an RPO-friendly order).
        foreach (var block in cfg.Blocks)
        {
            if (block.Kind == BasicBlockKind.Entry) continue;

            current = new Dictionary<TrackedKey, SsaId>();
            var blockPhis = ImmutableArray.CreateBuilder<Phi>();

            if (block.Predecessors.Length >= 2)
            {
                // Place φ for every global key.
                foreach (var key in globals)
                {
                    var result = NewVersion(key);
                    current[key] = result;
                    blockPhis.Add(new Phi(result, ImmutableArray<PhiOperand>.Empty));
                    phisToBind.Add((block, key, result));
                }
            }
            else if (block.Predecessors.Length == 1
                     && blockOut.TryGetValue(block.Predecessors[0].Source, out var predOut))
            {
                current = new Dictionary<TrackedKey, SsaId>(predOut);
            }
            // 0 predecessors → empty `current`; unreachable but harmless.

            entryVersions[block] = ImmutableDictionary.CreateRange(current);
            if (blockPhis.Count > 0)
            {
                phisBuilder[block] = blockPhis.ToImmutable();
            }

            foreach (var op in EnumerateBlockOps(block))
            {
                ProcessOperation(op, current, NewVersion, definitions, uses);
            }

            blockOut[block] = current;
        }

        // --------- Pass 3: bind φ operands ---------
        if (phisToBind.Count > 0)
        {
            var rebuilt = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableArray<Phi>>();
            foreach (var (block, _, _) in phisToBind.GroupBy(t => t.Block).Select(g => (g.Key, default(TrackedKey)!, default(SsaId))))
            {
                rebuilt[block] = ImmutableArray<Phi>.Empty;
            }

            // Build per-block list of bound Phi.
            var grouped = phisToBind.GroupBy(t => t.Block);
            foreach (var group in grouped)
            {
                var block = group.Key;
                var bound = ImmutableArray.CreateBuilder<Phi>();
                foreach (var (_, key, result) in group)
                {
                    var operands = ImmutableArray.CreateBuilder<PhiOperand>();
                    foreach (var predBranch in block.Predecessors)
                    {
                        var predBlock = predBranch.Source;
                        if (blockOut.TryGetValue(predBlock, out var predOut)
                            && predOut.TryGetValue(key, out var predVersion))
                        {
                            operands.Add(new PhiOperand(predBlock, predVersion));
                        }
                        // If pred has no def for this key, skip — undefined-on-this-path; consumers treat missing as Top.
                    }
                    bound.Add(new Phi(result, operands.ToImmutable()));
                }
                rebuilt[block] = bound.ToImmutable();
            }

            // Replace old φ entries with bound ones.
            foreach (var kv in rebuilt)
                phisBuilder[kv.Key] = kv.Value;
        }

        var allVersionsImmutable = allVersions.ToImmutableDictionary(
            kv => kv.Key,
            kv => kv.Value.ToImmutableArray());

        return new SsaIndex(
            definitions.ToImmutable(),
            uses.ToImmutable(),
            entryVersions.ToImmutable(),
            phisBuilder.ToImmutable(),
            allVersionsImmutable);
    }

    // --- helpers ---

    private static IMethodSymbol? TryGetMethodSymbol(SemanticModel model)
    {
        var methodSyntax = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .FirstOrDefault();
        return methodSyntax is null ? null : (IMethodSymbol?)model.GetDeclaredSymbol(methodSyntax);
    }

    private static TrackedKey? TryGetDefinitionKey(IOperation op) => op switch
    {
        IVariableDeclaratorOperation { Symbol: ILocalSymbol local } =>
            new TrackedKey.Symbol((ISymbol)local),
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation lref } =>
            new TrackedKey.Symbol((ISymbol)lref.Local),
        ISimpleAssignmentOperation { Target: IParameterReferenceOperation pref } =>
            new TrackedKey.Symbol((ISymbol)pref.Parameter),
        ICompoundAssignmentOperation { Target: ILocalReferenceOperation lref } =>
            new TrackedKey.Symbol((ISymbol)lref.Local),
        _ => null,
    };

    private static void ProcessOperation(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        System.Func<TrackedKey, SsaId> newVersion,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses)
    {
        var defKey = TryGetDefinitionKey(op);
        if (defKey is not null)
        {
            var id = newVersion(defKey);
            current[defKey] = id;
            definitions[op] = id;
            return;
        }

        switch (op)
        {
            case ILocalReferenceOperation lref:
            {
                var key = new TrackedKey.Symbol((ISymbol)lref.Local);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IParameterReferenceOperation pref:
            {
                var key = new TrackedKey.Symbol((ISymbol)pref.Parameter);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
        }
    }

    private static IEnumerable<IOperation> EnumerateBlockOps(BasicBlock block)
    {
        foreach (var op in block.Operations)
        {
            foreach (var d in EnumerateAllOps(op))
                yield return d;
        }
        if (block.BranchValue is not null)
        {
            foreach (var d in EnumerateAllOps(block.BranchValue))
                yield return d;
        }
    }

    private static IEnumerable<IOperation> EnumerateAllOps(IOperation op)
    {
        yield return op;
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            foreach (var d in EnumerateAllOps(child))
                yield return d;
        }
    }
}
```

- [ ] **Step 4: Build + test (both old + new should pass)**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilder"
```
Expected: 4 tests passed (2 straight-line + 2 if-else).

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.Core/Ssa/SsaBuilder.cs tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderIfElseTests.cs
git commit -m "feat(core): SsaBuilder semi-pruned phi placement for if-else"
```

---

### Task 8: SsaBuilder — while loop (back-edges)

**Files:**
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderLoopTests.cs`

No implementation change is expected — Pass 3 binds back-edge operands after all blocks are visited. This task is a regression test that the implementation already handles loops correctly.

- [ ] **Step 1: Write the test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderLoopTests
{
    [Fact]
    public void WhileLoop_PlacesPhiOnHeader_WithEntryAndBackEdgeOperands()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        while (x < 10) { x++; }
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Loop header: block with a back-edge predecessor (pred ordinal >= block ordinal).
        var header = cfg.Blocks.First(b =>
            b.Predecessors.Length >= 2 &&
            b.Predecessors.Any(p => p.Source.Ordinal >= b.Ordinal));

        var phis = index.PhisAt(header);
        Assert.NotEmpty(phis);
        Assert.Contains(phis, p => p.Operands.Length >= 2);
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderLoopTests"`
Expected: 1 test passed.

- [ ] **Step 3: Commit**

```
git add tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderLoopTests.cs
git commit -m "test(core): SSA phi placement for while-loop header back-edge"
```

If the test fails, the most likely cause is that `cfg.Blocks` iteration order leaves the header's back-edge predecessor not yet in `blockOut` at Pass 3 time. Fix: ensure Pass 3 reads from the final `blockOut` map only after **all** non-entry blocks have been processed (already the case in Task 7 implementation).

---

### Task 9: SsaBuilder — switch

**Files:**
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderSwitchTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderSwitchTests
{
    [Fact]
    public void Switch_PhiHasOneOperandPerCaseOnMerge()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(int kind)
    {
        int x = 0;
        switch (kind)
        {
            case 1: x = 1; break;
            case 2: x = 2; break;
            default: x = 3; break;
        }
        var y = x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Merge block after switch: more than 2 predecessors.
        var mergeBlock = cfg.Blocks
            .Where(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry)
            .OrderByDescending(b => b.Predecessors.Length)
            .First();

        var phis = index.PhisAt(mergeBlock);
        Assert.NotEmpty(phis);
        Assert.Equal(mergeBlock.Predecessors.Length, phis[0].Operands.Length);
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderSwitchTests"`
Expected: 1 test passed.

- [ ] **Step 3: Commit**

```
git add tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderSwitchTests.cs
git commit -m "test(core): SSA phi placement for switch merge"
```

---

### Task 10: SsaBuilder — nested control flow

**Files:**
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderNestedTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderNestedTests
{
    [Fact]
    public void IfInsideWhile_PlacesPhiOnInnerMergeAndOuterHeader()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool c)
    {
        int x = 0;
        while (x < 10)
        {
            if (c) x = 1;
            else x = 2;
        }
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var mergeBlocks = cfg.Blocks
            .Where(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry)
            .ToList();

        // Expect at least: while-header and if/else merge.
        Assert.True(mergeBlocks.Count >= 2);
        Assert.All(mergeBlocks, b =>
        {
            var phis = index.PhisAt(b);
            Assert.NotEmpty(phis);
        });
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderNestedTests"`
Expected: 1 test passed.

- [ ] **Step 3: Commit**

```
git add tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderNestedTests.cs
git commit -m "test(core): SSA phi placement for nested if-inside-while"
```

---

### Task 11: SsaBuilder — this.field tracking + kill on method call

**Files:**
- Modify: `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderFieldKillTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderFieldKillTests
{
    [Fact]
    public void ThisField_GetsNewVersionAfterInstanceMethodCall()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void Side() { }
    void M()
    {
        this.f = 1;
        Side();
        var y = this.f;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var versions = index.AllVersions(new TrackedKey.InstanceField(field));

        // Expect ≥3 versions: initial assign, kill after Side(), and the read use does NOT add a version.
        Assert.True(versions.Count >= 2, $"Expected ≥2 versions, got {versions.Count}");
    }

    [Fact]
    public void ThisField_NotKilledByStaticMethodCall()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    static void Pure() { }
    void M()
    {
        this.f = 1;
        Pure();
        var y = this.f;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var versions = index.AllVersions(new TrackedKey.InstanceField(field));

        // Only 1 def (the assignment); no kill.
        Assert.Single(versions);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderFieldKillTests"`
Expected: first test fails (no field tracking yet); second probably passes (no defs at all).

- [ ] **Step 3: Extend `TryGetDefinitionKey` and `ProcessOperation` in `SsaBuilder.cs`**

Add a `this.field` assignment branch to `TryGetDefinitionKey`:

```csharp
private static TrackedKey? TryGetDefinitionKey(IOperation op) => op switch
{
    IVariableDeclaratorOperation { Symbol: ILocalSymbol local } =>
        new TrackedKey.Symbol((ISymbol)local),
    ISimpleAssignmentOperation { Target: ILocalReferenceOperation lref } =>
        new TrackedKey.Symbol((ISymbol)lref.Local),
    ISimpleAssignmentOperation { Target: IParameterReferenceOperation pref } =>
        new TrackedKey.Symbol((ISymbol)pref.Parameter),
    ISimpleAssignmentOperation
    {
        Target: IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field }
    } => new TrackedKey.InstanceField(field),
    ICompoundAssignmentOperation { Target: ILocalReferenceOperation lref } =>
        new TrackedKey.Symbol((ISymbol)lref.Local),
    ICompoundAssignmentOperation
    {
        Target: IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field2 }
    } => new TrackedKey.InstanceField(field2),
    _ => null,
};
```

In `ProcessOperation`, add a kill-on-invocation case after the def-check:

```csharp
if (IsThisAccessingInvocation(op))
{
    foreach (var key in current.Keys.OfType<TrackedKey.InstanceField>().ToList())
    {
        var id = newVersion(key);
        current[key] = id;
    }
    return;
}
```

Add the helper at the bottom of `SsaBuilder`:

```csharp
private static bool IsThisAccessingInvocation(IOperation op)
{
    if (op is IInvocationOperation inv)
    {
        // Static method without `this` parameter → safe.
        if (inv.TargetMethod.IsStatic)
        {
            // But `this` could be passed as an argument.
            foreach (var arg in inv.Arguments)
            {
                if (arg.Value is IInstanceReferenceOperation)
                    return true;
            }
            return false;
        }
        // Instance method: if receiver is `this` or implicit `this`, it can mutate fields.
        return inv.Instance is IInstanceReferenceOperation or null;
    }
    if (op is IObjectCreationOperation create)
    {
        foreach (var arg in create.Arguments)
        {
            if (arg.Value is IInstanceReferenceOperation)
                return true;
        }
        return false;
    }
    return false;
}
```

Also add field-reference use tracking in `ProcessOperation`:

```csharp
case IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field }:
{
    var key = new TrackedKey.InstanceField(field);
    if (current.TryGetValue(key, out var id))
        uses[(op, key)] = id;
    break;
}
```

(Insert after the `IParameterReferenceOperation` case.)

- [ ] **Step 4: Build + test**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderFieldKillTests"
```
Expected: 2 tests passed.

- [ ] **Step 5: Re-run the full SSA test suite to confirm no regression**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilder"`
Expected: All SSA tests passed.

- [ ] **Step 6: Commit**

```
git add src/OpenVulScan.Core/Ssa/SsaBuilder.cs tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderFieldKillTests.cs
git commit -m "feat(core): SsaBuilder this.field tracking and method-call invalidation"
```

---

### Task 12: SsaBuilder — flow captures

**Files:**
- Modify: `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderCaptureTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderCaptureTests
{
    [Fact]
    public void FlowCapture_GetsSsaIdWithVersionZero()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int Foo(string? s) => (s ?? """").Length;
}");
        var index = SsaBuilder.Build(cfg, model);

        var captureOp = cfg.Blocks
            .SelectMany(b => b.Operations)
            .SelectMany(EnumerateOps)
            .OfType<IFlowCaptureOperation>()
            .FirstOrDefault();
        Assert.NotNull(captureOp);

        var id = index.DefinitionAt(captureOp);
        Assert.NotNull(id);
        Assert.Equal(0, id!.Value.Version);
        Assert.IsType<TrackedKey.Capture>(id.Value.Key);
    }

    private static System.Collections.Generic.IEnumerable<Microsoft.CodeAnalysis.IOperation> EnumerateOps(Microsoft.CodeAnalysis.IOperation op)
    {
        yield return op;
        foreach (var c in op.ChildOperations)
        {
            if (c is null) continue;
            foreach (var d in EnumerateOps(c)) yield return d;
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderCaptureTests"`
Expected: test fails — capture not yet tracked.

- [ ] **Step 3: Extend `TryGetDefinitionKey` and `ProcessOperation` in `SsaBuilder.cs`**

In `TryGetDefinitionKey`, add a capture branch (note: captures have their own `Id`, version is implicitly 0):

```csharp
IFlowCaptureOperation flow => new TrackedKey.Capture(flow.Id),
```

In `ProcessOperation`, after the def-branch (which now allocates via `NewVersion`), captures should always get version `0` regardless of order. Special-case them:

```csharp
if (op is IFlowCaptureOperation flow)
{
    var key = new TrackedKey.Capture(flow.Id);
    // Capture has at most one def-site by Roslyn semantics → version always 0.
    var id = new SsaId(key, 0);
    current[key] = id;
    definitions[op] = id;
    if (!allVersionsLocal(key)) allVersionsRegister(key, id);
    return;
}
```

Adapt `NewVersion`/`allVersions` access via two small inline helpers (`allVersionsLocal`, `allVersionsRegister`) defined as closures over the same dictionary used in `Build`. Concretely, refactor `NewVersion` so callers can opt out of incrementing:

```csharp
SsaId AllocateExplicit(TrackedKey key, int version)
{
    var id = new SsaId(key, version);
    if (!allVersions.TryGetValue(key, out var list))
    {
        list = new List<SsaId>();
        allVersions[key] = list;
    }
    if (!list.Contains(id)) list.Add(id);
    return id;
}
```

Then in `ProcessOperation` for `IFlowCaptureOperation`, call `AllocateExplicit(key, 0)`. Pass `AllocateExplicit` down alongside `NewVersion`.

Also handle `IFlowCaptureReferenceOperation` for uses:

```csharp
case IFlowCaptureReferenceOperation flowRef:
{
    var key = new TrackedKey.Capture(flowRef.Id);
    if (current.TryGetValue(key, out var id))
        uses[(op, key)] = id;
    break;
}
```

- [ ] **Step 4: Build + test**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderCaptureTests"
```
Expected: 1 test passed.

- [ ] **Step 5: Re-run full SSA suite**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilder"`
Expected: All SSA tests passed.

- [ ] **Step 6: Commit**

```
git add src/OpenVulScan.Core/Ssa/SsaBuilder.cs tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderCaptureTests.cs
git commit -m "feat(core): SsaBuilder integrates Roslyn flow captures via TrackedKey.Capture"
```

---

### Task 13: SsaBuilder — shadowing regression

**Files:**
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderShadowingTests.cs`

No implementation change — ILocalSymbol identity already distinguishes shadowed locals. This task is a regression test.

- [ ] **Step 1: Write the test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderShadowingTests
{
    [Fact]
    public void ShadowedLocals_AreTrackedSeparately()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool c)
    {
        int x = 1;
        if (c)
        {
            int x = 2;
            var y = x;
        }
        var z = x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var declarators = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
            .Where(v => v.Identifier.Text == "x")
            .Select(v => (ISymbol)model.GetDeclaredSymbol(v)!)
            .ToList();

        Assert.Equal(2, declarators.Count);
        var k1 = new TrackedKey.Symbol(declarators[0]);
        var k2 = new TrackedKey.Symbol(declarators[1]);
        Assert.NotEqual(k1, k2);

        Assert.NotEmpty(index.AllVersions(k1));
        Assert.NotEmpty(index.AllVersions(k2));
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderShadowingTests"`
Expected: 1 test passed.

- [ ] **Step 3: Commit**

```
git add tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderShadowingTests.cs
git commit -m "test(core): SSA distinguishes shadowed locals via ILocalSymbol identity"
```

---

### Task 14: NullStateSsaTransfer

**Files:**
- Create: `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/NullStateSsaTransferTests.cs`

This task adds the new SSA-aware transfer **alongside** `NullStateMapTransfer`. Old transfer stays until Task 18.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class NullStateSsaTransferTests
{
    [Fact]
    public void Assignment_OfNullLiteral_TracksDefinitelyNull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        string s = null;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, NullState>.Empty;

        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);

        var localSym = (ISymbol)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
                .First())!;
        var versions = index.AllVersions(new TrackedKey.Symbol(localSym));
        Assert.Single(versions);

        Assert.Equal(NullState.DefinitelyNull, state[versions[0]]);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~NullStateSsaTransferTests" --no-build`
Expected: Compile failure ("NullStateSsaTransfer not found").

- [ ] **Step 3: Implement NullStateSsaTransfer**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public sealed class NullStateSsaTransfer : ITransfer<ImmutableDictionary<SsaId, NullState>>
{
    private readonly SsaIndex _ssa;

    public NullStateSsaTransfer(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    public ImmutableDictionary<SsaId, NullState> Apply(
        ImmutableDictionary<SsaId, NullState> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        var def = _ssa.DefinitionAt(operation);
        if (def is null) return state;

        var valueState = operation switch
        {
            IVariableDeclaratorOperation { Initializer: { } init } => Evaluate(init.Value, state),
            ISimpleAssignmentOperation assignment => Evaluate(assignment.Value, state),
            ICompoundAssignmentOperation => NullState.Unknown,
            _ => NullState.Unknown,
        };
        return state.SetItem(def.Value, valueState);
    }

    public ImmutableDictionary<SsaId, NullState> Apply(
        ImmutableDictionary<SsaId, NullState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Apply φ functions on entry first.
        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = NullState.Unknown;
            bool any = false;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                {
                    joined = any ? Join(joined, s) : s;
                    any = true;
                }
            }
            state = state.SetItem(phi.Result, any ? joined : NullState.Unknown);
        }

        foreach (var op in block.Operations.SelectMany(EnumerateOps))
            state = Apply(state, op);
        if (block.BranchValue is not null)
            foreach (var op in EnumerateOps(block.BranchValue))
                state = Apply(state, op);
        return state;
    }

    private NullState Evaluate(IOperation expr, ImmutableDictionary<SsaId, NullState> state)
    {
        return expr switch
        {
            ILiteralOperation lit when lit.ConstantValue.HasValue && lit.ConstantValue.Value is null =>
                NullState.DefinitelyNull,
            ILocalReferenceOperation lref =>
                Lookup(lref, new TrackedKey.Symbol((ISymbol)lref.Local), state),
            IParameterReferenceOperation pref =>
                Lookup(pref, new TrackedKey.Symbol((ISymbol)pref.Parameter), state),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } fref =>
                Lookup(fref, new TrackedKey.InstanceField(fref.Field), state),
            IObjectCreationOperation => NullState.NotNull,
            IArrayCreationOperation => NullState.NotNull,
            IConversionOperation conv => Evaluate(conv.Operand, state),
            IParenthesizedOperation paren => Evaluate(paren.Operand, state),
            _ => NullState.Unknown,
        };
    }

    private NullState Lookup(IOperation op, TrackedKey key, ImmutableDictionary<SsaId, NullState> state)
    {
        var use = _ssa.UseAt(op, key);
        if (use is null) return NullState.Unknown;
        return state.TryGetValue(use.Value, out var s) ? s : NullState.Unknown;
    }

    private static readonly NullStateLattice _lattice = new();
    private static NullState Join(NullState a, NullState b) => _lattice.Join(a, b);

    private static IEnumerable<IOperation> EnumerateOps(IOperation op)
    {
        yield return op;
        foreach (var c in op.ChildOperations)
        {
            if (c is null) continue;
            foreach (var d in EnumerateOps(c)) yield return d;
        }
    }
}
```

- [ ] **Step 4: Build + test**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~NullStateSsaTransferTests"
```
Expected: 1 test passed.

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs tests/OpenVulScan.Core.Tests/Ssa/NullStateSsaTransferTests.cs
git commit -m "feat(core): NullStateSsaTransfer keyed by SsaId with phi support"
```

---

### Task 15: ConstantSsaTransfer

**Files:**
- Create: `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/ConstantSsaTransferTests.cs`

Symmetric to Task 14 but for `ConstantLatticeValue`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class ConstantSsaTransferTests
{
    [Fact]
    public void Assignment_OfIntegerLiteral_TracksConstantValue()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 42;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, ConstantLatticeValue>.Empty;

        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);

        var localSym = (ISymbol)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
                .First())!;
        var versions = index.AllVersions(new TrackedKey.Symbol(localSym));
        Assert.Single(versions);

        var value = state[versions[0]];
        Assert.Equal(LatticeElementKind.Const, value.Kind);
        Assert.Equal(42, value.Value);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~ConstantSsaTransferTests" --no-build`
Expected: Compile failure ("ConstantSsaTransfer not found").

- [ ] **Step 3: Implement ConstantSsaTransfer**

Create `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public sealed class ConstantSsaTransfer : ITransfer<ImmutableDictionary<SsaId, ConstantLatticeValue>>
{
    private static readonly ConstantLattice _lattice = new();
    private readonly SsaIndex _ssa;

    public ConstantSsaTransfer(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    public ImmutableDictionary<SsaId, ConstantLatticeValue> Apply(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        var def = _ssa.DefinitionAt(operation);
        if (def is null) return state;

        var value = operation switch
        {
            IVariableDeclaratorOperation { Initializer: { } init } => Evaluate(init.Value, state),
            ISimpleAssignmentOperation assignment => Evaluate(assignment.Value, state),
            _ => ConstantLatticeValue.Top,
        };
        return state.SetItem(def.Value, value);
    }

    public ImmutableDictionary<SsaId, ConstantLatticeValue> Apply(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = ConstantLatticeValue.Bottom;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                    joined = _lattice.Join(joined, s);
            }
            state = state.SetItem(phi.Result, joined);
        }

        foreach (var op in block.Operations.SelectMany(EnumerateOps))
            state = Apply(state, op);
        if (block.BranchValue is not null)
            foreach (var op in EnumerateOps(block.BranchValue))
                state = Apply(state, op);
        return state;
    }

    private ConstantLatticeValue Evaluate(IOperation expr, ImmutableDictionary<SsaId, ConstantLatticeValue> state)
    {
        return expr switch
        {
            ILiteralOperation lit when lit.ConstantValue.HasValue && lit.ConstantValue.Value is not null =>
                ConstantLatticeValue.Const(lit.ConstantValue.Value),
            ILocalReferenceOperation lref =>
                Lookup(lref, new TrackedKey.Symbol((ISymbol)lref.Local), state),
            IParameterReferenceOperation pref =>
                Lookup(pref, new TrackedKey.Symbol((ISymbol)pref.Parameter), state),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } fref =>
                Lookup(fref, new TrackedKey.InstanceField(fref.Field), state),
            IConversionOperation conv => Evaluate(conv.Operand, state),
            IParenthesizedOperation paren => Evaluate(paren.Operand, state),
            _ => ConstantLatticeValue.Top,
        };
    }

    private ConstantLatticeValue Lookup(IOperation op, TrackedKey key, ImmutableDictionary<SsaId, ConstantLatticeValue> state)
    {
        var use = _ssa.UseAt(op, key);
        if (use is null) return ConstantLatticeValue.Top;
        return state.TryGetValue(use.Value, out var s) ? s : ConstantLatticeValue.Top;
    }

    private static IEnumerable<IOperation> EnumerateOps(IOperation op)
    {
        yield return op;
        foreach (var c in op.ChildOperations)
        {
            if (c is null) continue;
            foreach (var d in EnumerateOps(c)) yield return d;
        }
    }
}
```

- [ ] **Step 4: Build + test**

Run:
```
dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj
dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~ConstantSsaTransferTests"
```
Expected: 1 test passed.

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs tests/OpenVulScan.Core.Tests/Ssa/ConstantSsaTransferTests.cs
git commit -m "feat(core): ConstantSsaTransfer keyed by SsaId with phi support"
```

---

### Task 16: DataFlowRule + DataFlowContext SSA integration (additive)

**Files:**
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRule.cs`
- Modify: `src/OpenVulScan.RuleEngine/DataFlowContext.cs`

Add SSA hooks without removing the existing API yet.

- [ ] **Step 1: Add `SsaIndex` property to DataFlowContext**

Read `src/OpenVulScan.RuleEngine/DataFlowContext.cs` first to see its constructor signature. Then add:

```csharp
public SsaIndex SsaIndex { get; }
```

Update the constructor to accept `SsaIndex` (with a sensible default of `SsaIndex.Empty` for callers that haven't migrated yet — there should be none outside `DataFlowRuleDispatcher`, but the default keeps tests compiling).

- [ ] **Step 2: Add `CreateTransfer(SsaIndex)` to DataFlowRule**

In `src/OpenVulScan.RuleEngine/DataFlowRule.cs`:

```csharp
// Default implementation: ignore SSA (legacy path).
public virtual ITransfer<TLattice> CreateTransfer(SsaIndex ssaIndex) => Transfer;
```

Keep `Transfer` abstract for now. After Task 17 migrates the dispatcher, we'll flip the abstractness in Task 18.

- [ ] **Step 3: Build entire solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run full test suite to verify no regressions**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```
git add src/OpenVulScan.RuleEngine/DataFlowRule.cs src/OpenVulScan.RuleEngine/DataFlowContext.cs
git commit -m "feat(rule-engine): SsaIndex on DataFlowContext + CreateTransfer hook on DataFlowRule"
```

---

### Task 17: DataFlowRuleDispatcher — build and pass SsaIndex

**Files:**
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs`

- [ ] **Step 1: Build SsaIndex per method, pass through transfer + context**

Update `DataFlowRuleDispatcher<TLattice>.Run`:

```csharp
foreach (var method in tree.GetRoot(cancellationToken).DescendantNodesAndSelf()
            .OfType<MethodDeclarationSyntax>())
{
    cancellationToken.ThrowIfCancellationRequested();
    var operation = model.GetOperation(method, cancellationToken);
    if (operation is not IMethodBodyOperation methodBody) continue;

    var cfg = ControlFlowGraph.Create(methodBody, cancellationToken);
    var ssaIndex = SsaBuilder.Build(cfg, model);

    foreach (var rule in _rules)
    {
        var transfer = rule.CreateTransfer(ssaIndex);
        var solver = new WorklistSolver<TLattice>(rule.Lattice, transfer, rule.EdgeRefiner);
        var result = solver.Solve(cfg, cancellationToken);

        foreach (var block in cfg.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = result.InStates[block];

            foreach (var op in GetAllOperations(block))
            {
                var context = new DataFlowContext(op, model, _compilation, ssaIndex, cancellationToken);
                rule.InvokeOnState(op, state, context);
                diagnostics.AddRange(context.Diagnostics);
                state = transfer.Apply(state, op);
            }
        }
    }
}
```

- [ ] **Step 2: Build entire solution**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 3: Run full test suite + V3022/V3063 snapshot regression**

Run: `dotnet test`
Expected: All tests pass. The dispatcher now builds SSA but rules still use legacy `Transfer` property (since they haven't overridden `CreateTransfer`), so behavior is unchanged.

- [ ] **Step 4: Commit**

```
git add src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs
git commit -m "feat(rule-engine): DataFlowRuleDispatcher builds SsaIndex per method"
```

---

### Task 18: Migrate V3022 and V3063 to SSA transfers

**Files:**
- Modify: `src/OpenVulScan.Rules.DataFlow/V3022AlwaysTrueFalse.cs`
- Modify: `src/OpenVulScan.Rules.DataFlow/V3063PartialAlwaysTrueFalse.cs`

- [ ] **Step 1: Read both rule files** to see how they currently expose `Transfer` and what lattice they use. Both are likely `DataFlowRule<ImmutableDictionary<string, ConstantLatticeValue>>`.

- [ ] **Step 2: Change their type parameter to `ImmutableDictionary<SsaId, ConstantLatticeValue>` and override `CreateTransfer(SsaIndex)`**

For each rule:

```csharp
public override ILattice<ImmutableDictionary<SsaId, ConstantLatticeValue>> Lattice
    => new MapLattice<SsaId, ConstantLatticeValue>(new ConstantLattice());

public override ITransfer<ImmutableDictionary<SsaId, ConstantLatticeValue>> CreateTransfer(SsaIndex ssaIndex)
    => new ConstantSsaTransfer(ssaIndex);

// The legacy Transfer property must still satisfy the abstract — return a placeholder that throws.
// It is never called because DataFlowRuleDispatcher prefers CreateTransfer.
public override ITransfer<ImmutableDictionary<SsaId, ConstantLatticeValue>> Transfer
    => throw new InvalidOperationException("Use CreateTransfer(SsaIndex) instead.");
```

Lookup logic inside `OnState`/handlers must shift from `state[name]` to `state[ssaIndex.UseAt(op, key).Value]`.

- [ ] **Step 3: Run V3022/V3063 snapshot tests**

Run:
```
dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3022Tests|FullyQualifiedName~V3063Tests"
```
Expected: All snapshot tests still pass.

If any snapshot mismatches, manually inspect the diff. SSA-based tracking should subsume name-based tracking; mismatches signal either (a) a bug in the new transfer, or (b) the old transfer was incorrectly conflating shadowed variables. In case (b), accept the new snapshot after verification.

- [ ] **Step 4: Commit**

```
git add src/OpenVulScan.Rules.DataFlow/V3022AlwaysTrueFalse.cs src/OpenVulScan.Rules.DataFlow/V3063PartialAlwaysTrueFalse.cs
git commit -m "feat(rules): migrate V3022 and V3063 to ConstantSsaTransfer"
```

---

### Task 19: Delete legacy NullStateMapTransfer and ConstantMapTransfer

**Files:**
- Delete: `src/OpenVulScan.Core/Lattice/NullStateMapTransfer.cs`
- Delete: `src/OpenVulScan.Core/Lattice/ConstantMapTransfer.cs`
- Possibly delete: `tests/OpenVulScan.Core.Tests/Lattice/NullStateMapTransferTests.cs` (verify existence first)
- Possibly delete: `tests/OpenVulScan.Core.Tests/Lattice/ConstantMapTransferTests.cs` (verify existence first)
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRule.cs` — remove the now-unused `Transfer` property override on rules (V3022, V3063) and reconsider whether `Transfer` should still exist on the base class.

- [ ] **Step 1: Confirm no callers remain**

Run: search the solution for `NullStateMapTransfer` and `ConstantMapTransfer`.

```
git grep -n "NullStateMapTransfer\|ConstantMapTransfer"
```
Expected: only the files to be deleted are referenced. If anything else references them, fix it before deleting.

- [ ] **Step 2: Delete the files**

```
git rm src/OpenVulScan.Core/Lattice/NullStateMapTransfer.cs
git rm src/OpenVulScan.Core/Lattice/ConstantMapTransfer.cs
# If the test files exist, remove them too:
git rm tests/OpenVulScan.Core.Tests/Lattice/NullStateMapTransferTests.cs 2>/dev/null
git rm tests/OpenVulScan.Core.Tests/Lattice/ConstantMapTransferTests.cs 2>/dev/null
```

- [ ] **Step 3: Remove the throwing `Transfer` overrides from V3022 and V3063**

In `DataFlowRule<TLattice>`, change `Transfer` from `abstract` to `virtual` with a default `=> throw new NotSupportedException("Override CreateTransfer instead.")`. Then delete the override from V3022/V3063 (they now only override `CreateTransfer` and `Lattice`).

- [ ] **Step 4: Build + run full test suite**

Run:
```
dotnet build --configuration Release
dotnet test --configuration Release
```
Expected: 0 build errors, all tests pass.

- [ ] **Step 5: Commit**

```
git add -A
git commit -m "refactor(core): remove legacy NullStateMapTransfer and ConstantMapTransfer"
```

---

### Task 20: Close beads and push

**Files:** none

- [ ] **Step 1: Close ovs-2qi.9**

Run: `bd close ovs-2qi.9 --reason="SSA numbering with explicit phi, TrackedKey (Symbol/InstanceField/Capture), and SsaId. Map transfers migrated. V3022/V3063 snapshots green."`

- [ ] **Step 2: Push beads + git**

Run:
```
git pull --rebase
bd dolt push
git push
git status
```
Expected: `git status` shows "up to date with origin".

---

## Self-Review

**1. Spec coverage:**

| Spec requirement | Task |
|---|---|
| `TrackedKey` records with `SymbolEqualityComparer` | Task 2 |
| `SsaId` record struct | Task 3 |
| `Phi` + `PhiOperand` | Task 4 |
| `SsaIndex` public API | Task 5 |
| Pass 1 def collection | Task 6 |
| Pass 2 straight-line versioning | Task 6 |
| Pass 2 φ placement | Task 7 |
| Pass 3 operand binding | Task 7 |
| Back-edges (while) | Task 8 |
| Switch | Task 9 |
| Nested control flow | Task 10 |
| `this.field` tracking | Task 11 |
| Kill on instance method call | Task 11 |
| Static method ≠ kill | Task 11 |
| `IFlowCapture` integration | Task 12 |
| Shadowing | Task 13 |
| NullStateSsaTransfer (migration) | Task 14 |
| ConstantSsaTransfer (migration) | Task 15 |
| `DataFlowContext.SsaIndex` | Task 16 |
| `DataFlowRule.CreateTransfer` | Task 16 |
| Dispatcher builds SsaIndex | Task 17 |
| V3022/V3063 migration | Task 18 |
| V3022/V3063 snapshot regression | Task 18 |
| Delete legacy transfers | Task 19 |
| Close beads + push | Task 20 |

**2. Placeholder scan:**

No "TBD/TODO/implement later" steps remain. Task 14 (NullStateSsaTransfer) and Task 15 (ConstantSsaTransfer) both have full implementations. Task 18 references `state[ssaIndex.UseAt(op, key).Value]` as a guide pattern for rule body changes — the actual rule files are read in Task 18 Step 1 first, so the engineer adapts based on real code.

**3. Type consistency:**

- `TrackedKey` (Task 2) → `SsaId(TrackedKey, int)` (Task 3) → `Phi.Result : SsaId, Operands : ImmutableArray<PhiOperand>` (Task 4) → `SsaIndex.PhisAt(BasicBlock) → IReadOnlyList<Phi>` (Task 5). Consistent.
- `NullStateSsaTransfer` and `ConstantSsaTransfer` both take `SsaIndex` in constructor → dispatcher calls `rule.CreateTransfer(ssaIndex)` (Task 17). Consistent.
- Both transfers use `private static readonly Lattice _lattice = new();` pattern for the join helper. Consistent.
