using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderFieldKillTests
{
    [Fact]
    public void ThisField_GetsNewVersionAfterInstanceMethodCall()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        this.f = 1;
        Side();
        var y = this.f;
    }
    void Side() { }
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var versions = index.AllVersions(new TrackedKey.InstanceField(field));

        // Expect ≥2 versions: initial assign, kill after Side().
        Assert.True(versions.Count >= 2, $"Expected ≥2 versions, got {versions.Count}");
    }

    [Fact]
    public void ThisField_NotKilledByStaticMethodCall()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        this.f = 1;
        Pure();
        var y = this.f;
    }
    static void Pure() { }
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var versions = index.AllVersions(new TrackedKey.InstanceField(field));

        // Only 1 def (the assignment); no kill from static call.
        Assert.Single(versions);
    }
}
