namespace OpenVulScan;

/// <summary>
/// A lattice over <see cref="NullState"/>.
/// </summary>
/// <remarks>
/// <para>
/// Partial order: <c>Unknown ⊑ DefinitelyNull ⊑ MaybeNull</c> and
/// <c>Unknown ⊑ NotNull ⊑ MaybeNull</c>.
/// </para>
/// <para>
/// <see cref="Bottom"/> = <see cref="NullState.Unknown"/> (no information).
/// <see cref="Top"/> = <see cref="NullState.MaybeNull"/> (most conservative).
/// </para>
/// <para>
/// Edge cases:
/// <list type="bullet">
///   <item><description>
///     Nullable value types (<c>int?</c>): wrapped in a nullable struct.
///     The <see cref="NullState"/> tracks the <em>logical</em> nullability
///     of the underlying value; the wrapper itself is a value type and
///     never null in the reference-type sense.
///   </description></item>
///   <item><description>
///     Generics with <c>class?</c> / <c>class</c> / <c>struct</c>
///     constraints: the initial state follows the annotation.
///     Unconstrained generics start at <see cref="NullState.Unknown"/>
///     and are refined by null checks.
///   </description></item>
///   <item><description>
///     Oblivious reference types (pre-nullable context) are treated as
///     <see cref="NullState.MaybeNull"/> to stay sound.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class NullStateLattice : ILattice<NullState>
{
    /// <inheritdoc />
    public NullState Bottom => NullState.Unknown;

    /// <inheritdoc />
    public NullState Top => NullState.MaybeNull;

    /// <inheritdoc />
    public NullState Join(NullState left, NullState right)
    {
        if (left == NullState.Unknown)
            return right;
        if (right == NullState.Unknown)
            return left;
        if (left == right)
            return left;

        return NullState.MaybeNull;
    }

    /// <inheritdoc />
    public bool LessOrEqual(NullState left, NullState right)
    {
        if (left == NullState.Unknown)
            return true;
        if (right == NullState.MaybeNull)
            return true;

        return left == right;
    }
}
