using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

/// <summary>
/// Defines a transfer function that maps a lattice state through control-flow
/// graph constructs.
/// </summary>
/// <typeparam name="T">The lattice element type.</typeparam>
/// <remarks>
/// Transfer functions are the building blocks of data-flow analysis:
/// given an abstract state before a construct, they produce the abstract
/// state after it.
/// </remarks>
public interface ITransfer<T>
{
    /// <summary>
    /// Applies the transfer function for <paramref name="operation"/>
    /// to the incoming lattice state.
    /// </summary>
    /// <param name="state">The lattice state before the operation.</param>
    /// <param name="operation">The Roslyn operation to model.</param>
    /// <returns>The lattice state after the operation.</returns>
    T Apply(T state, IOperation operation);

    /// <summary>
    /// Applies the transfer function for all operations in <paramref name="block"/>
    /// to the incoming lattice state, in sequential order.
    /// </summary>
    /// <param name="state">The lattice state before the basic block.</param>
    /// <param name="block">The control-flow basic block to model.</param>
    /// <returns>The lattice state after the basic block.</returns>
    T Apply(T state, BasicBlock block);
}
