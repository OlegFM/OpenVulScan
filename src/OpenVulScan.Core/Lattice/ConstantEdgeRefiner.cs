using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Edge refiner for constant-value analysis. Given a control-flow edge whose
/// source block ends with a conditional branch, it extracts constant-value
/// refinements from the branch condition and applies them to the state map.
/// </summary>
public sealed class ConstantEdgeRefiner : IEdgeRefiner<ImmutableDictionary<string, ConstantLatticeValue>>
{
    /// <inheritdoc />
    public ImmutableDictionary<string, ConstantLatticeValue> Refine(
        ImmutableDictionary<string, ConstantLatticeValue> state,
        ControlFlowBranch branch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(branch);

        if (branch.Source?.BranchValue is not IOperation condition)
        {
            return state;
        }

        bool isTrueBranch = branch.Source.FallThroughSuccessor == branch;
        bool isFalseBranch = branch.Source.ConditionalSuccessor == branch;

        if (!isTrueBranch && !isFalseBranch)
        {
            return state;
        }

        var refinements = CollectRefinements(condition, isTrueBranch);
        if (refinements.IsEmpty)
        {
            return state;
        }

        var builder = state.ToBuilder();
        foreach (var (name, refinedValue) in refinements)
        {
            if (builder.TryGetValue(name, out var currentValue))
            {
                builder[name] = refinedValue;
            }
            else
            {
                builder[name] = refinedValue;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<(string Name, ConstantLatticeValue Value)> CollectRefinements(
        IOperation condition,
        bool whenTrue)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            case IBinaryOperation binary:
                return CollectBinaryRefinements(binary, whenTrue);

            case IUnaryOperation unary when unary.OperatorKind == UnaryOperatorKind.Not:
                return CollectRefinements(unary.Operand, !whenTrue);

            default:
                return ImmutableArray<(string, ConstantLatticeValue)>.Empty;
        }
    }

    private static ImmutableArray<(string Name, ConstantLatticeValue Value)> CollectBinaryRefinements(
        IBinaryOperation binary,
        bool whenTrue)
    {
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals:
                return CollectEqualsRefinements(binary, whenTrue);

            case BinaryOperatorKind.NotEquals:
                return CollectEqualsRefinements(binary, !whenTrue);

            case BinaryOperatorKind.ConditionalAnd:
                if (whenTrue)
                {
                    var left = CollectRefinements(binary.LeftOperand, whenTrue: true);
                    var right = CollectRefinements(binary.RightOperand, whenTrue: true);
                    return left.AddRange(right);
                }

                return ImmutableArray<(string, ConstantLatticeValue)>.Empty;

            case BinaryOperatorKind.ConditionalOr:
                if (!whenTrue)
                {
                    var left = CollectRefinements(binary.LeftOperand, whenTrue: false);
                    var right = CollectRefinements(binary.RightOperand, whenTrue: false);
                    return left.AddRange(right);
                }

                return ImmutableArray<(string, ConstantLatticeValue)>.Empty;

            default:
                return ImmutableArray<(string, ConstantLatticeValue)>.Empty;
        }
    }

    private static ImmutableArray<(string Name, ConstantLatticeValue Value)> CollectEqualsRefinements(
        IBinaryOperation binary,
        bool whenTrue)
    {
        var left = Unwrap(binary.LeftOperand);
        var right = Unwrap(binary.RightOperand);

        if (whenTrue)
        {
            // x == const => then-branch: x = Const(const)
            if (TryGetVariableName(left, out var varName) && TryGetConstantValue(right, out var constValue))
            {
                return ImmutableArray.Create((varName, ConstantLatticeValue.Const(constValue)));
            }

            if (TryGetVariableName(right, out var varName2) && TryGetConstantValue(left, out var constValue2))
            {
                return ImmutableArray.Create((varName2, ConstantLatticeValue.Const(constValue2)));
            }
        }
        else
        {
            // x == const => else-branch: x = Top (not that const)
            if (TryGetVariableName(left, out var varName) && TryGetConstantValue(right, out _))
            {
                return ImmutableArray.Create((varName, ConstantLatticeValue.Top));
            }

            if (TryGetVariableName(right, out var varName2) && TryGetConstantValue(left, out _))
            {
                return ImmutableArray.Create((varName2, ConstantLatticeValue.Top));
            }
        }

        return ImmutableArray<(string, ConstantLatticeValue)>.Empty;
    }

    private static bool TryGetVariableName(IOperation operation, out string name)
    {
        operation = UnwrapAllConversions(operation);

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

    private static bool TryGetConstantValue(IOperation operation, out object value)
    {
        operation = UnwrapAllConversions(operation);

        if (operation is ILiteralOperation literal && literal.ConstantValue.HasValue)
        {
            value = literal.ConstantValue.Value!;
            return true;
        }

        value = null!;
        return false;
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

    private static IOperation UnwrapAllConversions(IOperation operation)
    {
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

    private static bool IsIdentityConversion(IConversionOperation conv)
    {
        return SymbolEqualityComparer.Default.Equals(conv.Type, conv.Operand.Type);
    }
}
