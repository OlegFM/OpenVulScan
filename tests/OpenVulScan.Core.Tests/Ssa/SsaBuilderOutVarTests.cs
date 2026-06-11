using System.Linq;
using Microsoft.CodeAnalysis.Operations;
using Xunit;
using static OpenVulScan.Tests.Ssa.CfgTestHarness;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderOutVarTests
{
    [Fact]
    public void OutVarArgument_DefinesLocalAndSubsequentReadBindsToIt()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(string s)
    {
        int.TryParse(s, out var x);
        var y = x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // The read of x in `var y = x` is the lref that is NOT the out-var declaration.
        var readOfX = AllOps(cfg)
            .OfType<ILocalReferenceOperation>()
            .Single(l => l.Local.Name == "x" && l.Parent is not IDeclarationExpressionOperation);

        var key = new TrackedKey.Symbol(readOfX.Local);

        // out var x must register a definition for x ...
        Assert.NotEmpty(index.AllVersions(key));
        // ... and the later read must bind to that version.
        var use = index.UseAt(readOfX, key);
        Assert.NotNull(use);
        Assert.Equal(0, use!.Value.Version);
    }
}
