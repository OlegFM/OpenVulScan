using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer functions for <see cref="NullState"/> tracked per variable
/// in an <see cref="ImmutableDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This transfer updates the map when locals or parameters are assigned
/// literal <see langword="null"/> or non-null values. It uses
/// <see cref="NullStateTransfer"/> to evaluate the null-state of expressions.
/// </para>
/// </remarks>
public sealed class NullStateMapTransfer : ITransfer<ImmutableDictionary<string, NullState>>
{
    /// <inheritdoc />
    public ImmutableDictionary<string, NullState> Apply(ImmutableDictionary<string, NullState> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        switch (operation)
        {
            case ISimpleAssignmentOperation assignment when assignment.Target is ILocalReferenceOperation localRef:
                var valueState = EvaluateExpression(assignment.Value, state);
                return state.SetItem(localRef.Local.Name, valueState);

            case ISimpleAssignmentOperation assignment when assignment.Target is IParameterReferenceOperation paramRef:
                var paramValueState = EvaluateExpression(assignment.Value, state);
                return state.SetItem(paramRef.Parameter.Name, paramValueState);

            case IVariableDeclaratorOperation varDecl when varDecl.Symbol is ILocalSymbol local:
                if (varDecl.Initializer is not null)
                {
                    var initState = EvaluateExpression(varDecl.Initializer.Value, state);
                    return state.SetItem(local.Name, initState);
                }

                return state.SetItem(local.Name, NullState.Unknown);
        }

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<string, NullState> Apply(ImmutableDictionary<string, NullState> state, BasicBlock block)
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

    private static NullState EvaluateExpression(IOperation? operation, ImmutableDictionary<string, NullState> state)
    {
        if (operation is null)
            return NullState.Unknown;

        return operation switch
        {
            ILiteralOperation literal => literal.ConstantValue.Value is null
                ? NullState.DefinitelyNull
                : NullState.NotNull,

            ILocalReferenceOperation localRef => state.TryGetValue(localRef.Local.Name, out var localState)
                ? localState
                : NullState.Unknown,

            IParameterReferenceOperation paramRef => state.TryGetValue(paramRef.Parameter.Name, out var paramState)
                ? paramState
                : NullState.Unknown,

            IObjectCreationOperation => NullState.NotNull,

            IArrayCreationOperation => NullState.NotNull,

            IConversionOperation conv => EvaluateExpression(conv.Operand, state),

            IParenthesizedOperation paren => EvaluateExpression(paren.Operand, state),

            _ => NullState.Unknown,
        };
    }
}
