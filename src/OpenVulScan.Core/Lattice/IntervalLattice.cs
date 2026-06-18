namespace OpenVulScan;

/// <summary>
/// The integer interval domain over <see cref="IntervalValue"/>: an infinite-height lattice
/// whose elements are convex ranges <c>[lo, hi]</c> ordered by inclusion, equipped with a
/// <see cref="Widen"/> operator for terminating fixpoint iteration over loops.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Bottom"/> = ∅ (empty interval, no information); <see cref="Top"/> = <c>[−∞,+∞]</c>.
/// <see cref="Join"/> is the convex hull (smallest enclosing interval), <em>not</em> set union —
/// the domain only represents contiguous ranges. The dual greatest-lower-bound (intersection) is
/// <see cref="IntervalValue.Intersect"/> on the value. <see cref="LessOrEqual"/> is interval
/// inclusion ⊆.
/// </para>
/// <para>
/// Unlike every other lattice in the project (height ≤ 3), interval chains can ascend forever
/// (<c>[0,0] ⊏ [0,1] ⊏ …</c>), so <see cref="IWideningLattice{T}"/> is implemented:
/// <see cref="Widen"/> sends an outward-moving bound straight to ±∞, capping the number of
/// changes per bound and forcing convergence. See ovs-2qi.4 / the design note for rationale.
/// </para>
/// </remarks>
public sealed class IntervalLattice : IWideningLattice<IntervalValue>
{
    /// <inheritdoc />
    public IntervalValue Bottom => IntervalValue.Empty;

    /// <inheritdoc />
    public IntervalValue Top => IntervalValue.Top;

    /// <inheritdoc />
    public IntervalValue Join(IntervalValue left, IntervalValue right)
    {
        if (left.IsEmpty)
            return right;
        if (right.IsEmpty)
            return left;

        return IntervalValue.Range(Math.Min(left.Lower, right.Lower), Math.Max(left.Upper, right.Upper));
    }

    /// <inheritdoc />
    public bool LessOrEqual(IntervalValue left, IntervalValue right)
    {
        if (left.IsEmpty)
            return true; // ∅ ⊑ everything
        if (right.IsEmpty)
            return false; // a non-empty interval is never ⊆ ∅

        return right.Lower <= left.Lower && left.Upper <= right.Upper; // left ⊆ right
    }

    /// <inheritdoc />
    public IntervalValue Widen(IntervalValue previous, IntervalValue incoming)
    {
        if (previous.IsEmpty)
            return incoming;
        if (incoming.IsEmpty)
            return previous;

        long lower = incoming.Lower < previous.Lower ? IntervalValue.NegativeInfinity : previous.Lower;
        long upper = incoming.Upper > previous.Upper ? IntervalValue.PositiveInfinity : previous.Upper;
        return IntervalValue.Range(lower, upper);
    }
}
