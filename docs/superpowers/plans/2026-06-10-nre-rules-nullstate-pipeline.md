# NRE Quartet (V3080/V3105/V3153/V3168) on Shared NullState Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire DataFlow rules into the production scheduler and ship four NRE rules (V3080, V3105, V3153, V3168) that share one NullState SSA analysis with a provenance classifier.

**Architecture:** One worklist solve per method per lattice family (dispatcher groups rules by lattice/transfer/refiner types); a new SSA-aware `NullStateSsaEdgeRefiner` suppresses guard false positives; a shared `NullDerefClassifier` finds deref sites and assigns exactly one rule code by provenance (await → V3153-syntax → V3105-def-site → V3080).

**Tech Stack:** .NET 10, Roslyn `ControlFlowGraph`/`IOperation`, xUnit, Verify snapshots.

**Spec:** `docs/superpowers/specs/2026-06-10-nre-rules-nullstate-pipeline-design.md`

**Conventions reminder (from CLAUDE.md):** single namespace `OpenVulScan`; `TreatWarningsAsErrors=true` with `AnalysisLevel=latest-all` — fix CA warnings or suppress narrowly with justification mirroring `RuleScheduler.cs`; 4-space indent; every async public API takes `CancellationToken`. Test method names must not contain underscores in `Rules.Tests` (CA1707 is an error there); underscores are allowed in `Core.Tests`.

---

## File structure

| File | Responsibility |
|---|---|
| `src/OpenVulScan.Core/Ssa/SsaIndex.cs` (modify) | + `DefSiteOf(SsaId)` reverse lookup |
| `src/OpenVulScan.Core/Lattice/NullStateSsaEdgeRefiner.cs` (create) | SSA-keyed branch refinement incl. lowered `IIsNullOperation` and capture refs |
| `src/OpenVulScan.RuleEngine/IDataFlowRule.cs` (create) | non-generic marker for scheduler discovery |
| `src/OpenVulScan.RuleEngine/DataFlowRule.cs` (modify) | implement marker; + `CreateEdgeRefiner(SsaIndex)` |
| `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs` (modify) | group rules, one solve per group, use `CreateEdgeRefiner` |
| `src/OpenVulScan.RuleEngine/RuleScheduler.cs` (modify) | collect `IDataFlowRule`, reflective dispatch per state type |
| `src/OpenVulScan.Rules.DataFlow/NullStateRuleBase.cs` (create) | seals Lattice/Transfer/Refiner for the family |
| `src/OpenVulScan.Rules.DataFlow/NullDerefClassifier.cs` (create) | deref-site detection + provenance classification |
| `src/OpenVulScan.Rules.DataFlow/V3080PossibleNullDereference.cs` (create) | thin subscriber |
| `src/OpenVulScan.Rules.DataFlow/V3105UseAfterNullConditional.cs` (create) | thin subscriber |
| `src/OpenVulScan.Rules.DataFlow/V3153DereferenceOfConditionalAccessResult.cs` (create) | thin subscriber |
| `src/OpenVulScan.Rules.DataFlow/V3168AwaitPossiblyNull.cs` (create) | thin subscriber |
| `tests/OpenVulScan.Core.Tests/Ssa/SsaIndexDefSiteTests.cs` (create) | DefSiteOf unit tests |
| `tests/OpenVulScan.Core.Tests/Lattice/NullStateSsaEdgeRefinerTests.cs` (create) | refiner unit tests |
| `tests/OpenVulScan.Rules.Tests/DataFlowGroupingTests.cs` (create) | grouping equivalence |
| `tests/OpenVulScan.Rules.Tests/SchedulerDataFlowTests.cs` (create) | scheduler wiring proof |
| `tests/OpenVulScan.Rules.Tests/SnapshotTestHarness.cs` (modify) | scan Rules.DataFlow assembly |
| `tests/OpenVulScan.Rules.Tests/V3080Tests.cs` … `V3168Tests.cs` (create) | snapshot suites |
| `tests/OpenVulScan.Rules.Tests/SyntheticCorpusTests.cs` + `TestData/Synthetic.cs` (create) | zero-FP corpus |

Key existing APIs (do not re-derive):

- `SsaId(TrackedKey Key, int Version)`; `TrackedKey.Symbol(ISymbol)` / `TrackedKey.InstanceField(IFieldSymbol)` / `TrackedKey.Capture(CaptureId)`.
- `SsaIndex`: `DefinitionAt(IOperation)`, `UseAt(IOperation, TrackedKey)`, `PhisAt(BasicBlock)`, `EntryVersions(BasicBlock)`, `AllVersions(TrackedKey)`. Built by `SsaBuilder.Build(cfg, model)`.
- `NullState { Unknown(⊥), DefinitelyNull, NotNull, MaybeNull(⊤) }`; `NullStateLattice`; `MapLattice<SsaId, NullStateLattice, NullState>`.
- `NullStateSsaTransfer(SsaIndex)` — evaluates defs incl. flow captures (S-2 work).
- `NullStateTransfer.RefineForNullCheck(NullState)` / `RefineForNotNullCheck(NullState)` — static helpers; reuse, do not duplicate.
- `IEdgeRefiner<T>.Refine(T state, ControlFlowBranch branch)` (in `src/OpenVulScan.Core/Cfg/IEdgeRefiner.cs`).
- `WorklistSolver<T>(ILattice<T>, ITransfer<T>, IEdgeRefiner<T>?)`; `Solve(cfg, ct)` → result with `InStates`/`OutStates` per block.
- `DataFlowContext(op, model, compilation, ssaIndex, ct)` with `ReportDiagnostic`/`Diagnostics`.
- `CfgTestHarness.Compile(snippet)` in `tests/OpenVulScan.Core.Tests/Ssa/` → `(Cfg, Model, Body)`.
- `SnapshotTestHarness.RunRuleSnapshotAsync(ruleCode, testCase, source)` in Rules.Tests.

---

### Task 1: `SsaIndex.DefSiteOf`

**Files:**
- Modify: `src/OpenVulScan.Core/Ssa/SsaIndex.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/SsaIndexDefSiteTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaIndexDefSiteTests
{
    private static IEnumerable<IOperation> AllOps(ControlFlowGraph cfg)
    {
        foreach (var block in cfg.Blocks)
        {
            var roots = block.BranchValue is null
                ? block.Operations
                : block.Operations.Concat(new[] { block.BranchValue });
            foreach (var op in roots)
            {
                foreach (var d in Descend(op)) yield return d;
            }
        }

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    [Fact]
    public void DefSiteOf_AssignmentDef_RoundTrips()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C { void M() { int x = 0; x = 1; } }");
        var index = SsaBuilder.Build(cfg, model);

        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .First(a => a.Value.ConstantValue.Value is 1);
        var id = index.DefinitionAt(assign);

        Assert.NotNull(id);
        Assert.Same(assign, index.DefSiteOf(id.Value));
    }

    [Fact]
    public void DefSiteOf_PhiResult_ReturnsNull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C { void M(bool c) { int x = 1; if (c) { x = 2; } int y = x; } }");
        var index = SsaBuilder.Build(cfg, model);

        var phi = cfg.Blocks.SelectMany(b => index.PhisAt(b))
            .First(p => p.Result.Key is TrackedKey.Symbol);

        Assert.Null(index.DefSiteOf(phi.Result));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaIndexDefSiteTests"`
Expected: compile error — `SsaIndex` has no `DefSiteOf`.

- [ ] **Step 3: Implement**

In `SsaIndex.cs`: add field, populate in constructor (inversion is safe — `SsaBuilder` allocates a fresh version per def site, so `_definitions` values are unique), add accessor:

```csharp
private readonly ImmutableDictionary<SsaId, IOperation> _defSites;
```

In the constructor body, after the existing assignments:

```csharp
_defSites = definitions.ToImmutableDictionary(kv => kv.Value, kv => kv.Key);
```

New public method (after `AllVersions`):

```csharp
/// <summary>
/// Returns the defining operation of <paramref name="id"/>, or
/// <see langword="null"/> for φ-results and entry versions, which have
/// no defining operation.
/// </summary>
public IOperation? DefSiteOf(SsaId id)
{
    return _defSites.TryGetValue(id, out var op) ? op : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaIndexDefSiteTests"`
Expected: 2 passed. Then run the full Core suite to catch regressions:
`dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add src/OpenVulScan.Core/Ssa/SsaIndex.cs tests/OpenVulScan.Core.Tests/Ssa/SsaIndexDefSiteTests.cs
git commit -m "feat(ssa): DefSiteOf reverse lookup on SsaIndex"
```

---

### Task 2: `NullStateSsaEdgeRefiner`

**Files:**
- Create: `src/OpenVulScan.Core/Lattice/NullStateSsaEdgeRefiner.cs`
- Test: `tests/OpenVulScan.Core.Tests/Lattice/NullStateSsaEdgeRefinerTests.cs`

Critical context: Roslyn lowers `?.` and `??` into flow-capture branches whose
`BranchValue` is an **`IIsNullOperation`** (namespace `Microsoft.CodeAnalysis.Operations`)
wrapping an `IFlowCaptureReferenceOperation`. Source-level checks appear as
`IBinaryOperation` (`== null` / `!= null`) or `IIsPatternOperation` (`is null` /
`is not null`). The refiner must handle all three families or `?.` constructs will
false-positive in the rules.

Branch direction must use `BasicBlock.ConditionKind` (`ControlFlowConditionKind.WhenTrue`
/ `WhenFalse`), not assume fall-through means true (the existing flat refiner's assumption
is not generally valid).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests.Lattice;

public class NullStateSsaEdgeRefinerTests
{
    private static (ControlFlowGraph Cfg, SsaIndex Index) Build(string snippet)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(snippet);
        return (cfg, SsaBuilder.Build(cfg, model));
    }

    private static ImmutableDictionary<SsaId, NullState> Solve(
        ControlFlowGraph cfg, SsaIndex index, BasicBlock block)
    {
        var lattice = new MapLattice<SsaId, NullStateLattice, NullState>();
        var transfer = new NullStateSsaTransfer(index);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, NullState>>(
            lattice, transfer, new NullStateSsaEdgeRefiner(index));
        var result = solver.Solve(cfg, CancellationToken.None);
        return transfer.ApplyPhis(result.InStates[block], block);
    }

    private static BasicBlock BlockDefining(ControlFlowGraph cfg, SsaIndex index, string localName)
    {
        return cfg.Blocks.First(b => b.Operations
            .SelectMany(Descend)
            .Any(op => op is ISimpleAssignmentOperation { Target: ILocalReferenceOperation l }
                && l.Local.Name == localName));

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    private static NullState StateOfLocalInit(
        ControlFlowGraph cfg, SsaIndex index, ImmutableDictionary<SsaId, NullState> state, string localName)
    {
        // The RHS of "var t = a;" reads a — look up the use version of the RHS reference.
        var assign = cfg.Blocks.SelectMany(b => b.Operations)
            .SelectMany(DescendAll)
            .OfType<ISimpleAssignmentOperation>()
            .First(a => a.Target is ILocalReferenceOperation l && l.Local.Name == localName);
        var rhs = (IParameterReferenceOperation)assign.Value;
        var use = index.UseAt(rhs, new TrackedKey.Symbol(rhs.Parameter));
        Assert.NotNull(use);
        return state.TryGetValue(use.Value, out var s) ? s : NullState.Unknown;

        static IEnumerable<IOperation> DescendAll(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in DescendAll(child)) yield return d;
            }
        }
    }

    [Fact]
    public void NotEqualsNull_RefinesThenAndElseBranches()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t; string u;
    if (a != null) { t = a; } else { u = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));
        var elseState = Solve(cfg, index, BlockDefining(cfg, index, "u"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
        Assert.Equal(NullState.DefinitelyNull, StateOfLocalInit(cfg, index, elseState, "u"));
    }

    [Fact]
    public void IsNotNullPattern_RefinesThenBranch()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t;
    if (a is not null) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void NegatedEqualsNull_InvertsBranchSense()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t;
    if (!(a == null)) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void ConditionalAnd_TrueBranch_RefinesBothOperands()
    {
        var (cfg, index) = Build(@"
class C { void M(string a, string b) { string t;
    if (a != null && b != null) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void GuardOnOtherVariable_DoesNotRefineThisOne()
    {
        var (cfg, index) = Build(@"
class C { void M(string a, string b) { string t;
    if (b != null) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.Unknown, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void LoweredIsNullBranch_RefinesCaptureOnBothEdges()
    {
        // a ?? "y" lowers to: capture(a); branch on IsNull(captureRef); …
        var (cfg, index) = Build(@"
class C { void M(string a) { var x = a ?? ""y""; } }");

        var branchBlock = cfg.Blocks.First(b =>
            b.BranchValue is IIsNullOperation { Operand: IFlowCaptureReferenceOperation });
        var isNull = (IIsNullOperation)branchBlock.BranchValue!;
        var cref = (IFlowCaptureReferenceOperation)isNull.Operand;
        var use = index.UseAt(cref, new TrackedKey.Capture(cref.Id));
        Assert.NotNull(use);

        var refiner = new NullStateSsaEdgeRefiner(index);
        var seed = ImmutableDictionary<SsaId, NullState>.Empty.SetItem(use.Value, NullState.MaybeNull);

        var refinedConditional = refiner.Refine(seed, branchBlock.ConditionalSuccessor!);
        var refinedFallThrough = refiner.Refine(seed, branchBlock.FallThroughSuccessor!);

        var pair = new[] { refinedConditional[use.Value], refinedFallThrough[use.Value] };
        Assert.Contains(NullState.DefinitelyNull, pair);
        Assert.Contains(NullState.NotNull, pair);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~NullStateSsaEdgeRefinerTests"`
Expected: compile error — `NullStateSsaEdgeRefiner` does not exist.

- [ ] **Step 3: Implement `NullStateSsaEdgeRefiner`**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// SSA-aware edge refiner for null-state analysis. Extracts null-state
/// refinements from branch conditions and applies them to the
/// <see cref="SsaId"/>-keyed state map.
/// </summary>
/// <remarks>
/// <para>
/// Recognises source-level checks (<c>x == null</c>, <c>x != null</c>,
/// <c>x is null</c>, <c>x is not null</c>, recursion through <c>!</c>,
/// <c>&amp;&amp;</c> and <c>||</c>) and the lowered
/// <see cref="IIsNullOperation"/> branches Roslyn emits for <c>?.</c> and
/// <c>??</c>, including <see cref="IFlowCaptureReferenceOperation"/> operands.
/// </para>
/// </remarks>
public sealed class NullStateSsaEdgeRefiner : IEdgeRefiner<ImmutableDictionary<SsaId, NullState>>
{
    private readonly SsaIndex _ssa;

    public NullStateSsaEdgeRefiner(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, NullState> Refine(
        ImmutableDictionary<SsaId, NullState> state,
        ControlFlowBranch branch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(branch);

        if (branch.Source is not { BranchValue: { } condition } source
            || source.ConditionKind == ControlFlowConditionKind.None)
        {
            return state;
        }

        bool isConditional = source.ConditionalSuccessor == branch;
        bool isFallThrough = source.FallThroughSuccessor == branch;
        if (!isConditional && !isFallThrough)
        {
            return state;
        }

        // The conditional successor is taken when the condition matches
        // ConditionKind; the fall-through edge is its complement.
        bool whenTrue = isConditional == (source.ConditionKind == ControlFlowConditionKind.WhenTrue);

        var refinements = ImmutableArray.CreateBuilder<(SsaId Id, bool IsNull)>();
        Collect(condition, whenTrue, refinements);

        foreach (var (id, isNull) in refinements)
        {
            var current = state.TryGetValue(id, out var s) ? s : NullState.Unknown;
            var refined = isNull
                ? NullStateTransfer.RefineForNullCheck(current)
                : NullStateTransfer.RefineForNotNullCheck(current);
            state = state.SetItem(id, refined);
        }

        return state;
    }

    private void Collect(
        IOperation condition,
        bool whenTrue,
        ImmutableArray<(SsaId, bool)>.Builder refinements)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            case IIsNullOperation isNull:
                AddRefinement(isNull.Operand, isNull: whenTrue, refinements);
                break;

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary:
                Collect(unary.Operand, !whenTrue, refinements);
                break;

            case IBinaryOperation binary:
                CollectBinary(binary, whenTrue, refinements);
                break;

            case IIsPatternOperation isPattern:
                CollectIsPattern(isPattern, whenTrue, refinements);
                break;

            default:
                break;
        }
    }

    private void CollectBinary(
        IBinaryOperation binary,
        bool whenTrue,
        ImmutableArray<(SsaId, bool)>.Builder refinements)
    {
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals when TryGetNullComparand(binary, out var operand):
                AddRefinement(operand, isNull: whenTrue, refinements);
                break;

            case BinaryOperatorKind.NotEquals when TryGetNullComparand(binary, out var operand):
                AddRefinement(operand, isNull: !whenTrue, refinements);
                break;

            case BinaryOperatorKind.ConditionalAnd when whenTrue:
                Collect(binary.LeftOperand, whenTrue: true, refinements);
                Collect(binary.RightOperand, whenTrue: true, refinements);
                break;

            case BinaryOperatorKind.ConditionalOr when !whenTrue:
                Collect(binary.LeftOperand, whenTrue: false, refinements);
                Collect(binary.RightOperand, whenTrue: false, refinements);
                break;

            default:
                break;
        }
    }

    private void CollectIsPattern(
        IIsPatternOperation isPattern,
        bool whenTrue,
        ImmutableArray<(SsaId, bool)>.Builder refinements)
    {
        switch (isPattern.Pattern)
        {
            case IConstantPatternOperation { ConstantValue.Value: null }:
                AddRefinement(isPattern.Value, isNull: whenTrue, refinements);
                break;

            case INegatedPatternOperation { Pattern: IConstantPatternOperation { ConstantValue.Value: null } }:
                AddRefinement(isPattern.Value, isNull: !whenTrue, refinements);
                break;

            default:
                break;
        }
    }

    private void AddRefinement(
        IOperation operand,
        bool isNull,
        ImmutableArray<(SsaId, bool)>.Builder refinements)
    {
        operand = Unwrap(operand);

        TrackedKey? key = operand switch
        {
            ILocalReferenceOperation l => new TrackedKey.Symbol(l.Local),
            IParameterReferenceOperation p => new TrackedKey.Symbol(p.Parameter),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f => new TrackedKey.InstanceField(f.Field),
            IFlowCaptureReferenceOperation c => new TrackedKey.Capture(c.Id),
            _ => null,
        };

        if (key is null)
        {
            return;
        }

        if (_ssa.UseAt(operand, key) is { } id)
        {
            refinements.Add((id, isNull));
        }
    }

    private static bool TryGetNullComparand(IBinaryOperation binary, out IOperation operand)
    {
        if (IsNullLiteral(binary.LeftOperand))
        {
            operand = binary.RightOperand;
            return true;
        }

        if (IsNullLiteral(binary.RightOperand))
        {
            operand = binary.LeftOperand;
            return true;
        }

        operand = binary;
        return false;
    }

    private static bool IsNullLiteral(IOperation operation)
    {
        operation = Unwrap(operation);
        return operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conv:
                    operation = conv.Operand;
                    continue;
                case IParenthesizedOperation paren:
                    operation = paren.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }
}
```

Note: `Unwrap` strips all conversions (not only identity ones). For null-state purposes a
conversion never changes nullness of a reference, and `UseAt` records uses on the
underlying reference operation, so the unwrapped node is the one with a use binding.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~NullStateSsaEdgeRefinerTests"`
Expected: 6 passed. If `LoweredIsNullBranch…` fails because `UseAt(cref, …)` is null,
inspect whether `SsaBuilder` records a use for the capture reference in the branch value —
it should after the S-2/S-3 work; if not, that is a bug to fix in `SsaBuilder.Walk`'s use
recording (capture refs outside def positions must record uses), not in the test.

- [ ] **Step 5: Run full Core suite**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj`
Expected: all pass (163 + 6 new, 1 skip).

- [ ] **Step 6: Commit**

```bash
git add src/OpenVulScan.Core/Lattice/NullStateSsaEdgeRefiner.cs tests/OpenVulScan.Core.Tests/Lattice/NullStateSsaEdgeRefinerTests.cs
git commit -m "feat(lattice): SSA-aware NullState edge refiner incl. lowered IsNull branches"
```

---

### Task 3: `CreateEdgeRefiner` contract + shared-solve grouping in the dispatcher

**Files:**
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRule.cs`
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs`
- Test: `tests/OpenVulScan.Rules.Tests/DataFlowGroupingTests.cs`

- [ ] **Step 1: Write the failing test**

CA1707 applies in Rules.Tests — no underscores in method names.

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class DataFlowGroupingTests
{
    private static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void GroupedRulesProduceSameDiagnosticsAsSeparateSolves()
    {
        const string source = @"
class C
{
    void M(bool c)
    {
        int x = 5;
        if (c) { x = 5; }
        if (x == 5) { }
        if (true) { }
    }
}";
        var compilation = Compile(source);

        var grouped = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
            new DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>[]
            {
                new V3022AlwaysTrueFalse(),
                new V3063PartialAlwaysTrueFalse(),
            },
            compilation).Run(CancellationToken.None);

        var separate = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
                new DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>[] { new V3022AlwaysTrueFalse() },
                compilation).Run(CancellationToken.None)
            .Concat(new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
                new DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>[] { new V3063PartialAlwaysTrueFalse() },
                compilation).Run(CancellationToken.None))
            .ToList();

        static string Render(Diagnostic d) =>
            $"{d.Id}|{d.Location.SourceSpan}|{d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";

        Assert.Equal(
            separate.Select(Render).OrderBy(s => s, StringComparer.Ordinal),
            grouped.Select(Render).OrderBy(s => s, StringComparer.Ordinal));
        Assert.NotEmpty(grouped);
    }
}
```

- [ ] **Step 2: Run test to verify current state**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~DataFlowGroupingTests"`
Expected: PASS already (current dispatcher solves per rule — equivalence holds trivially).
This test is the safety net for the rewrite; verify it is green before changing the
dispatcher, then keep it green after.

- [ ] **Step 3: Add `CreateEdgeRefiner` to `DataFlowRule`**

In `src/OpenVulScan.RuleEngine/DataFlowRule.cs`, after `CreateTransfer`:

```csharp
// Default implementation: SSA-unaware refiner from the legacy property.
// SSA-aware rules override this to construct a refiner over the index.
public virtual IEdgeRefiner<TLattice>? CreateEdgeRefiner(SsaIndex ssaIndex) => EdgeRefiner;
```

- [ ] **Step 4: Rewrite `DataFlowRuleDispatcher.Run` with grouping**

Replace the per-rule loop inside the `foreach (var method …)` body (the part from
`foreach (var rule in _rules)` through the end of its block) with:

```csharp
var entries = _rules
    .Select(rule => (
        Rule: rule,
        Transfer: rule.CreateTransfer(ssaIndex),
        Refiner: rule.CreateEdgeRefiner(ssaIndex)))
    .ToList();

var groups = entries.GroupBy(e => (
    LatticeType: e.Rule.Lattice.GetType(),
    TransferType: e.Transfer.GetType(),
    RefinerType: e.Refiner?.GetType()));

foreach (var group in groups)
{
    cancellationToken.ThrowIfCancellationRequested();

    // Transfers and refiners are stateless apart from the SsaIndex they
    // were built over (same per method), so type equality implies
    // identical behaviour and one solve serves the whole group.
    var first = group.First();
    var solver = new WorklistSolver<TLattice>(first.Rule.Lattice, first.Transfer, first.Refiner);
    var result = solver.Solve(cfg, cancellationToken);

    foreach (var block in cfg.Blocks)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = result.InStates[block];
        state = first.Transfer.ApplyPhis(state, block);

        foreach (var op in GetAllOperations(block))
        {
            foreach (var entry in group)
            {
                var context = new DataFlowContext(op, model, _compilation, ssaIndex, cancellationToken);
                entry.Rule.InvokeOnState(op, state, context);
                diagnostics.AddRange(context.Diagnostics);
            }

            state = first.Transfer.Apply(state, op);
        }
    }
}
```

- [ ] **Step 5: Run grouping test + both existing DataFlow rule suites**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~DataFlowGroupingTests|FullyQualifiedName~V3022Tests|FullyQualifiedName~V3063Tests|FullyQualifiedName~DataFlowRuleTests"`
Expected: all pass.

- [ ] **Step 6: Run full suite (all three test projects)**

Run: `dotnet test --configuration Release`
Expected: all pass, build clean (warnings-as-errors).

- [ ] **Step 7: Commit**

```bash
git add src/OpenVulScan.RuleEngine/DataFlowRule.cs src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs tests/OpenVulScan.Rules.Tests/DataFlowGroupingTests.cs
git commit -m "feat(engine): shared worklist solve per rule group + CreateEdgeRefiner contract"
```

---

### Task 4: Scheduler wiring for DataFlow rules

**Files:**
- Create: `src/OpenVulScan.RuleEngine/IDataFlowRule.cs`
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRule.cs` (implement marker)
- Modify: `src/OpenVulScan.RuleEngine/RuleScheduler.cs`
- Modify: `tests/OpenVulScan.Rules.Tests/SnapshotTestHarness.cs`
- Test: `tests/OpenVulScan.Rules.Tests/SchedulerDataFlowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class SchedulerDataFlowTests
{
    [Fact]
    public async Task SchedulerRunsDataFlowRules()
    {
        var tree = CSharpSyntaxTree.ParseText(@"
class C { void M() { if (true) { } } }");
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var registry = new RuleRegistry();
        registry.Scan(typeof(DataFlowRulesPlaceholder).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        Assert.Contains(diagnostics, d => d.Id == "V3022");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~SchedulerDataFlowTests"`
Expected: FAIL — `AnalyzeAsync` returns no V3022 (scheduler ignores DataFlow rules).

- [ ] **Step 3: Create the marker interface**

`src/OpenVulScan.RuleEngine/IDataFlowRule.cs`:

```csharp
namespace OpenVulScan;

/// <summary>
/// Non-generic marker for <see cref="DataFlowRule{TLattice}"/> so the
/// scheduler can discover data-flow rules without knowing their closed
/// lattice state type.
/// </summary>
public interface IDataFlowRule
{
}
```

In `DataFlowRule.cs`, change the class declaration:

```csharp
public abstract class DataFlowRule<TLattice> : IDataFlowRule
```

- [ ] **Step 4: Dispatch DataFlow rules in `RuleScheduler`**

In `RuleScheduler.AnalyzeAsync`: collect instances, then dispatch reflectively. Add to the
collection loop (alongside the `AstRule`/`SymbolRule` checks):

```csharp
var dataFlowRules = new List<IDataFlowRule>();
```

```csharp
if (instance is IDataFlowRule dataFlowRule)
{
    dataFlowRules.Add(dataFlowRule);
}
```

After the `symbolRules` dispatch block:

```csharp
if (dataFlowRules.Count > 0)
{
    foreach (var group in dataFlowRules.GroupBy(r => GetDataFlowStateType(r.GetType())))
    {
        if (group.Key is null)
        {
            continue;
        }

        allDiagnostics.AddRange(RunDataFlowGroup(group.Key, group, compilation, cancellationToken));
    }
}
```

New private helpers (bottom of the class):

```csharp
private static Type? GetDataFlowStateType(Type ruleType)
{
    for (var type = ruleType.BaseType; type is not null; type = type.BaseType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataFlowRule<>))
        {
            return type.GetGenericArguments()[0];
        }
    }

    return null;
}

private IReadOnlyList<Diagnostic> RunDataFlowGroup(
    Type stateType,
    IEnumerable<IDataFlowRule> rules,
    Compilation compilation,
    CancellationToken cancellationToken)
{
    // The dispatcher is generic in the lattice state type, which is only
    // known at runtime — close it reflectively, mirroring the reflection
    // already used for AstRule handler discovery.
    var ruleBaseType = typeof(DataFlowRule<>).MakeGenericType(stateType);
    var listType = typeof(List<>).MakeGenericType(ruleBaseType);
    var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
    foreach (var rule in rules)
    {
        list.Add(rule);
    }

    var dispatcherType = typeof(DataFlowRuleDispatcher<>).MakeGenericType(stateType);
    try
    {
        var dispatcher = Activator.CreateInstance(dispatcherType, list, compilation)!;
        var run = dispatcherType.GetMethod(nameof(DataFlowRuleDispatcher<object>.Run))!;
        return (IReadOnlyList<Diagnostic>)run.Invoke(dispatcher, new object[] { cancellationToken })!;
    }
    catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is OperationCanceledException oce)
    {
        throw oce;
    }
}
```

Note: `DataFlowRuleDispatcher<object>.Run` in `nameof` is just a compile-time name anchor.
If the analyzers complain about `Activator`/reflection usage (e.g. IL-trimming or CA
warnings), suppress narrowly with `#pragma warning disable` + justification, mirroring the
existing CA1031 pattern in this file. Cancellation must surface as
`OperationCanceledException`, not `TargetInvocationException` — hence the unwrap.

- [ ] **Step 5: Run the new test**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~SchedulerDataFlowTests"`
Expected: PASS.

- [ ] **Step 6: Make the snapshot harness scan the DataFlow assembly**

In `tests/OpenVulScan.Rules.Tests/SnapshotTestHarness.cs`, after
`registry.Scan(typeof(AstRulesPlaceholder).Assembly);` add:

```csharp
registry.Scan(typeof(DataFlowRulesPlaceholder).Assembly);
```

- [ ] **Step 7: Run the full Rules.Tests suite — existing snapshots must not change**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj`
Expected: all pass. The harness filters diagnostics by `d.Id == ruleCode`, so V3022/V3063
firing on other rules' test sources cannot contaminate snapshots. If any snapshot diff
appears, inspect it — do not blindly accept.

- [ ] **Step 8: Run full suite**

Run: `dotnet test --configuration Release`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add src/OpenVulScan.RuleEngine/IDataFlowRule.cs src/OpenVulScan.RuleEngine/DataFlowRule.cs src/OpenVulScan.RuleEngine/RuleScheduler.cs tests/OpenVulScan.Rules.Tests/SnapshotTestHarness.cs tests/OpenVulScan.Rules.Tests/SchedulerDataFlowTests.cs
git commit -m "feat(engine): run DataFlow rules through RuleScheduler and snapshot harness"
```

---

### Task 5: `NullStateRuleBase`, `NullDerefClassifier`, V3080

**Files:**
- Create: `src/OpenVulScan.Rules.DataFlow/NullStateRuleBase.cs`
- Create: `src/OpenVulScan.Rules.DataFlow/NullDerefClassifier.cs`
- Create: `src/OpenVulScan.Rules.DataFlow/V3080PossibleNullDereference.cs`
- Test: `tests/OpenVulScan.Rules.Tests/V3080Tests.cs`

- [ ] **Step 1: Write the failing snapshot tests (V3080)**

Snapshot test-case names keep underscores (they become file names); method names must not
(CA1707).

```csharp
using Xunit;

namespace OpenVulScan.Tests;

public class V3080Tests
{
    [Fact]
    public Task NullLiteralThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_literal_then_deref", @"
class C
{
    void M()
    {
        string s = null;
        var n = s.Length;
    }
}");

    [Fact]
    public Task NullOnOneBranchThenDerefAfterJoin() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_on_one_branch_join", @"
class C
{
    void M(bool c)
    {
        string s = ""x"";
        if (c) { s = null; }
        var n = s.Length;
    }
}");

    [Fact]
    public Task NullAssignedThenInvocation() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_then_invocation", @"
class C
{
    void M()
    {
        string s = null;
        var t = s.ToString();
    }
}");

    [Fact]
    public Task NullArrayElementAccess() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_array_element_access", @"
class C
{
    void M()
    {
        int[] a = null;
        var n = a[0];
    }
}");

    [Fact]
    public Task NullFieldThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_field_then_deref", @"
class C
{
    string f;
    void M()
    {
        this.f = null;
        var n = this.f.Length;
    }
}");

    [Fact]
    public Task GuardedDerefIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "guarded_deref_silent", @"
class C
{
    void M(bool c)
    {
        string s = null;
        if (c) { s = ""x""; }
        if (s != null)
        {
            var n = s.Length;
        }
    }
}");

    [Fact]
    public Task IsNotNullGuardIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "is_not_null_guard_silent", @"
class C
{
    void M(bool c)
    {
        string s = null;
        if (c) { s = ""x""; }
        if (s is not null)
        {
            var n = s.Length;
        }
    }
}");

    [Fact]
    public Task ReassignedBeforeDerefIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "reassigned_before_deref_silent", @"
class C
{
    void M()
    {
        string s = null;
        s = ""x"";
        var n = s.Length;
    }
}");

    [Fact]
    public Task UncheckedParameterIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "unchecked_parameter_silent", @"
class C
{
    void M(string p)
    {
        var n = p.Length;
    }
}");

    [Fact]
    public Task EarlyReturnGuardIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "early_return_guard_silent", @"
class C
{
    void M(bool c)
    {
        string s = null;
        if (c) { s = ""x""; }
        if (s == null) { return; }
        var n = s.Length;
    }
}");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3080Tests"`
Expected: FAIL — no `*.verified.txt` yet and 0 diagnostics produced (rule doesn't exist).
The positives will produce empty-diagnostics received files at first run — they must NOT be
accepted until the rule exists and the received output shows the expected diagnostics.

- [ ] **Step 3: Implement `NullStateRuleBase`**

```csharp
using System.Collections.Immutable;

namespace OpenVulScan;

/// <summary>
/// Shared pipeline definition for the NRE rule family
/// (V3080 / V3105 / V3153 / V3168): NullState over SSA versions with
/// branch refinement. Sealing the pipeline members guarantees the
/// dispatcher groups all family rules into a single worklist solve.
/// </summary>
public abstract class NullStateRuleBase : DataFlowRule<ImmutableDictionary<SsaId, NullState>>
{
    public sealed override ILattice<ImmutableDictionary<SsaId, NullState>> Lattice { get; }
        = new MapLattice<SsaId, NullStateLattice, NullState>();

    public sealed override ITransfer<ImmutableDictionary<SsaId, NullState>> CreateTransfer(SsaIndex ssaIndex)
        => new NullStateSsaTransfer(ssaIndex);

    public sealed override IEdgeRefiner<ImmutableDictionary<SsaId, NullState>>? CreateEdgeRefiner(SsaIndex ssaIndex)
        => new NullStateSsaEdgeRefiner(ssaIndex);
}
```

- [ ] **Step 4: Implement `NullDerefClassifier`**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// A dereference of a possibly-null value, attributed to exactly one rule
/// code by provenance.
/// </summary>
internal sealed record NullDeref(string Code, string ReceiverName, Location Location);

/// <summary>
/// Shared deref-site detector and provenance classifier for the NRE rule
/// family. Returns at most one <see cref="NullDeref"/> per operation, so
/// one deref site never produces diagnostics from two family rules.
/// </summary>
internal static class NullDerefClassifier
{
    /// <summary>
    /// Classifies <paramref name="operation"/> if it is a deref site whose
    /// receiver is <see cref="NullState.DefinitelyNull"/> or
    /// <see cref="NullState.MaybeNull"/>; returns <see langword="null"/>
    /// otherwise. <see cref="NullState.Unknown"/> receivers are silent by
    /// design: no evidence, no warning.
    /// </summary>
    public static NullDeref? Classify(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        SsaIndex ssa)
    {
        var receiver = operation switch
        {
            IAwaitOperation awaitOp => awaitOp.Operation,
            IMemberReferenceOperation { Instance: { } instance } => instance,
            IInvocationOperation { Instance: { } instance } => instance,
            IArrayElementReferenceOperation arrayRef => arrayRef.ArrayReference,
            _ => null,
        };

        if (receiver is null)
        {
            return null;
        }

        receiver = Unwrap(receiver);

        TrackedKey? key = receiver switch
        {
            ILocalReferenceOperation l => new TrackedKey.Symbol(l.Local),
            IParameterReferenceOperation p => new TrackedKey.Symbol(p.Parameter),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f => new TrackedKey.InstanceField(f.Field),
            IFlowCaptureReferenceOperation c => new TrackedKey.Capture(c.Id),
            _ => null,
        };

        if (key is null || ssa.UseAt(receiver, key) is not { } id)
        {
            return null;
        }

        var nullState = state.TryGetValue(id, out var s) ? s : NullState.Unknown;
        if (nullState is not (NullState.DefinitelyNull or NullState.MaybeNull))
        {
            return null;
        }

        var code = ClassifyProvenance(operation, receiver, id, ssa);
        var name = receiver.Syntax.ToString();
        return new NullDeref(code, name, operation.Syntax.GetLocation());
    }

    private static string ClassifyProvenance(
        IOperation operation,
        IOperation receiver,
        SsaId id,
        SsaIndex ssa)
    {
        if (operation is IAwaitOperation)
        {
            return "V3168";
        }

        if (UnwrapParens(receiver.Syntax) is ConditionalAccessExpressionSyntax)
        {
            return "V3153";
        }

        if (receiver is ILocalReferenceOperation or IParameterReferenceOperation
            && DefIsConditionalAccess(id, ssa))
        {
            return "V3105";
        }

        return "V3080";
    }

    private static bool DefIsConditionalAccess(SsaId id, SsaIndex ssa)
    {
        // φ-results and entry versions have no def site — provenance is
        // ambiguous there and falls back to V3080.
        var rhsSyntax = ssa.DefSiteOf(id) switch
        {
            IVariableDeclaratorOperation { Initializer.Value: { } value } => value.Syntax,
            ISimpleAssignmentOperation assignment => assignment.Value.Syntax,
            _ => null,
        };

        return rhsSyntax is not null && UnwrapParens(rhsSyntax) is ConditionalAccessExpressionSyntax;
    }

    private static SyntaxNode UnwrapParens(SyntaxNode syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax paren)
        {
            syntax = paren.Expression;
        }

        return syntax;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conv:
                    operation = conv.Operand;
                    continue;
                case IParenthesizedOperation paren:
                    operation = paren.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }
}
```

- [ ] **Step 5: Implement V3080**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3080", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3080PossibleNullDereference : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3080",
        "Possible null dereference",
        "Possible null dereference of '{0}'",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var deref = NullDerefClassifier.Classify(operation, state, context.SsaIndex);
        if (deref is { Code: "V3080" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location, deref.ReceiverName));
        }
    }
}
```

- [ ] **Step 6: Run V3080 tests; inspect and accept received snapshots**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3080Tests"`

Inspect every `V3080.*.received.txt`:
- the five positives must contain exactly one V3080 diagnostic at the deref line;
- the five negatives (`guarded…`, `is_not_null…`, `reassigned…`, `unchecked_parameter…`,
  `early_return…`) must contain an empty `Diagnostics` list (Verify omits empty
  collections — the snapshot will simply lack the `Diagnostics` property).

Accept by renaming each `*.received.txt` → `*.verified.txt` ONLY after verifying contents.
Re-run the filter; expected: 10 passed.

- [ ] **Step 7: Run full suite**

Run: `dotnet test --configuration Release`
Expected: all pass. Note: V3080 may legitimately fire on other rules' snapshot sources, but
the harness id-filter keeps those snapshots unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/OpenVulScan.Rules.DataFlow/NullStateRuleBase.cs src/OpenVulScan.Rules.DataFlow/NullDerefClassifier.cs src/OpenVulScan.Rules.DataFlow/V3080PossibleNullDereference.cs tests/OpenVulScan.Rules.Tests/V3080Tests.cs tests/OpenVulScan.Rules.Tests/V3080.*.verified.txt
git commit -m "feat(rules): V3080 possible null dereference on shared NullState pipeline"
```

---

### Task 6: V3105 — use after null-conditional assignment

**Files:**
- Create: `src/OpenVulScan.Rules.DataFlow/V3105UseAfterNullConditional.cs`
- Test: `tests/OpenVulScan.Rules.Tests/V3105Tests.cs`

- [ ] **Step 1: Write the failing snapshot tests**

```csharp
using Xunit;

namespace OpenVulScan.Tests;

public class V3105Tests
{
    [Fact]
    public Task DeclaratorFromConditionalAccessThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "declarator_from_conditional_then_deref", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F;
        var n = x.Length;
    }
}");

    [Fact]
    public Task AssignmentFromConditionalAccessThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "assignment_from_conditional_then_deref", @"
class C
{
    string F;
    void M(C a)
    {
        string x;
        x = a?.F;
        var n = x.Length;
    }
}");

    [Fact]
    public Task ConditionalInvocationResultThenInvocation() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "conditional_invocation_result_then_call", @"
class C
{
    string F() => """";
    void M(C a)
    {
        var x = a?.F();
        var n = x.ToString();
    }
}");

    [Fact]
    public Task ParenthesizedConditionalRhsThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "parenthesized_conditional_rhs_then_deref", @"
class C
{
    string F;
    void M(C a)
    {
        var x = (a?.F);
        var n = x.Length;
    }
}");

    [Fact]
    public Task NullCheckedAfterConditionalIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "null_checked_after_conditional_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F;
        if (x != null)
        {
            var n = x.Length;
        }
    }
}");

    [Fact]
    public Task CoalesceFallbackIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "coalesce_fallback_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F ?? ""y"";
        var n = x.Length;
    }
}");

    [Fact]
    public Task PlainAssignmentDoesNotFireV3105() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "plain_null_assignment_not_v3105", @"
class C
{
    void M()
    {
        string x = null;
        var n = x.Length;
    }
}");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3105Tests"`
Expected: FAIL (no verified files; rule missing).

- [ ] **Step 3: Implement V3105**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3105", RuleSeverity.Level1, "CWE-690", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3105UseAfterNullConditional : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3105",
        "Use after null-conditional assignment",
        "The '{0}' variable was used after it was assigned through null-conditional operator. NullReferenceException is possible",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var deref = NullDerefClassifier.Classify(operation, state, context.SsaIndex);
        if (deref is { Code: "V3105" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location, deref.ReceiverName));
        }
    }
}
```

- [ ] **Step 4: Run, inspect, accept snapshots**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3105Tests"`

Inspect received files: 4 positives → one V3105 each at the deref line; 3 negatives →
no diagnostics. Special attention to `plain_null_assignment_not_v3105`: the deref fires
**V3080**, not V3105 — the V3105-filtered snapshot must be empty. Accept by renaming.
Expected after re-run: 7 passed.

- [ ] **Step 5: Run V3080 suite to confirm no classification regression**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3080Tests|FullyQualifiedName~V3105Tests"`
Expected: 17 passed.

- [ ] **Step 6: Commit**

```bash
git add src/OpenVulScan.Rules.DataFlow/V3105UseAfterNullConditional.cs tests/OpenVulScan.Rules.Tests/V3105Tests.cs tests/OpenVulScan.Rules.Tests/V3105.*.verified.txt
git commit -m "feat(rules): V3105 use after null-conditional assignment"
```

---

### Task 7: V3153 — dereference of a `?.` result

**Files:**
- Create: `src/OpenVulScan.Rules.DataFlow/V3153DereferenceOfConditionalAccessResult.cs`
- Test: `tests/OpenVulScan.Rules.Tests/V3153Tests.cs`

- [ ] **Step 1: Write the failing snapshot tests**

```csharp
using Xunit;

namespace OpenVulScan.Tests;

public class V3153Tests
{
    [Fact]
    public Task MemberAccessOnParenthesizedConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "member_access_on_parenthesized_conditional", @"
class C
{
    string F;
    void M(C a)
    {
        var n = (a?.F).Length;
    }
}");

    [Fact]
    public Task InvocationOnParenthesizedConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "invocation_on_parenthesized_conditional", @"
class C
{
    string F() => """";
    void M(C a)
    {
        var n = (a?.F()).ToString();
    }
}");

    [Fact]
    public Task ElementAccessOnParenthesizedConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "element_access_on_parenthesized_conditional", @"
class C
{
    int[] F;
    void M(C a)
    {
        var n = (a?.F)[0];
    }
}");

    [Fact]
    public Task NestedConditionalChainDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "nested_conditional_chain_deref", @"
class C
{
    C Next;
    string F;
    void M(C a)
    {
        var n = (a?.Next?.F).Length;
    }
}");

    [Fact]
    public Task ChainedConditionalAccessIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "chained_conditional_access_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var n = a?.F.Length;
    }
}");

    [Fact]
    public Task CoalesceGuardedConditionalIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "coalesce_guarded_conditional_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var n = (a?.F ?? ""y"").Length;
    }
}");

    [Fact]
    public Task VariableDerefDoesNotFireV3153() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "variable_deref_not_v3153", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F;
        var n = x.Length;
    }
}");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3153Tests"`
Expected: FAIL.

- [ ] **Step 3: Implement V3153**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3153", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3153DereferenceOfConditionalAccessResult : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3153",
        "Dereference of null-conditional access result",
        "Dereferencing the result of null-conditional access operator can lead to NullReferenceException",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var deref = NullDerefClassifier.Classify(operation, state, context.SsaIndex);
        if (deref is { Code: "V3153" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location));
        }
    }
}
```

- [ ] **Step 4: Run, inspect, accept snapshots**

Inspect received files: 4 positives → one V3153 each; 3 negatives → empty.
`chained_conditional_access_silent` is the language-semantics case (`a?.F.Length`
short-circuits — receiver of `.Length` is inside the conditional region and not
maybe-null on that path); `coalesce_guarded…` proves the refiner handles the lowered
`??` IsNull branch. Accept by renaming. Expected: 7 passed.

- [ ] **Step 5: Cross-rule regression**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3080Tests|FullyQualifiedName~V3105Tests|FullyQualifiedName~V3153Tests"`
Expected: 24 passed.

- [ ] **Step 6: Commit**

```bash
git add src/OpenVulScan.Rules.DataFlow/V3153DereferenceOfConditionalAccessResult.cs tests/OpenVulScan.Rules.Tests/V3153Tests.cs tests/OpenVulScan.Rules.Tests/V3153.*.verified.txt
git commit -m "feat(rules): V3153 dereference of null-conditional access result"
```

---

### Task 8: V3168 — await of a potentially-null expression

**Files:**
- Create: `src/OpenVulScan.Rules.DataFlow/V3168AwaitPossiblyNull.cs`
- Test: `tests/OpenVulScan.Rules.Tests/V3168Tests.cs`

Async snippets need `Task` metadata. `System.Private.CoreLib` (already referenced via
`typeof(object).Assembly`) defines `Task`, so the harness compilation resolves
`async Task` methods; residual compilation errors (e.g. missing `AsyncStateMachineAttribute`
facade types) do not prevent `GetOperation`/CFG construction. If CFG construction does fail
for async methods, add `MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location)`
to `SnapshotTestHarness.CreateTestCompilation` — test plumbing only.

- [ ] **Step 1: Write the failing snapshot tests**

```csharp
using Xunit;

namespace OpenVulScan.Tests;

public class V3168Tests
{
    [Fact]
    public Task AwaitConditionalInvocation() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_conditional_invocation", @"
using System.Threading.Tasks;
class C
{
    Task F() => Task.CompletedTask;
    async Task M(C a)
    {
        await a?.F();
    }
}");

    [Fact]
    public Task AwaitVariableAssignedFromConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_variable_from_conditional", @"
using System.Threading.Tasks;
class C
{
    Task F() => Task.CompletedTask;
    async Task M(C a)
    {
        var t = a?.F();
        await t;
    }
}");

    [Fact]
    public Task AwaitNullLiteralAssignedTask() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_null_literal_task", @"
using System.Threading.Tasks;
class C
{
    async Task M()
    {
        Task t = null;
        await t;
    }
}");

    [Fact]
    public Task AwaitTaskNulledOnOneBranch() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_task_nulled_on_branch", @"
using System.Threading.Tasks;
class C
{
    async Task M(bool c)
    {
        Task t = Task.CompletedTask;
        if (c) { t = null; }
        await t;
    }
}");

    [Fact]
    public Task AwaitCheckedTaskIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_checked_task_silent", @"
using System.Threading.Tasks;
class C
{
    Task F() => Task.CompletedTask;
    async Task M(C a)
    {
        var t = a?.F();
        if (t != null)
        {
            await t;
        }
    }
}");

    [Fact]
    public Task AwaitFreshTaskIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_fresh_task_silent", @"
using System.Threading.Tasks;
class C
{
    async Task M()
    {
        await Task.CompletedTask;
    }
}");

    [Fact]
    public Task AwaitUncheckedParameterIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_unchecked_parameter_silent", @"
using System.Threading.Tasks;
class C
{
    async Task M(Task t)
    {
        await t;
    }
}");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3168Tests"`
Expected: FAIL.

- [ ] **Step 3: Implement V3168**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3168", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3168AwaitPossiblyNull : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3168",
        "Await of potentially null expression",
        "Awaiting on expression with potential null value can lead to throwing of 'NullReferenceException'",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var deref = NullDerefClassifier.Classify(operation, state, context.SsaIndex);
        if (deref is { Code: "V3168" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location));
        }
    }
}
```

- [ ] **Step 4: Run, inspect, accept snapshots**

Inspect received files: 4 positives → one V3168 each (note
`await_variable_from_conditional` is V3168, **not** V3105 — the await node wins in the
classifier); 3 negatives → empty. Accept by renaming. Expected: 7 passed.

- [ ] **Step 5: Run the whole quartet**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3080Tests|FullyQualifiedName~V3105Tests|FullyQualifiedName~V3153Tests|FullyQualifiedName~V3168Tests"`
Expected: 31 passed (10 + 7 + 7 + 7).

- [ ] **Step 6: Commit**

```bash
git add src/OpenVulScan.Rules.DataFlow/V3168AwaitPossiblyNull.cs tests/OpenVulScan.Rules.Tests/V3168Tests.cs tests/OpenVulScan.Rules.Tests/V3168.*.verified.txt
git commit -m "feat(rules): V3168 await of potentially null expression"
```

---

### Task 9: `Synthetic.cs` zero-FP corpus + final gates

**Files:**
- Create: `tests/OpenVulScan.Rules.Tests/TestData/Synthetic.cs` (test data, excluded from compilation)
- Modify: `tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj`
- Test: `tests/OpenVulScan.Rules.Tests/SyntheticCorpusTests.cs`

- [ ] **Step 1: Create the corpus file**

`tests/OpenVulScan.Rules.Tests/TestData/Synthetic.cs` — realistic safe null-handling. The
file is test DATA: it must not be compiled into the test assembly.

```csharp
// Synthetic corpus: realistic null-safe patterns. The NRE quartet
// (V3080, V3105, V3153, V3168) must produce ZERO diagnostics here.
using System;
using System.Threading.Tasks;

namespace Synthetic;

public class GuardClauses
{
    public int IfGuard(string s)
    {
        if (s != null)
        {
            return s.Length;
        }
        return 0;
    }

    public int IsNotNullGuard(string s)
    {
        if (s is not null)
        {
            return s.Length;
        }
        return 0;
    }

    public int EarlyReturn(string s)
    {
        if (s == null) { return 0; }
        return s.Length;
    }

    public int IsNullEarlyReturn(string s)
    {
        if (s is null) { return 0; }
        return s.Length;
    }

    public int AndChainGuard(string a, string b)
    {
        if (a != null && b != null)
        {
            return a.Length + b.Length;
        }
        return 0;
    }

    public int OrNegativeGuard(string s)
    {
        if (s == null || s.Length == 0)
        {
            return 0;
        }
        return s.Length;
    }
}

public class Reassignment
{
    public int AssignedBeforeUse()
    {
        string s = null;
        s = "value";
        return s.Length;
    }

    public int AssignedOnAllBranches(bool c)
    {
        string s = null;
        if (c) { s = "a"; } else { s = "b"; }
        return s.Length;
    }

    public int LoopReassignment(int n)
    {
        string s = "seed";
        for (int i = 0; i < n; i++)
        {
            var len = s.Length;
            s = len.ToString();
        }
        return s.Length;
    }
}

public class Coalescing
{
    public int CoalesceLiteral(string s)
    {
        var t = s ?? "fallback";
        return t.Length;
    }

    public int CoalesceOnConditional(GuardClauses g)
    {
        var t = g?.ToString() ?? "fallback";
        return t.Length;
    }

    public int InlineCoalesceDeref(string s)
    {
        return (s ?? "fallback").Length;
    }
}

public class ConditionalChains
{
    public string Inner;

    public int? SafeChain(ConditionalChains c)
    {
        return c?.Inner?.Length;
    }

    public int CheckedConditionalResult(ConditionalChains c)
    {
        var inner = c?.Inner;
        if (inner != null)
        {
            return inner.Length;
        }
        return 0;
    }
}

public class AsyncPatterns
{
    public Task Work() => Task.CompletedTask;

    public async Task AwaitFresh()
    {
        await Task.CompletedTask;
    }

    public async Task AwaitChecked(AsyncPatterns p)
    {
        var t = p?.Work();
        if (t != null)
        {
            await t;
        }
    }

    public async Task AwaitParameter(Task t)
    {
        await t;
    }
}

public class FieldPatterns
{
    private string _name;

    public int GuardedField()
    {
        if (_name != null)
        {
            return _name.Length;
        }
        return 0;
    }

    public int AssignedField()
    {
        _name = "value";
        return _name.Length;
    }
}
```

- [ ] **Step 2: Exclude the corpus from compilation, copy to output**

In `tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj`, add an `ItemGroup`:

```xml
<ItemGroup>
  <Compile Remove="TestData/**/*.cs" />
  <None Include="TestData/**/*.cs" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

(2-space indent in csproj files per `.editorconfig`.)

- [ ] **Step 3: Write the zero-FP test**

`tests/OpenVulScan.Rules.Tests/SyntheticCorpusTests.cs`:

```csharp
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class SyntheticCorpusTests
{
    private static readonly string[] s_quartet = ["V3080", "V3105", "V3153", "V3168"];

    [Fact]
    public async Task QuartetProducesZeroDiagnosticsOnSyntheticCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic.cs");
        var source = await File.ReadAllTextAsync(path);

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SyntheticAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var registry = new RuleRegistry();
        registry.Scan(typeof(DataFlowRulesPlaceholder).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        var falsePositives = diagnostics
            .Where(d => s_quartet.Contains(d.Id))
            .Select(d => $"{d.Id} at {d.Location.GetLineSpan()}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToList();

        Assert.Empty(falsePositives);
    }
}
```

- [ ] **Step 4: Run the corpus test**

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~SyntheticCorpusTests"`
Expected: PASS with zero false positives. If it fails, each reported FP is a real precision
bug: diagnose (likely refiner gap or classifier over-reach), fix in
`NullStateSsaEdgeRefiner`/`NullDerefClassifier`, and add the failing pattern as a negative
snapshot case to the corresponding rule's test suite. Do NOT weaken the corpus to make the
test pass.

- [ ] **Step 5: Full suite + Release build (final gates)**

Run:
```bash
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```
Expected: build clean (warnings-as-errors), all tests pass
(456 baseline + ~50 new across Core/Rules).

- [ ] **Step 6: Commit**

```bash
git add tests/OpenVulScan.Rules.Tests/TestData/Synthetic.cs tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj tests/OpenVulScan.Rules.Tests/SyntheticCorpusTests.cs
git commit -m "test(rules): synthetic zero-FP corpus for the NRE quartet"
```

---

## Post-plan checklist (session close, not plan tasks)

- `bd close ovs-2qi.15` with reason; file follow-ups discovered during implementation.
- `git pull --rebase && bd dolt push && git push` — work is not done until pushed.
