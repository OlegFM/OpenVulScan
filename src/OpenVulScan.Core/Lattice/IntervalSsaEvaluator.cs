using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Evaluates an expression to an <see cref="IntervalValue"/> against an SSA-keyed interval state.
/// </summary>
/// <remarks>
/// <para>
/// Convention: for an ARRAY-typed definition the tracked interval is the array's LENGTH — an
/// <see cref="IArrayCreationOperation"/> evaluates to its first dimension size — so range rules
/// (V3106) can compare an index interval against it. Array aliases propagate the length through
/// the ordinary SSA lookup.
/// </para>
/// <para>
/// Unknown ⇒ <see cref="IntervalValue.Top"/> (sound over-approximation);
/// <see cref="IntervalValue.Empty"/> (⊥) only flows in from unreachable paths.
/// </para>
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
            // The value of an assignment expression `(x = v)` is v.
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

    /// <summary>
    /// Embeds an integral boxed constant into <see cref="long"/>. All integral types embed
    /// exactly except <see cref="ulong"/> values above <see cref="long.MaxValue"/>.
    /// </summary>
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
