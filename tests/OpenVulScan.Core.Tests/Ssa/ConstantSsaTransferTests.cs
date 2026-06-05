using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class ConstantSsaTransferTests
{
    [Fact]
    public void Assignment_OfIntegerLiteral_TracksConstantValue()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 42;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, ConstantLatticeValue>.Empty;

        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);

        var localSym = (ISymbol)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .First())!;
        var versions = index.AllVersions(new TrackedKey.Symbol(localSym));
        Assert.Single(versions);

        var value = state[versions[0]];
        Assert.Equal(LatticeElementKind.Const, value.Kind);
        Assert.Equal(42, value.Value);
    }
}
