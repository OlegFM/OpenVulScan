namespace OpenVulScan;

/// <summary>
/// The four elements of a flat lattice over <see cref="bool"/>.
/// </summary>
public enum BoolLatticeValue
{
    /// <summary>
    /// The least element (⊥), representing "no information".
    /// </summary>
    Bottom,

    /// <summary>
    /// The concrete boolean value <see langword="false"/>.
    /// </summary>
    False,

    /// <summary>
    /// The concrete boolean value <see langword="true"/>.
    /// </summary>
    True,

    /// <summary>
    /// The greatest element (⊤), representing "conflicting / any value".
    /// </summary>
    Top,
}
