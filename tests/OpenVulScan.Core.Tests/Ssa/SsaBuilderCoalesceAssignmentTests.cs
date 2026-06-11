using System.Linq;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderCoalesceAssignmentTests
{
    [Fact]
    public void CoalesceAssignment_RegistersNewVersionForTarget()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(string s)
    {
        s ??= ""fallback"";
        var t = s;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var param = model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("M").OfType<Microsoft.CodeAnalysis.IMethodSymbol>().First().Parameters[0];
        var key = new TrackedKey.Symbol(param);

        // v0 is the parameter seed; the ??= write must add at least one more version.
        var versions = index.AllVersions(key);
        Assert.True(versions.Count >= 2,
            $"Expected >= 2 versions for 's' (param seed + ??= write), got {versions.Count}");
    }
}
