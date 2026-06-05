using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderLoopTests
{
    [Fact]
    public void WhileLoop_PlacesPhiOnHeader_WithEntryAndBackEdgeOperands()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        while (x < 10) { x++; }
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Loop header: block with a back-edge predecessor (pred ordinal >= block ordinal).
        var header = cfg.Blocks.First(b =>
            b.Predecessors.Length >= 2 &&
            b.Predecessors.Any(p => p.Source.Ordinal >= b.Ordinal));

        var phis = index.PhisAt(header);
        Assert.NotEmpty(phis);
        Assert.Contains(phis, p => p.Operands.Length >= 2);
    }
}
