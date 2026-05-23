using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer function for constant-value analysis tracked per variable
/// in an <see cref="ImmutableDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class ConstantMapTransfer : ITransfer<ImmutableDictionary<string, ConstantLatticeValue>>
{
    /// <inheritdoc />
    public ImmutableDictionary<string, ConstantLatticeValue> Apply(ImmutableDictionary<string, ConstantLatticeValue> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        switch (operation)
        {
            case ISimpleAssignmentOperation assignment when TryGetVariableName(assignment.Target, out var name):
                var valueState = EvaluateExpression(assignment.Value, state);
                return state.SetItem(name, valueState);

            case IVariableDeclaratorOperation varDecl when varDecl.Symbol is ILocalSymbol local:
                if (varDecl.Initializer is not null)
                {
                    var initState = EvaluateExpression(varDecl.Initializer.Value, state);
                    return state.SetItem(local.Name, initState);
                }

                return state.SetItem(local.Name, ConstantLatticeValue.Bottom);
        }

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<string, ConstantLatticeValue> Apply(ImmutableDictionary<string, ConstantLatticeValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        var result = state;
        foreach (var op in block.Operations)
        {
            result = Apply(result, op);
        }

        if (block.BranchValue is not null)
        {
            result = Apply(result, block.BranchValue);
        }

        return result;
    }

    public static ConstantLatticeValue EvaluateExpression(IOperation? operation, ImmutableDictionary<string, ConstantLatticeValue> state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (operation is null)
            return ConstantLatticeValue.Bottom;

        operation = Unwrap(operation);

        switch (operation)
        {
            case ILiteralOperation literal when literal.ConstantValue.HasValue:
                return ConstantLatticeValue.Const(literal.ConstantValue.Value!);

            case ILocalReferenceOperation localRef:
                return state.TryGetValue(localRef.Local.Name, out var localState)
                    ? localState
                    : ConstantLatticeValue.Bottom;

            case IParameterReferenceOperation paramRef:
                return state.TryGetValue(paramRef.Parameter.Name, out var paramState)
                    ? paramState
                    : ConstantLatticeValue.Bottom;

            case IConversionOperation conv:
                return EvaluateConversion(conv, state);

            case IBinaryOperation binary:
                return EvaluateBinary(binary, state);

            case IUnaryOperation unary:
                return EvaluateUnary(unary, state);

            default:
                return ConstantLatticeValue.Bottom;
        }
    }

    private static ConstantLatticeValue EvaluateConversion(IConversionOperation conv, ImmutableDictionary<string, ConstantLatticeValue> state)
    {
        var operandState = EvaluateExpression(conv.Operand, state);
        if (operandState.Kind == LatticeElementKind.Const && conv.Type is not null)
        {
            try
            {
                var converted = ConvertConstant(operandState.Value!, conv.Type);
                if (converted is not null)
                {
                    return ConstantLatticeValue.Const(converted);
                }
            }
            catch (InvalidCastException)
            {
                return ConstantLatticeValue.Top;
            }
            catch (OverflowException)
            {
                return ConstantLatticeValue.Top;
            }
        }

        return operandState;
    }

    private static object? ConvertConstant(object value, ITypeSymbol typeSymbol)
    {
        var type = typeSymbol.SpecialType switch
        {
            SpecialType.System_Int32 => typeof(int),
            SpecialType.System_Int64 => typeof(long),
            SpecialType.System_Single => typeof(float),
            SpecialType.System_Double => typeof(double),
            SpecialType.System_Decimal => typeof(decimal),
            SpecialType.System_UInt32 => typeof(uint),
            SpecialType.System_UInt64 => typeof(ulong),
            SpecialType.System_Boolean => typeof(bool),
            SpecialType.System_String => typeof(string),
            SpecialType.System_Byte => typeof(byte),
            SpecialType.System_SByte => typeof(sbyte),
            SpecialType.System_Int16 => typeof(short),
            SpecialType.System_UInt16 => typeof(ushort),
            SpecialType.System_Char => typeof(char),
            _ => null,
        };

        if (type is null)
        {
            return null;
        }

        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    private static ConstantLatticeValue EvaluateBinary(IBinaryOperation binary, ImmutableDictionary<string, ConstantLatticeValue> state)
    {
        var left = EvaluateExpression(binary.LeftOperand, state);
        var right = EvaluateExpression(binary.RightOperand, state);

        if (left.Kind != LatticeElementKind.Const || right.Kind != LatticeElementKind.Const)
            return ConstantLatticeValue.Bottom;

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
    }

    private static ConstantLatticeValue EvaluateUnary(IUnaryOperation unary, ImmutableDictionary<string, ConstantLatticeValue> state)
    {
        var operand = EvaluateExpression(unary.Operand, state);

        if (operand.Kind != LatticeElementKind.Const)
            return ConstantLatticeValue.Bottom;

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
    }

    private static object BitwiseOr(object left, object right)
    {
        return left switch
        {
            int i when right is int j => i | j,
            long l when right is long r => l | r,
            uint i when right is uint j => i | j,
            ulong l when right is ulong r => l | r,
            bool b when right is bool r => b || r,
            _ => throw new NotSupportedException(),
        };
    }

    private static object BitwiseAnd(object left, object right)
    {
        return left switch
        {
            int i when right is int j => i & j,
            long l when right is long r => l & r,
            uint i when right is uint j => i & j,
            ulong l when right is ulong r => l & r,
            bool b when right is bool r => b && r,
            _ => throw new NotSupportedException(),
        };
    }

    private static object BitwiseXor(object left, object right)
    {
        return left switch
        {
            int i when right is int j => i ^ j,
            long l when right is long r => l ^ r,
            uint i when right is uint j => i ^ j,
            ulong l when right is ulong r => l ^ r,
            bool b when right is bool r => b ^ r,
            _ => throw new NotSupportedException(),
        };
    }

    private static object BitwiseNot(object value)
    {
        return value switch
        {
            int i => ~i,
            long l => ~l,
            uint i => ~i,
            ulong l => ~l,
            bool b => !b,
            _ => throw new NotSupportedException(),
        };
    }

    private static bool TryGetVariableName(IOperation operation, out string name)
    {
        operation = Unwrap(operation);

        switch (operation)
        {
            case ILocalReferenceOperation localRef:
                name = localRef.Local.Name;
                return true;

            case IParameterReferenceOperation paramRef:
                name = paramRef.Parameter.Name;
                return true;

            default:
                name = string.Empty;
                return false;
        }
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conv && IsIdentityConversion(conv))
        {
            operation = conv.Operand;
        }

        while (operation is IParenthesizedOperation paren)
        {
            operation = paren.Operand;
        }

        return operation;
    }

    private static bool IsIdentityConversion(IConversionOperation conv)
    {
        return SymbolEqualityComparer.Default.Equals(conv.Type, conv.Operand.Type);
    }

    private static class Arithmetic
    {
        public static object Add(object left, object right)
        {
            return left switch
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
        }

        public static object Subtract(object left, object right)
        {
            return left switch
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
        }

        public static object Multiply(object left, object right)
        {
            return left switch
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
        }

        public static object Divide(object left, object right)
        {
            return left switch
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
        }

        public static bool GreaterThan(object left, object right)
        {
            return left switch
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
        }

        public static bool GreaterThanOrEqual(object left, object right)
        {
            return left switch
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
        }

        public static bool LessThan(object left, object right)
        {
            return left switch
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
        }

        public static bool LessThanOrEqual(object left, object right)
        {
            return left switch
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
        }

        public static object Negate(object value)
        {
            return value switch
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
}
