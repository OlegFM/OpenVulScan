using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Edge refiner for null-state analysis. Given a control-flow edge whose
/// source block ends with a conditional branch, it extracts null-state
/// refinements from the branch condition and applies them to the state map.
/// </summary>
public sealed class NullStateEdgeRefiner : IEdgeRefiner<ImmutableDictionary<string, NullState>>
{
    /// <inheritdoc />
    public ImmutableDictionary<string, NullState> Refine(
        ImmutableDictionary<string, NullState> state,
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
        foreach (var (name, refinedState) in refinements)
        {
            if (builder.TryGetValue(name, out var currentState))
            {
                builder[name] = refinedState switch
                {
                    NullState.DefinitelyNull => NullStateTransfer.RefineForNullCheck(currentState),
                    NullState.NotNull => NullStateTransfer.RefineForNotNullCheck(currentState),
                    _ => currentState,
                };
            }
            else
            {
                builder[name] = refinedState;
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<(string Name, NullState State)> CollectRefinements(
        IOperation condition,
        bool whenTrue)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            case IBinaryOperation binary:
                return CollectBinaryRefinements(binary, whenTrue);

            case IIsPatternOperation isPattern:
                return CollectIsPatternRefinements(isPattern, whenTrue);

            case IUnaryOperation unary when unary.OperatorKind == UnaryOperatorKind.Not:
                // !cond: invert the branch sense
                return CollectRefinements(unary.Operand, !whenTrue);

            default:
                return ImmutableArray<(string, NullState)>.Empty;
        }
    }

    private static ImmutableArray<(string Name, NullState State)> CollectBinaryRefinements(
        IBinaryOperation binary,
        bool whenTrue)
    {
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals:
                return CollectNullComparisonRefinements(binary, whenTrue, equalsNullMeans: NullState.DefinitelyNull, notEqualsNullMeans: NullState.NotNull);

            case BinaryOperatorKind.NotEquals:
                return CollectNullComparisonRefinements(binary, whenTrue, equalsNullMeans: NullState.NotNull, notEqualsNullMeans: NullState.DefinitelyNull);

            case BinaryOperatorKind.ConditionalAnd:
                if (whenTrue)
                {
                    // a && b is true: both a and b are true
                    var left = CollectRefinements(binary.LeftOperand, whenTrue: true);
                    var right = CollectRefinements(binary.RightOperand, whenTrue: true);
                    return left.AddRange(right);
                }

                // a && b is false: at least one is false, can't refine
                return ImmutableArray<(string, NullState)>.Empty;

            case BinaryOperatorKind.ConditionalOr:
                if (!whenTrue)
                {
                    // a || b is false: both a and b are false
                    var left = CollectRefinements(binary.LeftOperand, whenTrue: false);
                    var right = CollectRefinements(binary.RightOperand, whenTrue: false);
                    return left.AddRange(right);
                }

                // a || b is true: at least one is true, can't refine
                return ImmutableArray<(string, NullState)>.Empty;

            default:
                return ImmutableArray<(string, NullState)>.Empty;
        }
    }

    private static ImmutableArray<(string Name, NullState State)> CollectNullComparisonRefinements(
        IBinaryOperation binary,
        bool whenTrue,
        NullState equalsNullMeans,
        NullState notEqualsNullMeans)
    {
        var left = Unwrap(binary.LeftOperand);
        var right = Unwrap(binary.RightOperand);

        if (IsNullLiteral(left) && TryGetVariableName(right, out var varRight))
        {
            return ImmutableArray.Create((varRight, whenTrue ? equalsNullMeans : notEqualsNullMeans));
        }

        if (IsNullLiteral(right) && TryGetVariableName(left, out var varLeft))
        {
            return ImmutableArray.Create((varLeft, whenTrue ? equalsNullMeans : notEqualsNullMeans));
        }

        return ImmutableArray<(string, NullState)>.Empty;
    }

    private static ImmutableArray<(string Name, NullState State)> CollectIsPatternRefinements(
        IIsPatternOperation isPattern,
        bool whenTrue)
    {
        var value = Unwrap(isPattern.Value);

        // x is null
        if (isPattern.Pattern is IConstantPatternOperation constPattern
            && constPattern.ConstantValue.Value is null
            && TryGetVariableName(value, out var name))
        {
            return ImmutableArray.Create((name, whenTrue ? NullState.DefinitelyNull : NullState.NotNull));
        }

        // x is not null
        if (isPattern.Pattern is INegatedPatternOperation negated
            && negated.Pattern is IConstantPatternOperation negConst
            && negConst.ConstantValue.Value is null
            && TryGetVariableName(value, out var negName))
        {
            return ImmutableArray.Create((negName, whenTrue ? NullState.NotNull : NullState.DefinitelyNull));
        }

        return ImmutableArray<(string, NullState)>.Empty;
    }

    private static bool IsNullLiteral(IOperation operation)
    {
        var unwrapped = UnwrapAllConversions(operation);
        return unwrapped is ILiteralOperation literal
            && literal.ConstantValue.HasValue
            && literal.ConstantValue.Value is null;
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
