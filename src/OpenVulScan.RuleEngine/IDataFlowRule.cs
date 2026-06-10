namespace OpenVulScan;

/// <summary>
/// Non-generic marker for <see cref="DataFlowRule{TLattice}"/> so the
/// scheduler can discover data-flow rules without knowing their closed
/// lattice state type.
/// </summary>
#pragma warning disable CA1040 // Empty interfaces are intentional here: the marker exists solely for scheduler discovery without requiring knowledge of the closed lattice type.
public interface IDataFlowRule
{
}
#pragma warning restore CA1040
