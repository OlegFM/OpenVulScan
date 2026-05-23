namespace OpenVulScan;

/// <summary>
/// A lattice over <see cref="ConstantLatticeValue"/> for tracking constant values
/// of expressions (int, string, bool, enum literals).
/// </summary>
/// <remarks>
/// <para>
/// Partial order: <see cref="ConstantLatticeValue.Bottom"/> ⊑ <see cref="ConstantLatticeValue.Const"/> ⊑ <see cref="ConstantLatticeValue.Top"/>.
/// Two <see cref="ConstantLatticeValue.Const"/> elements with different values are incomparable;
/// joining them yields <see cref="ConstantLatticeValue.Top"/>.
/// </para>
/// <para>
/// <see cref="Bottom"/> = <see cref="ConstantLatticeValue.Bottom"/> (no information).
/// <see cref="Top"/> = <see cref="ConstantLatticeValue.Top"/> (conflicting or unknown).
/// </para>
/// </remarks>
public sealed class ConstantLattice : ILattice<ConstantLatticeValue>
{
    /// <inheritdoc />
    public ConstantLatticeValue Bottom => ConstantLatticeValue.Bottom;

    /// <inheritdoc />
    public ConstantLatticeValue Top => ConstantLatticeValue.Top;

    /// <inheritdoc />
    public ConstantLatticeValue Join(ConstantLatticeValue left, ConstantLatticeValue right)
    {
        if (left.Kind == LatticeElementKind.Bottom)
            return right;
        if (right.Kind == LatticeElementKind.Bottom)
            return left;
        if (left.Kind == LatticeElementKind.Top || right.Kind == LatticeElementKind.Top)
            return Top;
        if (left.Equals(right))
            return left;

        return Top;
    }

    /// <inheritdoc />
    public bool LessOrEqual(ConstantLatticeValue left, ConstantLatticeValue right)
    {
        if (left.Kind == LatticeElementKind.Bottom)
            return true;
        if (right.Kind == LatticeElementKind.Top)
            return true;
        if (left.Kind == LatticeElementKind.Top && right.Kind != LatticeElementKind.Top)
            return false;
        if (right.Kind == LatticeElementKind.Bottom && left.Kind != LatticeElementKind.Bottom)
            return false;

        return left.Equals(right);
    }
}
