using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

/// <summary>
/// Transfer for the leak rules (V3114, V3073) over
/// <c>ImmutableDictionary&lt;TrackedKey, OwnershipState&gt;</c>. A tracked resource becomes
/// <see cref="OwnershipState.Open"/> at its <c>new</c> creation site and
/// <see cref="OwnershipState.Disposed"/> at an explicit <c>Dispose()</c> call. Untracked keys
/// (absent) stay ⊥, so a branch that never creates the resource contributes nothing.
/// </summary>
public sealed class ResourceOwnershipTransfer : ITransfer<ImmutableDictionary<TrackedKey, OwnershipState>>
{
    private readonly IReadOnlySet<TrackedKey> _tracked;
    private readonly Compilation _compilation;

    public ResourceOwnershipTransfer(IReadOnlySet<TrackedKey> tracked, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        ArgumentNullException.ThrowIfNull(compilation);
        _tracked = tracked;
        _compilation = compilation;
    }

    /// <inheritdoc />
    public ImmutableDictionary<TrackedKey, OwnershipState> Apply(
        ImmutableDictionary<TrackedKey, OwnershipState> state, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        if (DisposeFlow.TryGetCreatedResource(operation, _compilation) is { } created
            && _tracked.Contains(created.Key))
            return state.SetItem(created.Key, OwnershipState.Open);

        if (DisposeFlow.TryGetDisposedResource(operation) is { } disposed
            && _tracked.Contains(disposed))
            return state.SetItem(disposed, OwnershipState.Disposed);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<TrackedKey, OwnershipState> Apply(
        ImmutableDictionary<TrackedKey, OwnershipState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        foreach (var op in OperationTree.Enumerate(block))
            state = Apply(state, op);

        return state;
    }
}
