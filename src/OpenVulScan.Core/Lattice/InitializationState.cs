namespace OpenVulScan;

/// <summary>
/// The four elements of the <see cref="InitializedLattice"/>, tracking whether a
/// local variable or field has been assigned a value before it is read.
/// </summary>
/// <remarks>
/// <para>
/// Ordering (partial): <see cref="Bottom"/> ⊑ <see cref="Uninit"/> ⊑ <see cref="MaybeInit"/>
/// and <see cref="Bottom"/> ⊑ <see cref="Init"/> ⊑ <see cref="MaybeInit"/>.
/// </para>
/// <para>
/// The three <em>domain</em> states are <see cref="Uninit"/>, <see cref="Init"/> and
/// <see cref="MaybeInit"/>. <see cref="Bottom"/> is the structural least element required
/// by <see cref="ILattice{T}"/>: the worklist solver seeds every block with it, and it is
/// the identity for <see cref="InitializedLattice.Join"/>. It is deliberately distinct from
/// <see cref="Uninit"/> — conflating "no information yet" with "definitely uninitialised on
/// this path" would make merges unsound (a path that genuinely leaves a variable unwritten
/// would be indistinguishable from a block that has not been analysed).
/// </para>
/// <para>
/// <see cref="Uninit"/> and <see cref="Init"/> are incomparable concretes. Joining them
/// yields <see cref="MaybeInit"/> (the value may be uninitialised on some path), which is
/// the greatest element (⊤).
/// </para>
/// </remarks>
public enum InitializationState
{
    /// <summary>
    /// The least element (⊥): no information is known yet. Used to seed blocks before the
    /// data-flow fixpoint reaches them; never a final program fact.
    /// </summary>
    Bottom,

    /// <summary>
    /// The variable is definitely not initialised on this path.
    /// </summary>
    Uninit,

    /// <summary>
    /// The variable is definitely initialised on this path.
    /// </summary>
    Init,

    /// <summary>
    /// The greatest element (⊤): control-flow paths disagree, so the variable may be
    /// uninitialised. A read in this state is a potential use-before-initialisation.
    /// </summary>
    MaybeInit,
}
