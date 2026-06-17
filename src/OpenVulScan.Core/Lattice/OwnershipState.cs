namespace OpenVulScan;

/// <summary>
/// The three elements of <see cref="ResourceOwnershipLattice"/>, tracking whether an owned
/// <see cref="System.IDisposable"/> is still holding its resource along a control-flow path.
/// </summary>
/// <remarks>
/// <para>
/// A chain <see cref="Untracked"/> ⊑ <see cref="Disposed"/> ⊑ <see cref="Open"/>. Unlike
/// <see cref="DisposeState"/>, the <em>dangerous</em> state (<see cref="Open"/> — created but not
/// disposed) is the <em>top</em>, so it is absorbing under join and survives a merge with a
/// disposing path. That is what lets a may-analysis flag a <em>partial</em> dispose
/// (disposed on some but not all paths), not only a total leak.
/// </para>
/// <para>
/// <see cref="Untracked"/> is ⊥ and the join identity: it also models "this resource was not
/// created on this path", so a resource declared inside one branch contributes nothing to the
/// other branch (no false positive). Do not reorder the enum values; they ascend with the chain.
/// </para>
/// </remarks>
public enum OwnershipState
{
    /// <summary>The least element (⊥): not created on this path. Also the join identity.</summary>
    Untracked = 0,

    /// <summary>The resource was created and then disposed on this path.</summary>
    Disposed = 1,

    /// <summary>The greatest element (⊤): created and not (yet) disposed — a potential leak.</summary>
    Open = 2,
}
