using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderStraightLineTests
{
    private static readonly int[] ExpectedXVersions = [0, 1];

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

    [Fact]
    public void IncrementOperation_AdvancesVersionAndDoesNotCreatePhantomUse()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x++;
        var y = x;
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

        // defs: 'int x = 0' (x v0), 'x++' (x v1), 'var y = x' (y v0) = 3 defs total
        Assert.Equal(3, defs.Count);

        // x has versions 0 and 1
        var xVersions = defs
            .Where(d => d.Key is TrackedKey.Symbol sym && sym.Variable.Name == "x")
            .Select(d => d.Version)
            .OrderBy(v => v)
            .ToList();
        Assert.Equal(ExpectedXVersions, xVersions);

        // No phantom use should be recorded for the Target lref of `x++`.
        // Find the x++ operation and its Target child; UseAt on the Target should return null.
        var incrementOp = cfg.Blocks
            .SelectMany(b => b.Operations)
            .SelectMany(EnumerateAllOps)
            .OfType<Microsoft.CodeAnalysis.Operations.IIncrementOrDecrementOperation>()
            .First();
        var targetChild = incrementOp.Target;
        var lref = (Microsoft.CodeAnalysis.Operations.ILocalReferenceOperation)targetChild;
        var key = new TrackedKey.Symbol(lref.Local);
        Assert.Null(index.UseAt(targetChild, key));
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
