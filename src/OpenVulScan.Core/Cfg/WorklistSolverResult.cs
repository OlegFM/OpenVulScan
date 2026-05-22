using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed class WorklistSolverResult<T>
{
    public WorklistSolverResult(ImmutableDictionary<BasicBlock, T> inStates, bool converged)
    {
        InStates = inStates;
        Converged = converged;
    }

    public ImmutableDictionary<BasicBlock, T> InStates { get; }

    public bool Converged { get; }
}
