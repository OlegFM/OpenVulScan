namespace OpenVulScan;

/// <summary>
/// Defines a join-semilattice (or complete lattice) over type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The element type of the lattice.</typeparam>
/// <remarks>
/// <para>
/// A join-semilattice is a partially ordered set equipped with a binary operation <see cref="Join"/>
/// that satisfies the following axioms for all <c>a, b, c</c>:
/// </para>
/// <list type="bullet">
///   <item><description>Associativity: <c>Join(Join(a, b), c) == Join(a, Join(b, c))</c></description></item>
///   <item><description>Commutativity: <c>Join(a, b) == Join(b, a)</c></description></item>
///   <item><description>Idempotence: <c>Join(a, a) == a</c></description></item>
/// </list>
/// <para>
/// The partial order is given by <see cref="LessOrEqual"/>, which must satisfy:
/// <c>LessOrEqual(a, b) iff Join(a, b) == b</c>.
/// </para>
/// <para>
/// <see cref="Bottom"/> is the least element (<c>Join(Bottom, a) == a</c> for all <c>a</c>).
/// <see cref="Top"/> is the greatest element (<c>Join(a, Top) == Top</c> for all <c>a</c>).
/// </para>
/// </remarks>
public interface ILattice<T>
{
    /// <summary>
    /// Gets the least element of the lattice (identity for <see cref="Join"/>).
    /// </summary>
    T Bottom { get; }

    /// <summary>
    /// Gets the greatest element of the lattice (absorbing for <see cref="Join"/>).
    /// </summary>
    T Top { get; }

    /// <summary>
    /// Computes the least upper bound (join) of two lattice elements.
    /// </summary>
    /// <param name="left">The first element.</param>
    /// <param name="right">The second element.</param>
    /// <returns>The join of <paramref name="left"/> and <paramref name="right"/>.</returns>
    T Join(T left, T right);

    /// <summary>
    /// Determines whether <paramref name="left"/> is less than or equal to
    /// <paramref name="right"/> in the lattice partial order.
    /// </summary>
    /// <param name="left">The left-hand element.</param>
    /// <param name="right">The right-hand element.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> ≤ <paramref name="right"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    bool LessOrEqual(T left, T right);
}
