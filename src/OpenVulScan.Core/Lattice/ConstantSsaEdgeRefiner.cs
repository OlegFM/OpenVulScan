using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// SSA-aware edge refiner for constant-value analysis. Extracts constant
/// refinements from branch conditions and applies them to the
/// <see cref="SsaId"/>-keyed state map, giving V3022/V3063 path-sensitive
/// narrowing across equality guards.
/// </summary>
/// <remarks>
/// <para>
/// Recognises equality guards (<c>x == c</c>, <c>x != c</c> against a literal),
/// bare boolean conditions (<c>if (flag)</c>), recursion through <c>!</c>, and
/// <c>&amp;&amp;</c> / <c>||</c> short-circuit operators.
/// </para>
/// <para>
/// Narrowing is a <em>meet</em>: a value already known to be <c>Const(d)</c> on a
/// <c>x == c</c> (d ≠ c) edge becomes <see cref="ConstantLatticeValue.Bottom"/>
/// rather than being overwritten with <c>Const(c)</c>. The solver does not prune
/// infeasible edges, so this keeps a contradictory (dead) branch from folding a
/// downstream condition into a false positive.
/// </para>
/// </remarks>
public sealed class ConstantSsaEdgeRefiner : IEdgeRefiner<ImmutableDictionary<SsaId, ConstantLatticeValue>>
{
    private readonly SsaIndex _ssa;

    /// <summary>
    /// Initialises a new instance of <see cref="ConstantSsaEdgeRefiner"/>.
    /// </summary>
    /// <param name="ssa">The SSA index built for the method being analysed.</param>
    public ConstantSsaEdgeRefiner(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, ConstantLatticeValue> Refine(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state,
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

        var refinements = ImmutableArray.CreateBuilder<(SsaId Id, object Value)>();
        Collect(condition, whenTrue, refinements);

        foreach (var (id, value) in refinements)
        {
            var current = state.TryGetValue(id, out var s) ? s : ConstantLatticeValue.Top;
            state = state.SetItem(id, Narrow(current, value));
        }

        return state;
    }

    private void Collect(
        IOperation condition,
        bool whenTrue,
        ImmutableArray<(SsaId, object)>.Builder refinements)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            // Bare boolean condition: `if (flag)` ⇒ flag == whenTrue.
            case ILocalReferenceOperation or IParameterReferenceOperation
                or IFieldReferenceOperation { Instance: IInstanceReferenceOperation }
                or IFlowCaptureReferenceOperation
                when condition.Type?.SpecialType == SpecialType.System_Boolean:
                AddRefinement(condition, whenTrue, refinements);
                break;

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
        ImmutableArray<(SsaId, object)>.Builder refinements)
    {
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals when whenTrue && TryGetEquality(binary, out var op, out var value):
                AddRefinement(op, value, refinements);
                break;

            case BinaryOperatorKind.NotEquals when !whenTrue && TryGetEquality(binary, out var op, out var value):
                AddRefinement(op, value, refinements);
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

    private void AddRefinement(
        IOperation operand,
        object value,
        ImmutableArray<(SsaId, object)>.Builder refinements)
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
            refinements.Add((id, value));
        }
    }

    /// <summary>Meet of the current value with <c>Const(value)</c>.</summary>
    private static ConstantLatticeValue Narrow(ConstantLatticeValue current, object value)
        => current.Kind switch
        {
            LatticeElementKind.Top => ConstantLatticeValue.Const(value),
            LatticeElementKind.Bottom => ConstantLatticeValue.Bottom,
            // Const ⊓ Const: agreeing keeps it, contradicting collapses to ⊥
            // (the edge is infeasible — the solver still visits it, so this
            // prevents a dead branch from producing a downstream false positive).
            LatticeElementKind.Const => Equals(current.Value, value)
                ? current
                : ConstantLatticeValue.Bottom,
            _ => current,
        };

    private static bool TryGetEquality(IBinaryOperation binary, out IOperation operand, out object value)
    {
        var left = Unwrap(binary.LeftOperand);
        var right = Unwrap(binary.RightOperand);

        if (IsTrackedReference(left) && TryGetLiteral(right, out value))
        {
            operand = left;
            return true;
        }

        if (IsTrackedReference(right) && TryGetLiteral(left, out value))
        {
            operand = right;
            return true;
        }

        operand = binary;
        value = null!;
        return false;
    }

    private static bool IsTrackedReference(IOperation operation) => operation switch
    {
        ILocalReferenceOperation => true,
        IParameterReferenceOperation => true,
        IFieldReferenceOperation { Instance: IInstanceReferenceOperation } => true,
        IFlowCaptureReferenceOperation => true,
        _ => false,
    };

    private static bool TryGetLiteral(IOperation operation, out object value)
    {
        operation = Unwrap(operation);

        if (operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: { } literal } })
        {
            value = literal;
            return true;
        }

        value = null!;
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
