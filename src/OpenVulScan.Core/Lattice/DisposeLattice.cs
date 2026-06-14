namespace OpenVulScan;

/// <summary>
/// A three-element chain lattice over <see cref="DisposeState"/> tracking the disposal
/// level of a single <see cref="System.IDisposable"/> object:
/// <see cref="DisposeState.Live"/> &lt; <see cref="DisposeState.Disposed"/> &lt;
/// <see cref="DisposeState.DoubleDisposed"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the diamond-shaped <see cref="InitializedLattice"/>, the dispose states are
/// totally ordered, so <see cref="Join"/> is the chain maximum and <see cref="Live"/>
/// doubles as ⊥. The implementation shape coincides with <see cref="BoolFlatLattice"/>
/// (identity, equal, else ⊤) only because the single non-trivial pair —
/// <see cref="DisposeState.Disposed"/> and <see cref="DisposeState.DoubleDisposed"/> —
/// joins to the top.
/// </para>
/// <para>
/// Per-object use: a consuming rule wraps this in a
/// <see cref="MapLattice{TKey, TLat, TVal}"/> keyed by the disposable symbol, advances a
/// symbol <see cref="DisposeState.Live"/> → <see cref="DisposeState.Disposed"/> on a
/// <c>Dispose()</c>/<c>using</c> exit, and a second disposal
/// <see cref="DisposeState.Disposed"/> → <see cref="DisposeState.DoubleDisposed"/> flags a
/// double-dispose. As with V3151, the join is a conservative upper bound; path-precise
/// double-dispose detection (avoiding a merge of a disposing and a non-disposing path) is
/// the consuming rule's responsibility.
/// </para>
/// </remarks>
public sealed class DisposeLattice : ILattice<DisposeState>
{
    /// <inheritdoc />
    public DisposeState Bottom => DisposeState.Live;

    /// <inheritdoc />
    public DisposeState Top => DisposeState.DoubleDisposed;

    /// <inheritdoc />
    public DisposeState Join(DisposeState left, DisposeState right)
    {
        if (left == DisposeState.Live)
            return right;
        if (right == DisposeState.Live)
            return left;
        if (left == right)
            return left;

        return DisposeState.DoubleDisposed;
    }

    /// <inheritdoc />
    public bool LessOrEqual(DisposeState left, DisposeState right)
    {
        if (left == DisposeState.Live)
            return true;
        if (right == DisposeState.DoubleDisposed)
            return true;

        return left == right;
    }
}
