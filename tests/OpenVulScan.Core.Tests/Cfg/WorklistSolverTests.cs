using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests;

public class WorklistSolverResultTests
{
    [Fact]
    public void Result_HoldsConvergedFlag()
    {
        var dict = ImmutableDictionary<BasicBlock, bool>.Empty;
        var result = new WorklistSolverResult<bool>(dict, converged: true);
        Assert.True(result.Converged);
        Assert.Same(dict, result.InStates);
    }
}
