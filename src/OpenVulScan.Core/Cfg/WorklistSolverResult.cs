using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed class WorklistSolverResult<T>
{
    public WorklistSolverResult(ImmutableDictionary<BasicBlock, T> inStates, ImmutableDictionary<BasicBlock, T> outStates, bool converged)
    {
        InStates = inStates;
        OutStates = outStates;
        Converged = converged;
    }

    public ImmutableDictionary<BasicBlock, T> InStates { get; }

    public ImmutableDictionary<BasicBlock, T> OutStates { get; }

    public bool Converged { get; }
}
