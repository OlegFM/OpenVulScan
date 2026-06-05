using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer functions for <see cref="NullState"/> tracked per SSA version
/// in an <see cref="ImmutableDictionary{SsaId,NullState}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the SSA-aware counterpart of <see cref="NullStateMapTransfer"/>.
/// It keys state by <see cref="SsaId"/> instead of variable name, enabling
/// precise value tracking across def-use chains and phi functions at join points.
/// </para>
/// <para>
/// The legacy <see cref="NullStateMapTransfer"/> (string-keyed) remains in place
/// until Task 19 removes it. This class is additive only.
/// </para>
/// </remarks>
public sealed class NullStateSsaTransfer : ITransfer<ImmutableDictionary<SsaId, NullState>>
{
    private static readonly NullStateLattice _lattice = new();
    private readonly SsaIndex _ssa;

    public NullStateSsaTransfer(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, NullState> Apply(
        ImmutableDictionary<SsaId, NullState> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        var def = _ssa.DefinitionAt(operation);
        if (def is null) return state;

        var valueState = operation switch
        {
            IVariableDeclaratorOperation { Initializer: { } init } => Evaluate(init.Value, state),
            ISimpleAssignmentOperation assignment => Evaluate(assignment.Value, state),
            ICompoundAssignmentOperation => NullState.Unknown,
            _ => NullState.Unknown,
        };
        return state.SetItem(def.Value, valueState);
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, NullState> Apply(
        ImmutableDictionary<SsaId, NullState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Apply phi functions on block entry first: join predecessor states.
        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = NullState.Unknown;
            var any = false;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                {
                    joined = any ? _lattice.Join(joined, s) : s;
                    any = true;
                }
            }
            state = state.SetItem(phi.Result, any ? joined : NullState.Unknown);
        }

        foreach (var op in block.Operations.SelectMany(EnumerateOps))
            state = Apply(state, op);

        if (block.BranchValue is not null)
            foreach (var op in EnumerateOps(block.BranchValue))
                state = Apply(state, op);

        return state;
    }

    private NullState Evaluate(IOperation expr, ImmutableDictionary<SsaId, NullState> state)
    {
        return expr switch
        {
            ILiteralOperation lit when lit.ConstantValue.HasValue && lit.ConstantValue.Value is null =>
                NullState.DefinitelyNull,
            ILiteralOperation lit when lit.ConstantValue.HasValue =>
                NullState.NotNull,
            ILocalReferenceOperation lref =>
                Lookup(lref, new TrackedKey.Symbol(lref.Local), state),
            IParameterReferenceOperation pref =>
                Lookup(pref, new TrackedKey.Symbol(pref.Parameter), state),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } fref =>
                Lookup(fref, new TrackedKey.InstanceField(fref.Field), state),
            IObjectCreationOperation => NullState.NotNull,
            IArrayCreationOperation => NullState.NotNull,
            IConversionOperation conv => Evaluate(conv.Operand, state),
            IParenthesizedOperation paren => Evaluate(paren.Operand, state),
            _ => NullState.Unknown,
        };
    }

    private NullState Lookup(IOperation op, TrackedKey key, ImmutableDictionary<SsaId, NullState> state)
    {
        var use = _ssa.UseAt(op, key);
        if (use is null) return NullState.Unknown;
        return state.TryGetValue(use.Value, out var s) ? s : NullState.Unknown;
    }

    private static IEnumerable<IOperation> EnumerateOps(IOperation op)
    {
        yield return op;
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            foreach (var d in EnumerateOps(child)) yield return d;
        }
    }
}
