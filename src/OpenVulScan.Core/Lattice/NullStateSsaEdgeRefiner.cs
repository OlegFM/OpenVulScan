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

    /// <summary>
    /// Initialises a new instance of <see cref="NullStateSsaEdgeRefiner"/>.
    /// </summary>
    /// <param name="ssa">The SSA index built for the method being analysed.</param>
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
