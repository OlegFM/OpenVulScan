using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderStraightLineTests
{
    [Fact]
    public void TwoSequentialDefs_GetDistinctVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x = 1;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var defs = cfg.Blocks
            .SelectMany(b => b.Operations)
            .SelectMany(EnumerateAllOps)
            .Select(op => index.DefinitionAt(op))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        Assert.Equal(2, defs.Count);
        Assert.Equal(0, defs[0].Version);
        Assert.Equal(1, defs[1].Version);
        Assert.Equal(defs[0].Key, defs[1].Key);
    }

    [Fact]
    public void ParameterIsDefinedAtEntry_Version0()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(string s)
    {
        var len = s.Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var param = (IParameterSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("M").OfType<IMethodSymbol>().First().Parameters[0];

        var versions = index.AllVersions(new TrackedKey.Symbol(param));
        Assert.NotEmpty(versions);
        Assert.Equal(0, versions[0].Version);
    }

    private static IEnumerable<IOperation> EnumerateAllOps(IOperation op)
    {
        yield return op;
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            foreach (var d in EnumerateAllOps(child))
                yield return d;
        }
    }
}
