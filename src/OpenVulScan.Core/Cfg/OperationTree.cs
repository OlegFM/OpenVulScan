using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

/// <summary>
/// Depth-first pre-order enumeration of Roslyn operation trees, shared by the
/// SSA builder and the SSA transfer functions.
/// </summary>
public static class OperationTree
{
    /// <summary>
    /// Enumerates <paramref name="operation"/> and all of its descendants, parents first.
    /// </summary>
    public static IEnumerable<IOperation> Enumerate(IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnumerateCore(operation);
    }

    /// <summary>
    /// Enumerates every operation in <paramref name="block"/> depth-first,
    /// including the branch value (if any) after the block operations.
    /// </summary>
    public static IEnumerable<IOperation> Enumerate(BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return EnumerateCore(block);
    }

    private static IEnumerable<IOperation> EnumerateCore(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            if (child is null) continue;
            foreach (var descendant in EnumerateCore(child))
                yield return descendant;
        }
    }

    private static IEnumerable<IOperation> EnumerateCore(BasicBlock block)
    {
        foreach (var op in block.Operations)
        {
            foreach (var descendant in EnumerateCore(op))
                yield return descendant;
        }
        if (block.BranchValue is not null)
        {
            foreach (var descendant in EnumerateCore(block.BranchValue))
                yield return descendant;
        }
    }
}
