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
/// Keys state by <see cref="SsaId"/> instead of variable name, enabling
/// precise value tracking across def-use chains and phi functions at join points.
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
            IFlowCaptureOperation capture => Evaluate(capture.Value, state),
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

        state = ApplyPhis(state, block);

        foreach (var op in OperationTree.Enumerate(block))
            state = Apply(state, op);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, NullState> ApplyPhis(
        ImmutableDictionary<SsaId, NullState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Join predecessor states into each φ-result on block entry.
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
            // default(T) where T is a reference type is null.  The Roslyn CFG emits
            // IDefaultValueOperation for the null arm of a ?. operator.
            IDefaultValueOperation dv when dv.Type is { IsReferenceType: true } =>
                NullState.DefinitelyNull,
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
            // The value of an assignment expression `(x = v)` is v; propagate it so
            // `(x = null) ?? …` and `(s = expr) != null` see the flowing null-state.
            ISimpleAssignmentOperation assign => Evaluate(assign.Value, state),
            IFlowCaptureReferenceOperation cref =>
                Lookup(cref, new TrackedKey.Capture(cref.Id), state),
            _ => NullState.Unknown,
        };
    }

    private NullState Lookup(IOperation op, TrackedKey key, ImmutableDictionary<SsaId, NullState> state)
    {
        var use = _ssa.UseAt(op, key);
        if (use is null) return NullState.Unknown;
        return state.TryGetValue(use.Value, out var s) ? s : NullState.Unknown;
    }
}
