using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed class SsaIndex
{
    private readonly ImmutableDictionary<IOperation, SsaId> _definitions;
    private readonly ImmutableDictionary<(IOperation Op, TrackedKey Key), SsaId> _uses;
    private readonly ImmutableDictionary<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>> _entryVersions;
    private readonly ImmutableDictionary<BasicBlock, ImmutableArray<Phi>> _phis;
    private readonly ImmutableDictionary<TrackedKey, ImmutableArray<SsaId>> _allVersions;

    internal SsaIndex(
        ImmutableDictionary<IOperation, SsaId> definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId> uses,
        ImmutableDictionary<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>> entryVersions,
        ImmutableDictionary<BasicBlock, ImmutableArray<Phi>> phis,
        ImmutableDictionary<TrackedKey, ImmutableArray<SsaId>> allVersions)
    {
        _definitions = definitions;
        _uses = uses;
        _entryVersions = entryVersions;
        _phis = phis;
        _allVersions = allVersions;
    }

    public static SsaIndex Empty { get; } = new(
        ImmutableDictionary<IOperation, SsaId>.Empty,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Empty,
        ImmutableDictionary<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>>.Empty,
        ImmutableDictionary<BasicBlock, ImmutableArray<Phi>>.Empty,
        ImmutableDictionary<TrackedKey, ImmutableArray<SsaId>>.Empty);

    public SsaId? DefinitionAt(IOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        return _definitions.TryGetValue(op, out var id) ? id : null;
    }

    public SsaId? UseAt(IOperation op, TrackedKey key)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(key);
        return _uses.TryGetValue((op, key), out var id) ? id : null;
    }

    public IReadOnlyDictionary<TrackedKey, SsaId> EntryVersions(BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return _entryVersions.TryGetValue(block, out var dict)
            ? (IReadOnlyDictionary<TrackedKey, SsaId>)dict
            : ImmutableDictionary<TrackedKey, SsaId>.Empty;
    }

    public IReadOnlyList<Phi> PhisAt(BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return _phis.TryGetValue(block, out var arr)
            ? (IReadOnlyList<Phi>)arr
            : ImmutableArray<Phi>.Empty;
    }

    public IReadOnlyList<SsaId> AllVersions(TrackedKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _allVersions.TryGetValue(key, out var arr)
            ? (IReadOnlyList<SsaId>)arr
            : ImmutableArray<SsaId>.Empty;
    }
}
