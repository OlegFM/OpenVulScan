using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderIfElseTests
{
    [Fact]
    public void IfElse_PlacesPhiAtMerge_WithBothBranchVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool cond)
    {
        int x = 0;
        if (cond) x = 1;
        else x = 2;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Find merge block: >=2 predecessors, not entry.
        var mergeBlock = cfg.Blocks.First(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry);
        var phis = index.PhisAt(mergeBlock);
        Assert.Single(phis);

        var phi = phis[0];
        Assert.Equal(2, phi.Operands.Length);
        // phi result version must be greater than both operand versions.
        Assert.All(phi.Operands, o => Assert.True(o.Version.Version < phi.Result.Version));
    }

    [Fact]
    public void IfWithoutElse_PlacesPhi_WithEntryAndIfBranchVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool cond)
    {
        int x = 0;
        if (cond) x = 1;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var mergeBlock = cfg.Blocks.First(b => b.Predecessors.Length >= 2 && b.Kind != BasicBlockKind.Entry);
        var phis = index.PhisAt(mergeBlock);
        Assert.Single(phis);
        Assert.Equal(2, phis[0].Operands.Length);
    }
}
