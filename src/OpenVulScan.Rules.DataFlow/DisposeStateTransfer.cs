using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Transfer for V3178 over <c>ImmutableDictionary&lt;TrackedKey, DisposeState&gt;</c>: an explicit
/// <c>Dispose()</c> advances a resource <see cref="DisposeState.Live"/> → <see cref="DisposeState.Disposed"/>
/// → <see cref="DisposeState.DoubleDisposed"/> using the chain order of <see cref="DisposeLattice"/>.
/// A declaration or reassignment binds the variable to a different object, so it resets the
/// state to <see cref="DisposeState.Live"/> — without this, a resource created inside a loop
/// would inherit the previous iteration's Disposed state over the back edge.
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

        switch (operation)
        {
            case IVariableDeclaratorOperation { Symbol: ILocalSymbol local, Initializer: not null }:
                return state.SetItem(new TrackedKey.Symbol(local), DisposeState.Live);

            case ISimpleAssignmentOperation { Target: { } target }
                when DisposeFlow.ResolveResourceKey(target) is { } reassigned:
                return state.SetItem(reassigned, DisposeState.Live);
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
