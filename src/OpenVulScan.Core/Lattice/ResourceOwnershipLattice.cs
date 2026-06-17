namespace OpenVulScan;

/// <summary>
/// A three-element chain lattice over <see cref="OwnershipState"/>:
/// <see cref="OwnershipState.Untracked"/> ⊏ <see cref="OwnershipState.Disposed"/> ⊏
/// <see cref="OwnershipState.Open"/>, with <see cref="Join"/> = chain maximum.
/// </summary>
/// <remarks>
/// Structurally identical to <see cref="BoolFlatLattice"/> / <see cref="DisposeLattice"/>
/// (bottom-identity, equal→same, else→top), because the only non-trivial unequal pair
/// (<see cref="OwnershipState.Disposed"/>, <see cref="OwnershipState.Open"/>) joins to ⊤.
/// The difference from <see cref="DisposeLattice"/> is <em>which</em> concrete is ⊤: here the
/// leak-dangerous "open" state, so partial dispose is not absorbed away at joins. Lifted per
/// resource via <c>MapLattice&lt;TrackedKey, ResourceOwnershipLattice, OwnershipState&gt;</c>.
/// </remarks>
public sealed class ResourceOwnershipLattice : ILattice<OwnershipState>
{
    /// <inheritdoc />
    public OwnershipState Bottom => OwnershipState.Untracked;

    /// <inheritdoc />
    public OwnershipState Top => OwnershipState.Open;

    /// <inheritdoc />
    public OwnershipState Join(OwnershipState left, OwnershipState right)
    {
        if (left == OwnershipState.Untracked)
            return right;
        if (right == OwnershipState.Untracked)
            return left;
        if (left == right)
            return left;

        return OwnershipState.Open;
    }

    /// <inheritdoc />
    public bool LessOrEqual(OwnershipState left, OwnershipState right)
    {
        if (left == OwnershipState.Untracked)
            return true;
        if (right == OwnershipState.Open)
            return true;

        return left == right;
    }
}
