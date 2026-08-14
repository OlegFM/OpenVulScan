using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Edge refiner for the ownership analysis (V3114, V3073): on a control-flow edge where a
/// tracked resource is known to be <c>null</c>, the resource is marked
/// <see cref="OwnershipState.Disposed"/> — a null reference has nothing to leak, so the
/// null branch must not drag the resource back to <see cref="OwnershipState.Open"/> at the
/// join. This makes the idiomatic guarded dispose (<c>if (x != null) x.Dispose();</c> and the
/// early-return form <c>if (x == null) return;</c>) clean on every path, while a guard on an
/// unrelated condition (<c>if (flag) x.Dispose();</c>) still reports a partial dispose.
/// </summary>
/// <remarks>
/// Recognises <c>== null</c> / <c>!= null</c>, <c>is null</c> / <c>is not null</c>, recursion
/// through <c>!</c>, <c>&amp;&amp;</c> and <c>||</c>, and the lowered
/// <see cref="IIsNullOperation"/> branches. Refinement is applied only to keys in the tracked
/// set, mirroring <see cref="ResourceOwnershipTransfer"/>.
/// </remarks>
public sealed class OwnershipNullGuardEdgeRefiner : IEdgeRefiner<ImmutableDictionary<TrackedKey, OwnershipState>>
{
    private readonly IReadOnlySet<TrackedKey> _tracked;

    public OwnershipNullGuardEdgeRefiner(IReadOnlySet<TrackedKey> tracked)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        _tracked = tracked;
    }

    /// <inheritdoc />
    public ImmutableDictionary<TrackedKey, OwnershipState> Refine(
        ImmutableDictionary<TrackedKey, OwnershipState> state, ControlFlowBranch branch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(branch);

        if (branch.Source is not { BranchValue: { } condition } source
            || source.ConditionKind == ControlFlowConditionKind.None)
            return state;

        bool isConditional = source.ConditionalSuccessor == branch;
        bool isFallThrough = source.FallThroughSuccessor == branch;
        if (!isConditional && !isFallThrough)
            return state;

        // The conditional successor is taken when the condition matches ConditionKind;
        // the fall-through edge is its complement.
        bool whenTrue = isConditional == (source.ConditionKind == ControlFlowConditionKind.WhenTrue);

        var refinements = ImmutableArray.CreateBuilder<(TrackedKey Key, bool IsNull)>();
        Collect(condition, whenTrue, refinements);

        foreach (var (key, isNull) in refinements)
        {
            if (isNull && _tracked.Contains(key))
                state = state.SetItem(key, OwnershipState.Disposed);
        }

        return state;
    }

    private static void Collect(
        IOperation condition, bool whenTrue, ImmutableArray<(TrackedKey Key, bool IsNull)>.Builder sink)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            case IIsNullOperation isNull:
                AddRefinement(isNull.Operand, isNull: whenTrue, sink);
                break;

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary:
                Collect(unary.Operand, !whenTrue, sink);
                break;

            case IIsPatternOperation isPattern:
                CollectIsPattern(isPattern, whenTrue, sink);
                break;

            case IBinaryOperation binary:
                CollectBinary(binary, whenTrue, sink);
                break;
        }
    }

    private static void CollectBinary(
        IBinaryOperation binary, bool whenTrue, ImmutableArray<(TrackedKey Key, bool IsNull)>.Builder sink)
    {
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals when TryGetNullComparand(binary) is { } operand:
                AddRefinement(operand, isNull: whenTrue, sink);
                break;

            case BinaryOperatorKind.NotEquals when TryGetNullComparand(binary) is { } operand:
                AddRefinement(operand, isNull: !whenTrue, sink);
                break;

            case BinaryOperatorKind.ConditionalAnd when whenTrue:
                // a && b is true: both operands are true.
                Collect(binary.LeftOperand, whenTrue: true, sink);
                Collect(binary.RightOperand, whenTrue: true, sink);
                break;

            case BinaryOperatorKind.ConditionalOr when !whenTrue:
                // a || b is false: both operands are false.
                Collect(binary.LeftOperand, whenTrue: false, sink);
                Collect(binary.RightOperand, whenTrue: false, sink);
                break;
        }
    }

    private static void CollectIsPattern(
        IIsPatternOperation isPattern, bool whenTrue, ImmutableArray<(TrackedKey Key, bool IsNull)>.Builder sink)
    {
        // x is null
        if (isPattern.Pattern is IConstantPatternOperation { ConstantValue: { HasValue: true, Value: null } })
        {
            AddRefinement(isPattern.Value, isNull: whenTrue, sink);
            return;
        }

        // x is not null
        if (isPattern.Pattern is INegatedPatternOperation
            {
                Pattern: IConstantPatternOperation { ConstantValue: { HasValue: true, Value: null } }
            })
        {
            AddRefinement(isPattern.Value, isNull: !whenTrue, sink);
        }
    }

    private static void AddRefinement(
        IOperation operand, bool isNull, ImmutableArray<(TrackedKey Key, bool IsNull)>.Builder sink)
    {
        if (DisposeFlow.ResolveResourceKey(operand) is { } key)
            sink.Add((key, isNull));
    }

    /// <summary>The non-null operand of a comparison against the <c>null</c> literal, if any.</summary>
    private static IOperation? TryGetNullComparand(IBinaryOperation binary)
    {
        if (IsNullLiteral(binary.RightOperand))
            return binary.LeftOperand;
        if (IsNullLiteral(binary.LeftOperand))
            return binary.RightOperand;
        return null;
    }

    private static bool IsNullLiteral(IOperation operation)
        => Unwrap(operation) is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };

    private static IOperation Unwrap(IOperation op) => op switch
    {
        IConversionOperation c => Unwrap(c.Operand),
        IParenthesizedOperation p => Unwrap(p.Operand),
        _ => op,
    };
}
