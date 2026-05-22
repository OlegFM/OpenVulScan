using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer functions for <see cref="NullState"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class implements <see cref="ITransfer{NullState}"/> for expression-level
/// evaluation: given the null-state(s) of the inputs to an operation, it returns
/// the null-state of the result.
/// </para>
/// <para>
/// Because <see cref="NullState"/> is a single value, binary operations such as
/// <c>??</c> receive the <see cref="NullStateLattice.Join(NullState, NullState)"/>
/// of their operands as the incoming <paramref name="state"/>. The transfer
/// function then applies operation-specific refinement where possible.
/// </para>
/// <para>
/// Branch refinement (e.g. after <c>x is null</c>) is exposed through the
/// <see cref="RefineForNullCheck"/> and <see cref="RefineForNotNullCheck"/>
/// helpers, which are <em>not</em> part of <see cref="ITransfer{T}"/> because
/// the interface models expression evaluation, not edge conditions.
/// </para>
/// </remarks>
public sealed class NullStateTransfer : ITransfer<NullState>
{
    /// <inheritdoc />
    public NullState Apply(NullState state, IOperation operation)
    {
        return operation switch
        {
            // NOTE: ITransfer<NullState> receives a single aggregated state.
            // For precise assignment/coalesce handling the solver must pass
            // operand states separately. Returning the incoming state is the
            // conservative sound approximation (Phase 2 limitation).
            ISimpleAssignmentOperation => state,
            ICompoundAssignmentOperation => state,
            IConditionalAccessOperation => ApplyConditionalAccess(state),
            IMemberReferenceOperation memberRef => ApplyMemberReference(state, memberRef),
            ICoalesceOperation => state,
            _ => state,
        };
    }

    /// <inheritdoc />
    public NullState Apply(NullState state, BasicBlock block)
    {
        return state;
    }

    /// <summary>
    /// Refines a variable's <see cref="NullState"/> after a successful
    /// <c>x is null</c> or <c>x == null</c> check.
    /// </summary>
    /// <param name="state">The state before the check.</param>
    /// <returns>
    /// The state in the <em>true</em> branch.
    /// Returns <see cref="NullState.Unknown"/> (Bottom) when the branch is
    /// unreachable because the state already contradicts the condition
    /// (e.g. <see cref="NullState.NotNull"/> refined for null-check).
    /// </returns>
    public static NullState RefineForNullCheck(NullState state)
    {
        return state switch
        {
            NullState.NotNull => NullState.Unknown,
            _ => NullState.DefinitelyNull,
        };
    }

    /// <summary>
    /// Refines a variable's <see cref="NullState"/> after a failed
    /// <c>x is null</c> or <c>x != null</c> check.
    /// </summary>
    /// <param name="state">The state before the check.</param>
    /// <returns>
    /// The state in the <em>false</em> branch.
    /// Returns <see cref="NullState.Unknown"/> (Bottom) when the branch is
    /// unreachable because the state already contradicts the condition
    /// (e.g. <see cref="NullState.DefinitelyNull"/> refined for not-null-check).
    /// </returns>
    public static NullState RefineForNotNullCheck(NullState state)
    {
        return state switch
        {
            NullState.DefinitelyNull => NullState.Unknown,
            _ => NullState.NotNull,
        };
    }

    private static NullState ApplyConditionalAccess(NullState receiverState)
    {
        return receiverState switch
        {
            NullState.DefinitelyNull => NullState.DefinitelyNull,
            NullState.MaybeNull => NullState.MaybeNull,
            _ => NullState.Unknown,
        };
    }

    private static NullState ApplyMemberReference(NullState receiverState, IMemberReferenceOperation operation)
    {
        if (IsNullableValueProperty(operation))
        {
            return NullState.NotNull;
        }

        switch (receiverState)
        {
            case NullState.DefinitelyNull:
                return NullState.Unknown;
            case NullState.MaybeNull:
                return NullState.MaybeNull;
            case NullState.NotNull:
                return NullState.Unknown;
            default:
                return NullState.Unknown;
        }
    }

    private static bool IsNullableValueProperty(IMemberReferenceOperation operation)
    {
        return operation is IPropertyReferenceOperation propertyRef
            && propertyRef.Property.ContainingType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && propertyRef.Property.Name is "Value" or "HasValue";
    }
}
