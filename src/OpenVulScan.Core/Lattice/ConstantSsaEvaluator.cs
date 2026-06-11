using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Evaluates an expression against an SSA-keyed constant-propagation state.
/// </summary>
/// <remarks>
/// <para>
/// Reads value bindings through an <see cref="SsaIndex"/> instead of by variable name.
/// Shared by SSA-aware rules (V3022, V3063) that need to fold conditions into
/// concrete <see cref="bool"/> values when all referenced operands are constant.
/// </para>
/// </remarks>
public static class ConstantSsaEvaluator
{
    public static ConstantLatticeValue Evaluate(
        IOperation? operation,
        ImmutableDictionary<SsaId, ConstantLatticeValue> state,
        SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ssa);

        if (operation is null)
        {
            return ConstantLatticeValue.Bottom;
        }

        operation = Unwrap(operation);

        switch (operation)
        {
            case ILiteralOperation literal when literal.ConstantValue.HasValue && literal.ConstantValue.Value is not null:
                return ConstantLatticeValue.Const(literal.ConstantValue.Value);

            case ILiteralOperation { ConstantValue: { HasValue: true, Value: null } }:
                return ConstantLatticeValue.Top;

            case ILocalReferenceOperation localRef:
                return Lookup(localRef, new TrackedKey.Symbol(localRef.Local), state, ssa);

            case IParameterReferenceOperation paramRef:
                return Lookup(paramRef, new TrackedKey.Symbol(paramRef.Parameter), state, ssa);

            case IFieldReferenceOperation { Instance: IInstanceReferenceOperation } fieldRef:
                return Lookup(fieldRef, new TrackedKey.InstanceField(fieldRef.Field), state, ssa);

            case IBinaryOperation binary:
                return EvaluateBinary(binary, state, ssa);

            case IUnaryOperation unary:
                return EvaluateUnary(unary, state, ssa);

            default:
                return ConstantLatticeValue.Bottom;
        }
    }

    private static ConstantLatticeValue Lookup(
        IOperation operation,
        TrackedKey key,
        ImmutableDictionary<SsaId, ConstantLatticeValue> state,
        SsaIndex ssa)
    {
        var use = ssa.UseAt(operation, key);
        if (use is null)
        {
            // Variable exists but has no SSA definition at this point — value is unknown (Top).
            return ConstantLatticeValue.Top;
        }

        return state.TryGetValue(use.Value, out var value)
            ? value
            // SSA definition exists but its constant value is not yet computed — unknown (Top).
            : ConstantLatticeValue.Top;
    }

    private static ConstantLatticeValue EvaluateBinary(
        IBinaryOperation binary,
        ImmutableDictionary<SsaId, ConstantLatticeValue> state,
        SsaIndex ssa)
    {
        var left = Evaluate(binary.LeftOperand, state, ssa);
        var right = Evaluate(binary.RightOperand, state, ssa);

        if (left.Kind != LatticeElementKind.Const || right.Kind != LatticeElementKind.Const)
        {
            return ConstantLatticeValue.Bottom;
        }

        var l = left.Value!;
        var r = right.Value!;

        try
        {
            return binary.OperatorKind switch
            {
                BinaryOperatorKind.Add => ConstantLatticeValue.Const(Arithmetic.Add(l, r)),
                BinaryOperatorKind.Subtract => ConstantLatticeValue.Const(Arithmetic.Subtract(l, r)),
                BinaryOperatorKind.Multiply => ConstantLatticeValue.Const(Arithmetic.Multiply(l, r)),
                BinaryOperatorKind.Divide => ConstantLatticeValue.Const(Arithmetic.Divide(l, r)),
                BinaryOperatorKind.Equals => ConstantLatticeValue.Const(Equals(l, r)),
                BinaryOperatorKind.NotEquals => ConstantLatticeValue.Const(!Equals(l, r)),
                BinaryOperatorKind.GreaterThan => ConstantLatticeValue.Const(Arithmetic.GreaterThan(l, r)),
                BinaryOperatorKind.GreaterThanOrEqual => ConstantLatticeValue.Const(Arithmetic.GreaterThanOrEqual(l, r)),
                BinaryOperatorKind.LessThan => ConstantLatticeValue.Const(Arithmetic.LessThan(l, r)),
                BinaryOperatorKind.LessThanOrEqual => ConstantLatticeValue.Const(Arithmetic.LessThanOrEqual(l, r)),
                BinaryOperatorKind.ConditionalAnd => ConstantLatticeValue.Const((bool)l && (bool)r),
                BinaryOperatorKind.ConditionalOr => ConstantLatticeValue.Const((bool)l || (bool)r),
                BinaryOperatorKind.Or => ConstantLatticeValue.Const(BitwiseOr(l, r)),
                BinaryOperatorKind.And => ConstantLatticeValue.Const(BitwiseAnd(l, r)),
                BinaryOperatorKind.ExclusiveOr => ConstantLatticeValue.Const(BitwiseXor(l, r)),
                _ => ConstantLatticeValue.Bottom,
            };
        }
        catch (DivideByZeroException)
        {
            return ConstantLatticeValue.Bottom;
        }
        catch (InvalidCastException)
        {
            return ConstantLatticeValue.Bottom;
        }
        catch (NotSupportedException)
        {
            // Arithmetic/bitwise helpers throw NotSupportedException for unsupported
            // type combinations (e.g. short | int, byte + char). Treat as unknown.
            return ConstantLatticeValue.Bottom;
        }
    }

    private static ConstantLatticeValue EvaluateUnary(
        IUnaryOperation unary,
        ImmutableDictionary<SsaId, ConstantLatticeValue> state,
        SsaIndex ssa)
    {
        var operand = Evaluate(unary.Operand, state, ssa);

        if (operand.Kind != LatticeElementKind.Const)
        {
            return ConstantLatticeValue.Bottom;
        }

        var value = operand.Value!;

        try
        {
            return unary.OperatorKind switch
            {
                UnaryOperatorKind.Not => ConstantLatticeValue.Const(!(bool)value),
                UnaryOperatorKind.Minus => ConstantLatticeValue.Const(Arithmetic.Negate(value)),
                UnaryOperatorKind.BitwiseNegation => ConstantLatticeValue.Const(BitwiseNot(value)),
                UnaryOperatorKind.Plus => operand,
                _ => ConstantLatticeValue.Bottom,
            };
        }
        catch (InvalidCastException)
        {
            return ConstantLatticeValue.Bottom;
        }
        catch (OverflowException)
        {
            return ConstantLatticeValue.Bottom;
        }
        catch (NotSupportedException)
        {
            // BitwiseNot/Arithmetic.Negate throw NotSupportedException for unsupported
            // types (e.g. ~short). Treat as unknown.
            return ConstantLatticeValue.Bottom;
        }
    }

    private static IOperation Unwrap(IOperation operation)
    {
        // Strip all conversions: ConstantSsaTransfer stores raw (unconverted) values,
        // so we must not let a conversion wrapper reach the switch. Stripping all
        // conversions (not just identity ones) keeps Evaluate consistent with Transfer.
        while (operation is IConversionOperation conv)
        {
            operation = conv.Operand;
        }

        while (operation is IParenthesizedOperation paren)
        {
            operation = paren.Operand;
        }

        return operation;
    }

    private static object BitwiseOr(object left, object right) => left switch
    {
        int i when right is int j => i | j,
        long l when right is long r => l | r,
        uint i when right is uint j => i | j,
        ulong l when right is ulong r => l | r,
        bool b when right is bool r => b || r,
        _ => throw new NotSupportedException(),
    };

    private static object BitwiseAnd(object left, object right) => left switch
    {
        int i when right is int j => i & j,
        long l when right is long r => l & r,
        uint i when right is uint j => i & j,
        ulong l when right is ulong r => l & r,
        bool b when right is bool r => b && r,
        _ => throw new NotSupportedException(),
    };

    private static object BitwiseXor(object left, object right) => left switch
    {
        int i when right is int j => i ^ j,
        long l when right is long r => l ^ r,
        uint i when right is uint j => i ^ j,
        ulong l when right is ulong r => l ^ r,
        bool b when right is bool r => b ^ r,
        _ => throw new NotSupportedException(),
    };

    private static object BitwiseNot(object value) => value switch
    {
        int i => ~i,
        long l => ~l,
        uint i => ~i,
        ulong l => ~l,
        bool b => !b,
        _ => throw new NotSupportedException(),
    };

    private static class Arithmetic
    {
        public static object Add(object left, object right) => left switch
        {
            int i when right is int j => i + j,
            long l when right is long r => l + r,
            float f when right is float r => f + r,
            double d when right is double r => d + r,
            uint i when right is uint j => i + j,
            ulong l when right is ulong r => l + r,
            decimal d when right is decimal r => d + r,
            string s when right is string r => s + r,
            string s => s + right.ToString(),
            _ => right.ToString() + left.ToString(),
        };

        public static object Subtract(object left, object right) => left switch
        {
            int i when right is int j => i - j,
            long l when right is long r => l - r,
            float f when right is float r => f - r,
            double d when right is double r => d - r,
            uint i when right is uint j => i - j,
            ulong l when right is ulong r => l - r,
            decimal d when right is decimal r => d - r,
            _ => throw new NotSupportedException(),
        };

        public static object Multiply(object left, object right) => left switch
        {
            int i when right is int j => i * j,
            long l when right is long r => l * r,
            float f when right is float r => f * r,
            double d when right is double r => d * r,
            uint i when right is uint j => i * j,
            ulong l when right is ulong r => l * r,
            decimal d when right is decimal r => d * r,
            _ => throw new NotSupportedException(),
        };

        public static object Divide(object left, object right) => left switch
        {
            int i when right is int j => i / j,
            long l when right is long r => l / r,
            float f when right is float r => f / r,
            double d when right is double r => d / r,
            uint i when right is uint j => i / j,
            ulong l when right is ulong r => l / r,
            decimal d when right is decimal r => d / r,
            _ => throw new NotSupportedException(),
        };

        public static bool GreaterThan(object left, object right) => left switch
        {
            int i when right is int j => i > j,
            long l when right is long r => l > r,
            float f when right is float r => f > r,
            double d when right is double r => d > r,
            uint i when right is uint j => i > j,
            ulong l when right is ulong r => l > r,
            decimal d when right is decimal r => d > r,
            _ => throw new NotSupportedException(),
        };

        public static bool GreaterThanOrEqual(object left, object right) => left switch
        {
            int i when right is int j => i >= j,
            long l when right is long r => l >= r,
            float f when right is float r => f >= r,
            double d when right is double r => d >= r,
            uint i when right is uint j => i >= j,
            ulong l when right is ulong r => l >= r,
            decimal d when right is decimal r => d >= r,
            _ => throw new NotSupportedException(),
        };

        public static bool LessThan(object left, object right) => left switch
        {
            int i when right is int j => i < j,
            long l when right is long r => l < r,
            float f when right is float r => f < r,
            double d when right is double r => d < r,
            uint i when right is uint j => i < j,
            ulong l when right is ulong r => l < r,
            decimal d when right is decimal r => d < r,
            _ => throw new NotSupportedException(),
        };

        public static bool LessThanOrEqual(object left, object right) => left switch
        {
            int i when right is int j => i <= j,
            long l when right is long r => l <= r,
            float f when right is float r => f <= r,
            double d when right is double r => d <= r,
            uint i when right is uint j => i <= j,
            ulong l when right is ulong r => l <= r,
            decimal d when right is decimal r => d <= r,
            _ => throw new NotSupportedException(),
        };

        public static object Negate(object value) => value switch
        {
            int i => -i,
            long l => -l,
            float f => -f,
            double d => -d,
            decimal d => -d,
            _ => throw new NotSupportedException(),
        };
    }
}
