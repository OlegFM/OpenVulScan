using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderShadowingTests
{
    [Fact]
    public void ShadowedLocals_AreTrackedSeparately()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool c)
    {
        int x = 1;
        if (c)
        {
            int x = 2;
            var y = x;
        }
        var z = x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var declarators = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Identifier.Text == "x")
            .Select(v => (ISymbol)model.GetDeclaredSymbol(v)!)
            .ToList();

        Assert.Equal(2, declarators.Count);
        var k1 = new TrackedKey.Symbol(declarators[0]);
        var k2 = new TrackedKey.Symbol(declarators[1]);
        Assert.NotEqual(k1, k2);

        Assert.NotEmpty(index.AllVersions(k1));
        Assert.NotEmpty(index.AllVersions(k2));
    }
}
