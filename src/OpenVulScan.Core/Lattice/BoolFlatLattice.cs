namespace OpenVulScan;

/// <summary>
/// A flat lattice over <see cref="bool"/> with four elements:
/// <see cref="BoolLatticeValue.Bottom"/> &lt;
/// { <see cref="BoolLatticeValue.False"/>, <see cref="BoolLatticeValue.True"/> } &lt;
/// <see cref="BoolLatticeValue.Top"/>.
/// </summary>
public sealed class BoolFlatLattice : ILattice<BoolLatticeValue>
{
    /// <inheritdoc />
    public BoolLatticeValue Bottom => BoolLatticeValue.Bottom;

    /// <inheritdoc />
    public BoolLatticeValue Top => BoolLatticeValue.Top;

    /// <inheritdoc />
    public BoolLatticeValue Join(BoolLatticeValue left, BoolLatticeValue right)
    {
        if (left == BoolLatticeValue.Bottom)
            return right;
        if (right == BoolLatticeValue.Bottom)
            return left;
        if (left == right)
            return left;

        return BoolLatticeValue.Top;
    }

    /// <inheritdoc />
    public bool LessOrEqual(BoolLatticeValue left, BoolLatticeValue right)
    {
        if (left == BoolLatticeValue.Bottom)
            return true;
        if (right == BoolLatticeValue.Top)
            return true;

        return left == right;
    }
}
