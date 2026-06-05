using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class NullStateSsaTransferTests
{
    [Fact]
    public void Assignment_OfNullLiteral_TracksDefinitelyNull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        string s = null;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, NullState>.Empty;

        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);

        var localSym = (ISymbol)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .First())!;
        var versions = index.AllVersions(new TrackedKey.Symbol(localSym));
        Assert.Single(versions);

        Assert.Equal(NullState.DefinitelyNull, state[versions[0]]);
    }
}
