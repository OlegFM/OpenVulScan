using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer functions for <see cref="IntervalValue"/> tracked per SSA version in an
/// <see cref="ImmutableDictionary{TKey,TValue}"/> keyed by <see cref="SsaId"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ConstantSsaTransfer"/>; expression evaluation is shared with rules
/// through <see cref="IntervalSsaEvaluator"/> (which also defines the array-length
/// convention for array-typed definitions).
/// </remarks>
public sealed class IntervalSsaTransfer : ITransfer<ImmutableDictionary<SsaId, IntervalValue>>
{
    private static readonly IntervalLattice _lattice = new();
    private readonly SsaIndex _ssa;

    public IntervalSsaTransfer(SsaIndex ssa)
    {
        ArgumentNullException.ThrowIfNull(ssa);
        _ssa = ssa;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> Apply(
        ImmutableDictionary<SsaId, IntervalValue> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        var def = _ssa.DefinitionAt(operation);
        if (def is null) return state;

        var value = operation switch
        {
            IVariableDeclaratorOperation { Initializer: { } init } =>
                IntervalSsaEvaluator.Evaluate(init.Value, state, _ssa),
            ISimpleAssignmentOperation assignment =>
                IntervalSsaEvaluator.Evaluate(assignment.Value, state, _ssa),
            IFlowCaptureOperation capture =>
                IntervalSsaEvaluator.Evaluate(capture.Value, state, _ssa),
            _ => IntervalValue.Top,
        };
        return state.SetItem(def.Value, value);
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> Apply(
        ImmutableDictionary<SsaId, IntervalValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        state = ApplyPhis(state, block);

        foreach (var op in OperationTree.Enumerate(block))
            state = Apply(state, op);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, IntervalValue> ApplyPhis(
        ImmutableDictionary<SsaId, IntervalValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Join predecessor states into each φ-result on block entry.
        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = IntervalValue.Empty;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                    joined = _lattice.Join(joined, s);
            }
            state = state.SetItem(phi.Result, joined);
        }

        return state;
    }
}
