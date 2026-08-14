# Interval SSA Pipeline + V3106 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SSA-keyed interval dataflow (evaluator + transfer + edge refiner) and the first range rule V3106 (definite index-out-of-bounds, intra-method).

**Architecture:** Mirrors the Constant SSA pipeline 1:1 — `IntervalSsaEvaluator`/`IntervalSsaTransfer`/`IntervalSsaEdgeRefiner` over `ImmutableDictionary<SsaId, IntervalValue>` with `MapLattice<SsaId, IntervalLattice, IntervalValue>`; the solver's back-edge widening (ovs-i1l) guarantees termination. Array-typed defs track the array's LENGTH interval.

**Tech Stack:** .NET 10, Roslyn IOperation/ControlFlowGraph, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-14-interval-ssa-pipeline-v3106-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all` — every file must build warning-clean.
- Flat namespace `OpenVulScan`; tests namespace `OpenVulScan.Tests` / `OpenVulScan.Tests.Ssa`.
- CA1707: no underscores in Rules.Tests method names (Core.Tests allows them).
- DataFlowRule subclasses MUST be stateless (dispatcher groups by transfer/refiner type).
- Unknown ⇒ `IntervalValue.Top` (sound); `Empty` (⊥) only means "unreachable/uncomputed".
- Commits: `git -c user.name="Oleg" -c user.email="olegefm@gmail.com" commit …` + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: IntervalSsaEvaluator + IntervalSsaTransfer

**Files:**
- Create: `src/OpenVulScan.Core/Lattice/IntervalSsaEvaluator.cs`
- Create: `src/OpenVulScan.Core/Lattice/IntervalSsaTransfer.cs`
- Test: `tests/OpenVulScan.Core.Tests/Ssa/IntervalSsaTransferTests.cs`

**Interfaces:**
- Consumes: `SsaIndex` (`DefinitionAt(IOperation)`, `UseAt(IOperation, TrackedKey)`, `PhisAt(BasicBlock)`, `AllVersions(TrackedKey)`), `TrackedKey.Symbol/InstanceField/Capture`, `IntervalValue` algebra, `IntervalLattice`, `OperationTree.Enumerate(BasicBlock)`.
- Produces: `public static IntervalValue IntervalSsaEvaluator.Evaluate(IOperation? operation, ImmutableDictionary<SsaId, IntervalValue> state, SsaIndex ssa)`; `public sealed class IntervalSsaTransfer : ITransfer<ImmutableDictionary<SsaId, IntervalValue>>` with ctor `(SsaIndex ssa)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/OpenVulScan.Core.Tests/Ssa/IntervalSsaTransferTests.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class IntervalSsaTransferTests
{
    [Fact]
    public void Declaration_WithLiteralAndArithmetic_TracksInterval()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int a = 5;
        int b = a + 2;
        int c = b * 3;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        Assert.Equal(IntervalValue.Constant(21), LastVersionValue(state, model, index, "c"));
    }

    [Fact]
    public void ArrayCreation_TracksLengthIntervalForArrayVariable()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        var arr = new int[10];
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        Assert.Equal(IntervalValue.Constant(10), LastVersionValue(state, model, index, "arr"));
    }

    [Fact]
    public void Branch_JoinsAtPhi_ProducesHull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int M(bool f)
    {
        int x = 1;
        if (f) { x = 5; }
        return x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        // The φ at the merge must be the convex hull [1, 5].
        var sym = LocalSymbol(model, "x");
        var versions = index.AllVersions(new TrackedKey.Symbol(sym));
        Assert.Contains(IntervalValue.Range(1, 5), versions.Select(v => state.TryGetValue(v, out var s) ? s : IntervalValue.Empty));
    }

    [Fact]
    public void UnknownParameter_IsTop()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(int n)
    {
        int y = n;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        Assert.Equal(IntervalValue.Top, LastVersionValue(state, model, index, "y"));
    }

    private static ImmutableDictionary<SsaId, IntervalValue> RunToFixpoint(
        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph cfg, SsaIndex index)
    {
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, IntervalValue>>(
            new MapLattice<SsaId, IntervalLattice, IntervalValue>(),
            new IntervalSsaTransfer(index));
        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        // Exit block's out-state carries the final map.
        return result.OutStates[cfg.Blocks.Last()];
    }

    private static ISymbol LocalSymbol(SemanticModel model, string name)
        => model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot().DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .First(v => v.Identifier.ValueText == name))!;

    private static IntervalValue LastVersionValue(
        ImmutableDictionary<SsaId, IntervalValue> state, SemanticModel model, SsaIndex index, string name)
    {
        var versions = index.AllVersions(new TrackedKey.Symbol(LocalSymbol(model, name)));
        return state[versions[^1]];
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests --filter IntervalSsaTransferTests`
Expected: compilation FAILURE — `IntervalSsaTransfer` does not exist.

- [ ] **Step 3: Implement evaluator + transfer**

```csharp
// src/OpenVulScan.Core/Lattice/IntervalSsaEvaluator.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Evaluates an expression to an <see cref="IntervalValue"/> against an SSA-keyed interval state.
/// </summary>
/// <remarks>
/// Convention: for an ARRAY-typed definition the tracked interval is the array's LENGTH
/// (an <see cref="IArrayCreationOperation"/> evaluates to its first dimension size), so
/// V3106-style rules can compare index intervals against it. Unknown ⇒ ⊤; ∅ only flows
/// in from unreachable paths.
/// </remarks>
public static class IntervalSsaEvaluator
{
    public static IntervalValue Evaluate(
        IOperation? operation,
        ImmutableDictionary<SsaId, IntervalValue> state,
        SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ssa);

        if (operation is null)
            return IntervalValue.Top;

        operation = Unwrap(operation);

        return operation switch
        {
            ILiteralOperation literal => EvaluateLiteral(literal),
            ILocalReferenceOperation localRef =>
                Lookup(localRef, new TrackedKey.Symbol(localRef.Local), state, ssa),
            IParameterReferenceOperation paramRef =>
                Lookup(paramRef, new TrackedKey.Symbol(paramRef.Parameter), state, ssa),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } fieldRef =>
                Lookup(fieldRef, new TrackedKey.InstanceField(fieldRef.Field), state, ssa),
            IFlowCaptureReferenceOperation captureRef =>
                Lookup(captureRef, new TrackedKey.Capture(captureRef.Id), state, ssa),
            ISimpleAssignmentOperation assign => Evaluate(assign.Value, state, ssa),
            IArrayCreationOperation creation => EvaluateArrayCreation(creation, state, ssa),
            IBinaryOperation binary => EvaluateBinary(binary, state, ssa),
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus } unary =>
                Evaluate(unary.Operand, state, ssa).Negate(),
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Plus } unary =>
                Evaluate(unary.Operand, state, ssa),
            _ => IntervalValue.Top,
        };
    }

    internal static bool TryGetIntegral(object? value, out long result)
    {
        switch (value)
        {
            case sbyte v: result = v; return true;
            case byte v: result = v; return true;
            case short v: result = v; return true;
            case ushort v: result = v; return true;
            case int v: result = v; return true;
            case uint v: result = v; return true;
            case long v: result = v; return true;
            case char v: result = v; return true;
            case ulong v when v <= long.MaxValue: result = (long)v; return true;
            default: result = 0; return false;
        }
    }

    private static IntervalValue EvaluateLiteral(ILiteralOperation literal)
        => literal.ConstantValue is { HasValue: true, Value: { } value } && TryGetIntegral(value, out long integral)
            ? IntervalValue.Constant(integral)
            : IntervalValue.Top;

    private static IntervalValue EvaluateArrayCreation(
        IArrayCreationOperation creation,
        ImmutableDictionary<SsaId, IntervalValue> state,
        SsaIndex ssa)
        => creation.DimensionSizes.Length == 1
            ? Evaluate(creation.DimensionSizes[0], state, ssa)
            : IntervalValue.Top;

    private static IntervalValue EvaluateBinary(
        IBinaryOperation binary,
        ImmutableDictionary<SsaId, IntervalValue> state,
        SsaIndex ssa)
    {
        var left = Evaluate(binary.LeftOperand, state, ssa);
        var right = Evaluate(binary.RightOperand, state, ssa);

        return binary.OperatorKind switch
        {
            BinaryOperatorKind.Add => left.Add(right),
            BinaryOperatorKind.Subtract => left.Subtract(right),
            BinaryOperatorKind.Multiply => left.Multiply(right),
            BinaryOperatorKind.Divide => left.Divide(right),
            _ => IntervalValue.Top,
        };
    }

    private static IntervalValue Lookup(
        IOperation operation,
        TrackedKey key,
        ImmutableDictionary<SsaId, IntervalValue> state,
        SsaIndex ssa)
    {
        var use = ssa.UseAt(operation, key);
        if (use is null)
            return IntervalValue.Top;

        return state.TryGetValue(use.Value, out var value) ? value : IntervalValue.Top;
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

```csharp
// src/OpenVulScan.Core/Lattice/IntervalSsaTransfer.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer functions for <see cref="IntervalValue"/> tracked per SSA version.
/// Mirrors <see cref="ConstantSsaTransfer"/>; expression evaluation is shared with rules
/// via <see cref="IntervalSsaEvaluator"/>.
/// </summary>
public sealed class IntervalSsaTransfer : ITransfer<ImmutableDictionary<SsaId, IntervalValue>>
{
    private static readonly IntervalLattice _lattice = new();
    private readonly SsaIndex _ssa;

    public IntervalSsaTransfer(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> Apply(
        ImmutableDictionary<SsaId, IntervalValue> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        var def = _ssa.DefinitionAt(operation);
        if (def is null) return state;

        var value = operation switch
        {
            IVariableDeclaratorOperation { Initializer: { } init } =>
                IntervalSsaEvaluator.Evaluate(init.Value, state, _ssa),
            ISimpleAssignmentOperation assignment =>
                IntervalSsaEvaluator.Evaluate(assignment.Value, state, _ssa),
            IFlowCaptureOperation capture =>
                IntervalSsaEvaluator.Evaluate(capture.Value, state, _ssa),
            _ => IntervalValue.Top,
        };
        return state.SetItem(def.Value, value);
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> Apply(
        ImmutableDictionary<SsaId, IntervalValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        state = ApplyPhis(state, block);

        foreach (var op in OperationTree.Enumerate(block))
            state = Apply(state, op);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> ApplyPhis(
        ImmutableDictionary<SsaId, IntervalValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = IntervalValue.Empty;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                    joined = _lattice.Join(joined, s);
            }
            state = state.SetItem(phi.Result, joined);
        }

        return state;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OpenVulScan.Core.Tests --filter IntervalSsaTransferTests`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenVulScan.Core/Lattice/IntervalSsaEvaluator.cs src/OpenVulScan.Core/Lattice/IntervalSsaTransfer.cs tests/OpenVulScan.Core.Tests/Ssa/IntervalSsaTransferTests.cs
git -c user.name="Oleg" -c user.email="olegefm@gmail.com" commit -m "feat(core): interval SSA evaluator and transfer (#ovs-kmj)"
```

---

### Task 2: IntervalSsaEdgeRefiner

**Files:**
- Create: `src/OpenVulScan.Core/Lattice/IntervalSsaEdgeRefiner.cs`
- Test: `tests/OpenVulScan.Core.Tests/Lattice/IntervalSsaEdgeRefinerTests.cs`

**Interfaces:**
- Consumes: Task 1's transfer; `IEdgeRefiner<T>.Refine(T state, ControlFlowBranch branch)`; `ControlFlowBranch.Source.{BranchValue, ConditionKind, ConditionalSuccessor, FallThroughSuccessor}`.
- Produces: `public sealed class IntervalSsaEdgeRefiner : IEdgeRefiner<ImmutableDictionary<SsaId, IntervalValue>>` with ctor `(SsaIndex ssa)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/OpenVulScan.Core.Tests/Lattice/IntervalSsaEdgeRefinerTests.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests;

public class IntervalSsaEdgeRefinerTests
{
    [Fact]
    public void RelationalGuard_RefinesTrueBranch()
    {
        // Inside `if (n < 10)`, y = n must be ⊑ [-∞, 9].
        var state = SolveAndGetLast(@"
class C
{
    void M(int n)
    {
        if (n < 10)
        {
            int y = n;
        }
    }
}", "y");

        Assert.False(state.IsEmpty);
        Assert.True(state.Upper <= 9, $"expected upper <= 9, got {state}");
    }

    [Fact]
    public void CompoundGuard_RefinesBothBounds()
    {
        // Inside `if (n >= 0 && n < 10)`, y = n must be ⊑ [0, 9].
        var state = SolveAndGetLast(@"
class C
{
    void M(int n)
    {
        if (n >= 0 && n < 10)
        {
            int y = n;
        }
    }
}", "y");

        Assert.Equal(IntervalValue.Range(0, 9), state);
    }

    [Fact]
    public void NegatedGuard_RefinesFalseBranchViaElse()
    {
        // In the else of `if (n < 10)`, y = n must be ⊑ [10, +∞].
        var state = SolveAndGetLast(@"
class C
{
    void M(int n)
    {
        if (n < 10) { } else { int y = n; }
    }
}", "y");

        Assert.False(state.IsEmpty);
        Assert.True(state.Lower >= 10, $"expected lower >= 10, got {state}");
    }

    [Fact]
    public void CountingLoop_WidensThenRefines_BodySeesBoundedIndex()
    {
        // Widening sends i to [0, +∞] at the header; the i < 10 true-edge
        // refinement must bound the BODY's view to [0, 9].
        var state = SolveAndGetLast(@"
class C
{
    void M()
    {
        for (int i = 0; i < 10; i = i + 1)
        {
            int y = i;
        }
    }
}", "y");

        Assert.Equal(IntervalValue.Range(0, 9), state);
    }

    [Fact]
    public void ReversedOperands_LiteralOnLeft_Mirrored()
    {
        // `10 > n` ≡ `n < 10`.
        var state = SolveAndGetLast(@"
class C
{
    void M(int n)
    {
        if (10 > n)
        {
            int y = n;
        }
    }
}", "y");

        Assert.False(state.IsEmpty);
        Assert.True(state.Upper <= 9, $"expected upper <= 9, got {state}");
    }

    private static IntervalValue SolveAndGetLast(string source, string localName)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(source);
        var index = SsaBuilder.Build(cfg, model);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, IntervalValue>>(
            new MapLattice<SsaId, IntervalLattice, IntervalValue>(),
            new IntervalSsaTransfer(index),
            new IntervalSsaEdgeRefiner(index));
        var result = solver.Solve(cfg);
        Assert.True(result.Converged);

        var sym = (ISymbol)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot().DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .First(v => v.Identifier.ValueText == localName))!;
        var versions = index.AllVersions(new TrackedKey.Symbol(sym));
        Assert.Single(versions);

        // The def sits in the guarded block; find the block whose out-state defines it.
        foreach (var block in cfg.Blocks)
        {
            if (result.OutStates[block].TryGetValue(versions[0], out var v))
                return v;
        }

        return IntervalValue.Empty;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests --filter IntervalSsaEdgeRefinerTests`
Expected: compilation FAILURE — `IntervalSsaEdgeRefiner` does not exist.

- [ ] **Step 3: Implement the refiner**

```csharp
// src/OpenVulScan.Core/Lattice/IntervalSsaEdgeRefiner.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// SSA-aware edge refiner for interval analysis. Narrows tracked intervals along branch
/// edges guarded by relational comparisons against integer literals
/// (<c>x &lt; c</c>, <c>x &lt;= c</c>, <c>x &gt; c</c>, <c>x &gt;= c</c>, <c>x == c</c>,
/// and the false edge of <c>x != c</c>), recursing through <c>!</c>, <c>&amp;&amp;</c>,
/// <c>||</c>. Literal-on-the-left comparisons are mirrored.
/// </summary>
/// <remarks>
/// Narrowing is a meet (<see cref="IntervalValue.Intersect"/>): a contradictory guard on a
/// dead edge collapses to ∅ instead of widening the state, keeping infeasible paths from
/// producing downstream false positives. Endpoints <c>c±1</c> saturate at the
/// <see cref="long"/> extremes — saturation over-approximates (⊇ the true set), never under.
/// </remarks>
public sealed class IntervalSsaEdgeRefiner : IEdgeRefiner<ImmutableDictionary<SsaId, IntervalValue>>
{
    private readonly SsaIndex _ssa;

    public IntervalSsaEdgeRefiner(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> Refine(
        ImmutableDictionary<SsaId, IntervalValue> state,
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

        bool whenTrue = isConditional == (source.ConditionKind == ControlFlowConditionKind.WhenTrue);

        var refinements = ImmutableArray.CreateBuilder<(SsaId Id, IntervalValue Constraint)>();
        Collect(condition, whenTrue, refinements);

        foreach (var (id, constraint) in refinements)
        {
            var current = state.TryGetValue(id, out var s) ? s : IntervalValue.Top;
            state = state.SetItem(id, current.Intersect(constraint));
        }

        return state;
    }

    private void Collect(
        IOperation condition,
        bool whenTrue,
        ImmutableArray<(SsaId, IntervalValue)>.Builder refinements)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary:
                Collect(unary.Operand, !whenTrue, refinements);
                break;

            case IBinaryOperation binary:
                CollectBinary(binary, whenTrue, refinements);
                break;

            default:
                break;
        }
    }

    private void CollectBinary(
        IBinaryOperation binary,
        bool whenTrue,
        ImmutableArray<(SsaId, IntervalValue)>.Builder refinements)
    {
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.ConditionalAnd when whenTrue:
                Collect(binary.LeftOperand, whenTrue: true, refinements);
                Collect(binary.RightOperand, whenTrue: true, refinements);
                return;

            case BinaryOperatorKind.ConditionalOr when !whenTrue:
                Collect(binary.LeftOperand, whenTrue: false, refinements);
                Collect(binary.RightOperand, whenTrue: false, refinements);
                return;

            default:
                break;
        }

        if (!TryGetComparison(binary, out var operand, out long constant, out var op))
        {
            return;
        }

        if (ConstraintFor(op, constant, whenTrue) is { } constraint)
        {
            AddRefinement(operand, constraint, refinements);
        }
    }

    /// <summary>The interval implied for <c>x</c> by <c>x ⟨op⟩ c</c> being <paramref name="whenTrue"/>.</summary>
    private static IntervalValue? ConstraintFor(BinaryOperatorKind op, long c, bool whenTrue)
        => (op, whenTrue) switch
        {
            (BinaryOperatorKind.LessThan, true) => IntervalValue.Range(IntervalValue.NegativeInfinity, SatDec(c)),
            (BinaryOperatorKind.LessThan, false) => IntervalValue.Range(c, IntervalValue.PositiveInfinity),
            (BinaryOperatorKind.LessThanOrEqual, true) => IntervalValue.Range(IntervalValue.NegativeInfinity, c),
            (BinaryOperatorKind.LessThanOrEqual, false) => IntervalValue.Range(SatInc(c), IntervalValue.PositiveInfinity),
            (BinaryOperatorKind.GreaterThan, true) => IntervalValue.Range(SatInc(c), IntervalValue.PositiveInfinity),
            (BinaryOperatorKind.GreaterThan, false) => IntervalValue.Range(IntervalValue.NegativeInfinity, c),
            (BinaryOperatorKind.GreaterThanOrEqual, true) => IntervalValue.Range(c, IntervalValue.PositiveInfinity),
            (BinaryOperatorKind.GreaterThanOrEqual, false) => IntervalValue.Range(IntervalValue.NegativeInfinity, SatDec(c)),
            (BinaryOperatorKind.Equals, true) => IntervalValue.Constant(c),
            (BinaryOperatorKind.NotEquals, false) => IntervalValue.Constant(c),
            // != when true / == when false cannot be represented as one convex interval.
            _ => null,
        };

    // Saturating: at the long extremes the exact predicate is unsatisfiable; saturation
    // over-approximates ∅ with a singleton, which is sound.
    private static long SatDec(long c) => c == long.MinValue ? long.MinValue : c - 1;

    private static long SatInc(long c) => c == long.MaxValue ? long.MaxValue : c + 1;

    private void AddRefinement(
        IOperation operand,
        IntervalValue constraint,
        ImmutableArray<(SsaId, IntervalValue)>.Builder refinements)
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
            refinements.Add((id, constraint));
        }
    }

    private static bool TryGetComparison(
        IBinaryOperation binary,
        out IOperation operand,
        out long constant,
        out BinaryOperatorKind normalizedOp)
    {
        var left = Unwrap(binary.LeftOperand);
        var right = Unwrap(binary.RightOperand);

        if (IsTrackedReference(left) && TryGetIntegralLiteral(right, out constant))
        {
            operand = left;
            normalizedOp = binary.OperatorKind;
            return true;
        }

        if (IsTrackedReference(right) && TryGetIntegralLiteral(left, out constant))
        {
            operand = right;
            normalizedOp = Mirror(binary.OperatorKind);
            return true;
        }

        operand = binary;
        constant = 0;
        normalizedOp = binary.OperatorKind;
        return false;
    }

    /// <summary>Mirrors a comparison when the literal is on the LEFT: <c>c &lt; x</c> ≡ <c>x &gt; c</c>.</summary>
    private static BinaryOperatorKind Mirror(BinaryOperatorKind op) => op switch
    {
        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
        BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
        BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
        _ => op,
    };

    private static bool IsTrackedReference(IOperation operation) => operation switch
    {
        ILocalReferenceOperation => true,
        IParameterReferenceOperation => true,
        IFieldReferenceOperation { Instance: IInstanceReferenceOperation } => true,
        IFlowCaptureReferenceOperation => true,
        _ => false,
    };

    private static bool TryGetIntegralLiteral(IOperation operation, out long value)
    {
        operation = Unwrap(operation);

        if (operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: { } literal } }
            && IntervalSsaEvaluator.TryGetIntegral(literal, out value))
        {
            return true;
        }

        value = 0;
        return false;
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OpenVulScan.Core.Tests --filter IntervalSsaEdgeRefinerTests`
Expected: 5 PASS (plus Task 1's 4 still green).

- [ ] **Step 5: Commit**

```bash
git add src/OpenVulScan.Core/Lattice/IntervalSsaEdgeRefiner.cs tests/OpenVulScan.Core.Tests/Lattice/IntervalSsaEdgeRefinerTests.cs
git -c user.name="Oleg" -c user.email="olegefm@gmail.com" commit -m "feat(core): interval SSA edge refiner for relational guards (#ovs-kmj)"
```

---

### Task 3: V3106 rule (definite index out of bounds)

**Files:**
- Create: `src/OpenVulScan.Rules.DataFlow/V3106IndexOutOfBounds.cs`
- Test: `tests/OpenVulScan.Rules.Tests/V3106Tests.cs`

**Interfaces:**
- Consumes: Tasks 1–2 pipeline; `DataFlowRule<TLattice>` (abstract `Lattice`, virtual `CreateTransfer`/`CreateEdgeRefiner`, `OnState(IOperation, TLattice, DataFlowContext)`); `DataFlowContext.{SsaIndex, ReportDiagnostic}`; `RuleAttribute(code, RuleSeverity, cwe, RuleCategory, AnalysisCapability)`.
- Produces: rule class `V3106IndexOutOfBounds`, auto-discovered by `RuleScheduler` (reflective `DataFlowRuleDispatcher<>` per state type — no registration edits needed).

- [ ] **Step 1: Write the failing tests** (CA1707: no underscores in method names)

```csharp
// tests/OpenVulScan.Rules.Tests/V3106Tests.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class V3106Tests
{
    [Fact]
    public void ConstantIndexEqualToLengthDetected()
    {
        var diagnostics = Run(@"
class C
{
    int M()
    {
        var a = new int[10];
        return a[10];
    }
}");
        var d = Assert.Single(diagnostics);
        Assert.Equal("V3106", d.Id);
    }

    [Fact]
    public void NegativeConstantIndexDetected()
    {
        var diagnostics = Run(@"
class C
{
    int M()
    {
        var a = new int[10];
        int i = -1;
        return a[i];
    }
}");
        Assert.Single(diagnostics);
    }

    [Fact]
    public void GuardProvenOutOfRangeDetected()
    {
        var diagnostics = Run(@"
class C
{
    int M(int i)
    {
        var a = new int[10];
        if (i >= 10)
        {
            return a[i];
        }
        return 0;
    }
}");
        Assert.Single(diagnostics);
    }

    [Fact]
    public void InRangeConstantIndexNotFlagged()
    {
        var diagnostics = Run(@"
class C
{
    int M()
    {
        var a = new int[10];
        return a[9];
    }
}");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownIndexNotFlagged()
    {
        var diagnostics = Run(@"
class C
{
    int M(int i)
    {
        var a = new int[10];
        return a[i];
    }
}");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownArrayLengthNotFlagged()
    {
        var diagnostics = Run(@"
class C
{
    int M(int[] a)
    {
        return a[100];
    }
}");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CountingLoopNotFlagged()
    {
        // Widening pushes i to [0, +∞] at the header; the i < 10 edge refinement
        // must bring the body's view back to [0, 9] — no diagnostic.
        var diagnostics = Run(@"
class C
{
    void M()
    {
        var a = new int[10];
        for (int i = 0; i < 10; i = i + 1)
        {
            a[i] = 0;
        }
    }
}");
        Assert.Empty(diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var rule = new V3106IndexOutOfBounds();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, IntervalValue>>(
            new[] { rule }, compilation);
        return dispatcher.Run(CancellationToken.None);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Rules.Tests --filter V3106Tests`
Expected: compilation FAILURE — `V3106IndexOutOfBounds` does not exist.

- [ ] **Step 3: Implement the rule**

```csharp
// src/OpenVulScan.Rules.DataFlow/V3106IndexOutOfBounds.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3106: array element access whose index is ALWAYS outside the array bounds.
/// Intra-method, definite-only (v1): flags when the index interval lies entirely at or
/// above the array-length interval's upper bound, or entirely below zero. The MAY variant
/// ("possibly out of bound", PVS wording) is a follow-up once corpus FP rates are known.
/// </summary>
[Rule("V3106", RuleSeverity.Level1, "CWE-125", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3106IndexOutOfBounds : DataFlowRule<ImmutableDictionary<SsaId, IntervalValue>>
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3106",
        "Index is out of bound",
        "Index {0} is always outside the bounds of the array",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ILattice<ImmutableDictionary<SsaId, IntervalValue>> Lattice { get; }
        = new MapLattice<SsaId, IntervalLattice, IntervalValue>();

    public override ITransfer<ImmutableDictionary<SsaId, IntervalValue>> CreateTransfer(SsaIndex ssaIndex)
        => new IntervalSsaTransfer(ssaIndex);

    public override IEdgeRefiner<ImmutableDictionary<SsaId, IntervalValue>>? CreateEdgeRefiner(SsaIndex ssaIndex)
        => new IntervalSsaEdgeRefiner(ssaIndex);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, IntervalValue> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (operation is not IArrayElementReferenceOperation { Indices.Length: 1 } elementRef)
        {
            return;
        }

        var index = IntervalSsaEvaluator.Evaluate(elementRef.Indices[0], state, context.SsaIndex);
        if (index.IsEmpty)
        {
            return; // unreachable path — no concrete execution reaches this access
        }

        // Array-typed defs track the array's LENGTH interval (see IntervalSsaEvaluator).
        var length = IntervalSsaEvaluator.Evaluate(elementRef.ArrayReference, state, context.SsaIndex);

        bool alwaysNegative = index.Upper < 0;
        bool alwaysPastEnd = !length.IsEmpty
            && !length.UpperIsInfinite
            && !index.LowerIsInfinite
            && index.Lower >= length.Upper;

        if (alwaysNegative || alwaysPastEnd)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                operation.Syntax.GetLocation(),
                index.ToString()));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OpenVulScan.Rules.Tests --filter V3106Tests`
Expected: 7 PASS.

- [ ] **Step 5: Run the FULL suite (scheduler discovers the new rule — corpus snapshots may shift)**

Run: `dotnet test`
Expected: all green. If an Integration.Tests corpus snapshot gains V3106 hits, inspect each — a genuine definite-OOB is a `.received`→`.verified` accept; anything else is an FP to fix before committing.

- [ ] **Step 6: Commit**

```bash
git add src/OpenVulScan.Rules.DataFlow/V3106IndexOutOfBounds.cs tests/OpenVulScan.Rules.Tests/V3106Tests.cs
git -c user.name="Oleg" -c user.email="olegefm@gmail.com" commit -m "feat(rules): V3106 definite index-out-of-bounds on interval pipeline (#ovs-kmj)"
```

---

### Task 4: Docs + bead close + push

- [ ] **Step 1:** `git add docs/superpowers/specs/2026-08-14-interval-ssa-pipeline-v3106-design.md docs/superpowers/plans/2026-08-14-interval-ssa-pipeline-v3106.md` and commit `docs(specs): interval SSA pipeline + V3106 design and plan (#ovs-kmj)`.
- [ ] **Step 2:** `bd close ovs-kmj --force`; file follow-up beads: MAY-variant V3106 + `Length` property tracking; `%`/shift/bitwise wiring in `IntervalSsaEvaluator`.
- [ ] **Step 3:** `bd export --all -o .beads/issues.jsonl`, commit `chore(beads): close ovs-kmj`, then `git pull --rebase && git push` (includes the still-unpushed widening commit) and verify `git status` is up to date.
