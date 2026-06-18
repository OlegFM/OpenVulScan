namespace OpenVulScan;

/// <summary>
/// An <see cref="ILattice{T}"/> of potentially infinite height that supplies a
/// <em>widening</em> operator so a monotone fixpoint iteration terminates.
/// </summary>
/// <typeparam name="T">The element type of the lattice.</typeparam>
/// <remarks>
/// <para>
/// Finite-height lattices (e.g. <see cref="NullStateLattice"/>, <see cref="DisposeLattice"/>)
/// converge under <see cref="ILattice{T}.Join"/> alone because their ascending chains are
/// bounded. Domains such as <see cref="IntervalLattice"/> admit infinite strictly-ascending
/// chains (<c>[0,0] ⊏ [0,1] ⊏ [0,2] ⊏ …</c>); a solver that only joins would never reach a
/// fixpoint on a counting loop.
/// </para>
/// <para>
/// <see cref="Widen"/> accelerates convergence: applied at a loop-header back-edge, any
/// component that moved outward between <paramref name="previous"/> and <paramref name="incoming"/>
/// is jumped to the corresponding extreme, guaranteeing each component changes only finitely
/// often. Widening is deliberately imprecise (it over-approximates) and is, in general,
/// <em>neither commutative nor associative</em> — it is a directed operator, not a join.
/// </para>
/// </remarks>
public interface IWideningLattice<T> : ILattice<T>
{
    /// <summary>
    /// Widens <paramref name="previous"/> towards <paramref name="incoming"/>, returning an
    /// element ⊒ <paramref name="incoming"/> chosen so that repeated application stabilises in
    /// a finite number of steps.
    /// </summary>
    /// <param name="previous">The previous iterate (the accumulated value).</param>
    /// <param name="incoming">The newly produced value (expected ⊒ <paramref name="previous"/>).</param>
    /// <returns>The widened element.</returns>
    T Widen(T previous, T incoming);
}
