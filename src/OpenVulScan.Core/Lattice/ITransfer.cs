using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// Defines a transfer function that maps a lattice state through a single
/// <see cref="IOperation"/>.
/// </summary>
/// <typeparam name="T">The lattice element type.</typeparam>
/// <remarks>
/// Transfer functions are the building blocks of data-flow analysis:
/// given an abstract state before an operation, they produce the abstract
/// state after the operation.
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
}
