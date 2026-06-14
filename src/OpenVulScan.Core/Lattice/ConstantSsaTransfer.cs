using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer functions for <see cref="ConstantLatticeValue"/> tracked per SSA version
/// in an <see cref="ImmutableDictionary{SsaId,ConstantLatticeValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Keys state by <see cref="SsaId"/> instead of variable name, enabling
/// precise constant propagation across def-use chains and phi functions at join points.
/// </para>
/// </remarks>
public sealed class ConstantSsaTransfer : ITransfer<ImmutableDictionary<SsaId, ConstantLatticeValue>>
{
    private static readonly ConstantLattice _lattice = new();
    private readonly SsaIndex _ssa;

    public ConstantSsaTransfer(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, ConstantLatticeValue> Apply(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        var def = _ssa.DefinitionAt(operation);
        if (def is null) return state;

        var value = operation switch
        {
            IVariableDeclaratorOperation { Initializer: { } init } => Evaluate(init.Value, state),
            ISimpleAssignmentOperation assignment => Evaluate(assignment.Value, state),
            IFlowCaptureOperation capture => Evaluate(capture.Value, state),
            _ => ConstantLatticeValue.Top,
        };
        return state.SetItem(def.Value, value);
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, ConstantLatticeValue> Apply(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        state = ApplyPhis(state, block);

        foreach (var op in OperationTree.Enumerate(block))
            state = Apply(state, op);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, ConstantLatticeValue> ApplyPhis(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Join predecessor states into each φ-result on block entry.
        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = ConstantLatticeValue.Bottom;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                    joined = _lattice.Join(joined, s);
            }
            state = state.SetItem(phi.Result, joined);
        }

        return state;
    }

    private ConstantLatticeValue Evaluate(IOperation expr, ImmutableDictionary<SsaId, ConstantLatticeValue> state)
    {
        return expr switch
        {
            ILiteralOperation lit when lit.ConstantValue.HasValue && lit.ConstantValue.Value is not null =>
                ConstantLatticeValue.Const(lit.ConstantValue.Value),
            ILocalReferenceOperation lref =>
                Lookup(lref, new TrackedKey.Symbol(lref.Local), state),
            IParameterReferenceOperation pref =>
                Lookup(pref, new TrackedKey.Symbol(pref.Parameter), state),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } fref =>
                Lookup(fref, new TrackedKey.InstanceField(fref.Field), state),
            IConversionOperation conv => Evaluate(conv.Operand, state),
            IParenthesizedOperation paren => Evaluate(paren.Operand, state),
            // The value of an assignment expression `(x = v)` is v; propagate it so
            // `int z = (x = 42)` and `(x = v) ?? …` bind the flowing constant.
            ISimpleAssignmentOperation assign => Evaluate(assign.Value, state),
            IFlowCaptureReferenceOperation cref =>
                Lookup(cref, new TrackedKey.Capture(cref.Id), state),
            _ => ConstantLatticeValue.Top,
        };
    }

    private ConstantLatticeValue Lookup(IOperation op, TrackedKey key, ImmutableDictionary<SsaId, ConstantLatticeValue> state)
    {
        var use = _ssa.UseAt(op, key);
        if (use is null) return ConstantLatticeValue.Top;
        return state.TryGetValue(use.Value, out var s) ? s : ConstantLatticeValue.Top;
    }
}
