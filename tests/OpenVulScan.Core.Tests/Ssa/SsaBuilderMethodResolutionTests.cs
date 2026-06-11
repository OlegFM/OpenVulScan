using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderMethodResolutionTests
{
    private const string TwoMethodSnippet = @"
class C
{
    void First(int a)
    {
        var z = a;
    }

    void Second(string s)
    {
        var t = s;
    }
}";

    [Fact]
    public void SecondMethodInFile_SeedsItsOwnParameters()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(TwoMethodSnippet, methodName: "Second");
        var index = SsaBuilder.Build(cfg, model);

        var methods = model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers().OfType<IMethodSymbol>().ToList();
        var firstParam = methods.First(m => m.Name == "First").Parameters[0];
        var secondParam = methods.First(m => m.Name == "Second").Parameters[0];

        // The CFG belongs to Second: its parameter 's' must be seeded at v0 ...
        var sVersions = index.AllVersions(new TrackedKey.Symbol(secondParam));
        Assert.NotEmpty(sVersions);
        Assert.Equal(0, sVersions[0].Version);

        // ... and First's parameter 'a' must not leak into this index.
        Assert.Empty(index.AllVersions(new TrackedKey.Symbol(firstParam)));
    }
}
