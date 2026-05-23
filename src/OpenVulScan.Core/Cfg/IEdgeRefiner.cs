using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

/// <summary>
/// Defines an edge refinement function that adjusts the lattice state
/// when flowing across a specific control-flow edge.
/// </summary>
/// <typeparam name="T">The lattice element type.</typeparam>
/// <remarks>
/// <para>
/// Edge refiners enable path-sensitive data-flow analysis. Before joining
/// the out-states of all predecessors of a block, the solver calls
/// <see cref="Refine(T, ControlFlowBranch)"/> for each incoming edge.
/// </para>
/// </remarks>
public interface IEdgeRefiner<T>
{
    /// <summary>
    /// Refines the lattice state for a specific control-flow edge.
    /// </summary>
    /// <param name="state">The out-state of the edge's source block.</param>
    /// <param name="branch">The control-flow edge.</param>
    /// <returns>The refined state for the edge's destination block.</returns>
    T Refine(T state, ControlFlowBranch branch);
}
