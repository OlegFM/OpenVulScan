using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// SSA-aware edge refiner for interval analysis. Narrows tracked intervals along branch
/// edges guarded by relational comparisons against integer literals
/// (<c>x &lt; c</c>, <c>x &lt;= c</c>, <c>x &gt; c</c>, <c>x &gt;= c</c>, <c>x == c</c>,
/// and the false edge of <c>x != c</c>), recursing through <c>!</c>, <c>&amp;&amp;</c> and
/// <c>||</c>. Literal-on-the-left comparisons are mirrored (<c>10 &gt; x</c> ≡ <c>x &lt; 10</c>).
/// </summary>
/// <remarks>
/// <para>
/// Narrowing is a meet (<see cref="IntervalValue.Intersect"/>): a contradictory guard on a
/// dead edge collapses the value to ∅ instead of widening it, keeping infeasible paths from
/// producing downstream false positives — same policy as <see cref="ConstantSsaEdgeRefiner"/>.
/// </para>
/// <para>
/// Endpoints <c>c±1</c> saturate at the <see cref="long"/> extremes: the exact predicate is
/// unsatisfiable there, and saturation over-approximates ∅ with a singleton, which is sound.
/// </para>
/// </remarks>
public sealed class IntervalSsaEdgeRefiner : IEdgeRefiner<ImmutableDictionary<SsaId, IntervalValue>>
{
    private readonly SsaIndex _ssa;

    /// <summary>
    /// Initialises a new instance of <see cref="IntervalSsaEdgeRefiner"/>.
    /// </summary>
    /// <param name="ssa">The SSA index built for the method being analysed.</param>
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

        // The conditional successor is taken when the condition matches
        // ConditionKind; the fall-through edge is its complement.
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

    /// <summary>
    /// The interval implied for <c>x</c> by <c>x ⟨op⟩ c</c> evaluating to
    /// <paramref name="whenTrue"/>, or <see langword="null"/> when the predicate has no
    /// convex representation (<c>!=</c> when true, <c>==</c> when false).
    /// </summary>
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
            _ => null,
        };

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
