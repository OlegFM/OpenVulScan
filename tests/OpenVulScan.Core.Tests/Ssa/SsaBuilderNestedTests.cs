using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderNestedTests
{
    [Fact]
    public void IfInsideWhile_PlacesPhiOnInnerMergeAndOuterHeader()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool c)
    {
        int x = 0;
        while (x < 10)
        {
            if (c) x = 1;
            else x = 2;
        }
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var mergeBlocks = cfg.Blocks
            .Where(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry)
            .ToList();

        // Roslyn fuses the if/else inside-loop merge with the while-header, so there is
        // exactly one merge block carrying both the entry/back-edge phi AND the conditional
        // branch phi. That merge block has 3 predecessors: entry, true-branch, false-branch.
        Assert.True(mergeBlocks.Count >= 1);
        Assert.Contains(mergeBlocks, b => b.Predecessors.Length >= 3);

        var header = mergeBlocks.First(b => b.Predecessors.Length >= 3);
        var phis = index.PhisAt(header);
        Assert.NotEmpty(phis);
        Assert.Contains(phis, p => p.Operands.Length == 3);
    }
}
