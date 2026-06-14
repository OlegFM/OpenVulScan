namespace OpenVulScan;

/// <summary>
/// A flat lattice over <see cref="InitializationState"/> tracking definite assignment of a
/// single local variable or field:
/// <see cref="InitializationState.Bottom"/> &lt;
/// { <see cref="InitializationState.Uninit"/>, <see cref="InitializationState.Init"/> } &lt;
/// <see cref="InitializationState.MaybeInit"/>.
/// </summary>
/// <remarks>
/// <para>
/// Structurally identical to <see cref="BoolFlatLattice"/>: a least element (⊥), two
/// incomparable concretes, and a greatest element (⊤). The ⊤ carries the domain meaning
/// "may be uninitialised" (<see cref="InitializationState.MaybeInit"/>), which is exactly
/// the join of <see cref="InitializationState.Uninit"/> and <see cref="InitializationState.Init"/>
/// arriving from disagreeing control-flow paths.
/// </para>
/// <para>
/// Per-variable use: a consuming rule wraps this in a
/// <see cref="MapLattice{TKey, TLat, TVal}"/> keyed by symbol, seeds locals to
/// <see cref="InitializationState.Uninit"/> and parameters to
/// <see cref="InitializationState.Init"/> at method entry, sets a symbol to
/// <see cref="InitializationState.Init"/> on assignment, and flags a read whose state is
/// <see cref="InitializationState.Uninit"/> or <see cref="InitializationState.MaybeInit"/>.
/// </para>
/// </remarks>
public sealed class InitializedLattice : ILattice<InitializationState>
{
    /// <inheritdoc />
    public InitializationState Bottom => InitializationState.Bottom;

    /// <inheritdoc />
    public InitializationState Top => InitializationState.MaybeInit;

    /// <inheritdoc />
    public InitializationState Join(InitializationState left, InitializationState right)
    {
        if (left == InitializationState.Bottom)
            return right;
        if (right == InitializationState.Bottom)
            return left;
        if (left == right)
            return left;

        return InitializationState.MaybeInit;
    }

    /// <inheritdoc />
    public bool LessOrEqual(InitializationState left, InitializationState right)
    {
        if (left == InitializationState.Bottom)
            return true;
        if (right == InitializationState.MaybeInit)
            return true;

        return left == right;
    }
}
