namespace OpenVulScan;

/// <summary>
/// The four elements of the null-state lattice.
/// </summary>
/// <remarks>
/// <para>
/// Ordering (partial): <see cref="Unknown"/> ⊑ <see cref="DefinitelyNull"/> ⊑ <see cref="MaybeNull"/>
/// and <see cref="Unknown"/> ⊑ <see cref="NotNull"/> ⊑ <see cref="MaybeNull"/>.
/// </para>
/// <para>
/// <see cref="Unknown"/> is the least element (⊥): no information is known yet.
/// <see cref="MaybeNull"/> is the greatest element (⊤): the value may be null
/// (either because it is definitely null, definitely not null, or we have
/// conflicting information from different control-flow paths).
/// </para>
/// <para>
/// <see cref="DefinitelyNull"/> and <see cref="NotNull"/> are incomparable.
/// Joining them yields <see cref="MaybeNull"/>.
/// </para>
/// </remarks>
public enum NullState
{
    /// <summary>
    /// No information is known (⊥).
    /// </summary>
    Unknown,

    /// <summary>
    /// The value is definitely <see langword="null"/>.
    /// </summary>
    DefinitelyNull,

    /// <summary>
    /// The value is definitely not <see langword="null"/>.
    /// </summary>
    NotNull,

    /// <summary>
    /// The value may be null (⊤). Used when different control-flow paths
    /// disagree, or when the state cannot be determined precisely.
    /// </summary>
    MaybeNull,
}
