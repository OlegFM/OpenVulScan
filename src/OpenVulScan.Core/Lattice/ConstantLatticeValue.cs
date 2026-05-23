namespace OpenVulScan;

/// <summary>
/// The elements of the constant-value lattice.
/// </summary>
/// <remarks>
/// <para>
/// Ordering: <see cref="Bottom"/> ⊑ <see cref="Const"/> ⊑ <see cref="Top"/>.
/// Two <see cref="Const"/> values with different underlying values are incomparable;
/// joining them yields <see cref="Top"/>.
/// </para>
/// </remarks>
public readonly struct ConstantLatticeValue : IEquatable<ConstantLatticeValue>
{
    /// <summary>
    /// No information is known (⊥).
    /// </summary>
    public static ConstantLatticeValue Bottom { get; } = new(LatticeElementKind.Bottom, null);

    /// <summary>
    /// The value is a known constant.
    /// </summary>
    public static ConstantLatticeValue Const(object value) => new(LatticeElementKind.Const, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>
    /// The value is unknown or conflicting (⊤).
    /// </summary>
    public static ConstantLatticeValue Top { get; } = new(LatticeElementKind.Top, null);

    private ConstantLatticeValue(LatticeElementKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>
    /// Gets the kind of lattice element.
    /// </summary>
    public LatticeElementKind Kind { get; }

    /// <summary>
    /// Gets the constant value, if <see cref="Kind"/> is <see cref="LatticeElementKind.Const"/>.
    /// </summary>
    public object? Value { get; }

    /// <inheritdoc />
    public bool Equals(ConstantLatticeValue other)
    {
        if (Kind != other.Kind)
            return false;

        if (Kind == LatticeElementKind.Const)
            return Equals(Value, other.Value);

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConstantLatticeValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Kind switch
        {
            LatticeElementKind.Bottom => 0,
            LatticeElementKind.Top => 1,
            LatticeElementKind.Const => HashCode.Combine(2, Value?.GetHashCode() ?? 0),
            _ => -1,
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Kind switch
        {
            LatticeElementKind.Bottom => "⊥",
            LatticeElementKind.Top => "⊤",
            LatticeElementKind.Const => $"Const({Value})",
            _ => $"Unknown({Kind})"
        };
    }

    /// <summary>
    /// Determines whether two <see cref="ConstantLatticeValue"/> instances are equal.
    /// </summary>
    public static bool operator ==(ConstantLatticeValue left, ConstantLatticeValue right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="ConstantLatticeValue"/> instances are not equal.
    /// </summary>
    public static bool operator !=(ConstantLatticeValue left, ConstantLatticeValue right) => !left.Equals(right);
}

/// <summary>
/// The kind of a <see cref="ConstantLatticeValue"/> element.
/// </summary>
public enum LatticeElementKind
{
    /// <summary>
    /// No information is known (⊥).
    /// </summary>
    Bottom,

    /// <summary>
    /// A known constant value.
    /// </summary>
    Const,

    /// <summary>
    /// Unknown or conflicting (⊤).
    /// </summary>
    Top,
}
