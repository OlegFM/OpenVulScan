using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

/// <summary>
/// Transfer for V3178 over <c>ImmutableDictionary&lt;TrackedKey, DisposeState&gt;</c>: an explicit
/// <c>Dispose()</c> advances a resource <see cref="DisposeState.Live"/> → <see cref="DisposeState.Disposed"/>
/// → <see cref="DisposeState.DoubleDisposed"/> using the chain order of <see cref="DisposeLattice"/>.
/// </summary>
public sealed class DisposeStateTransfer : ITransfer<ImmutableDictionary<TrackedKey, DisposeState>>
{
    /// <inheritdoc />
    public ImmutableDictionary<TrackedKey, DisposeState> Apply(
        ImmutableDictionary<TrackedKey, DisposeState> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        if (DisposeFlow.TryGetDisposedResource(operation) is { } key)
        {
            var current = state.TryGetValue(key, out var s) ? s : DisposeState.Live;
            var next = current == DisposeState.Live ? DisposeState.Disposed : DisposeState.DoubleDisposed;
            return state.SetItem(key, next);
        }

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<TrackedKey, DisposeState> Apply(
        ImmutableDictionary<TrackedKey, DisposeState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        foreach (var op in OperationTree.Enumerate(block))
            state = Apply(state, op);

        return state;
    }
}
