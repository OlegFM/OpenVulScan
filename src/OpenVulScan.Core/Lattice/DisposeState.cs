namespace OpenVulScan;

/// <summary>
/// The three elements of the <see cref="DisposeLattice"/>, tracking how many times an
/// <see cref="System.IDisposable"/> object has been disposed along a control-flow path.
/// </summary>
/// <remarks>
/// <para>
/// These states form a <em>total order</em> (a chain) modelling a saturating dispose
/// counter {0, 1, ≥2}:
/// <see cref="Live"/> ⊑ <see cref="Disposed"/> ⊑ <see cref="DoubleDisposed"/>.
/// </para>
/// <para>
/// <see cref="Live"/> is both the least element (⊥) and the identity of
/// <see cref="DisposeLattice.Join"/>: a path on which the object is never disposed should
/// not raise the merged disposal level, so no separate bottom element is required (in
/// contrast to <see cref="InitializationState"/>, whose concretes are incomparable and
/// therefore need a distinct ⊥).
/// </para>
/// <para>
/// The enum values intentionally ascend with the chain order; do not reorder them.
/// </para>
/// </remarks>
public enum DisposeState
{
    /// <summary>
    /// The least element (⊥): the object has not been disposed (dispose count 0).
    /// Also the identity for the join.
    /// </summary>
    Live = 0,

    /// <summary>
    /// The object has been disposed exactly once (dispose count 1).
    /// </summary>
    Disposed = 1,

    /// <summary>
    /// The greatest element (⊤): the object has been disposed more than once
    /// (dispose count ≥ 2) — a double-dispose bug.
    /// </summary>
    DoubleDisposed = 2,
}
