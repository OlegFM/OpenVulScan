using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderSwitchTests
{
    [Fact]
    public void Switch_PhiHasOneOperandPerCaseOnMerge()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(int kind)
    {
        int x = 0;
        switch (kind)
        {
            case 1: x = 1; break;
            case 2: x = 2; break;
            default: x = 3; break;
        }
        var y = x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Merge block after switch: more than 2 predecessors.
        var mergeBlock = cfg.Blocks
            .Where(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry)
            .OrderByDescending(b => b.Predecessors.Length)
            .First();

        var phis = index.PhisAt(mergeBlock);
        Assert.NotEmpty(phis);
        Assert.Equal(mergeBlock.Predecessors.Length, phis[0].Operands.Length);
    }
}
