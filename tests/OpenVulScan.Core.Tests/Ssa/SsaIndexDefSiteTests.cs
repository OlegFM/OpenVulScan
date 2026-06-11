using System.Linq;
using Microsoft.CodeAnalysis.Operations;
using Xunit;
using static OpenVulScan.Tests.Ssa.CfgTestHarness;

namespace OpenVulScan.Tests.Ssa;

public class SsaIndexDefSiteTests
{

    [Fact]
    public void DefSiteOf_AssignmentDef_RoundTrips()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C { void M() { int x = 0; x = 1; } }");
        var index = SsaBuilder.Build(cfg, model);

        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .First(a => a.Value.ConstantValue.Value is 1);
        var id = index.DefinitionAt(assign);

        Assert.NotNull(id);
        Assert.Same(assign, index.DefSiteOf(id.Value));
    }

    [Fact]
    public void DefSiteOf_PhiResult_ReturnsNull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C { void M(bool c) { int x = 1; if (c) { x = 2; } int y = x; } }");
        var index = SsaBuilder.Build(cfg, model);

        var phi = cfg.Blocks.SelectMany(b => index.PhisAt(b))
            .First(p => p.Result.Key is TrackedKey.Symbol);

        Assert.Null(index.DefSiteOf(phi.Result));
    }
}
