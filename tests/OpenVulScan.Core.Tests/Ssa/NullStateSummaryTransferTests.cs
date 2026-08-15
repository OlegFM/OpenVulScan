using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class NullStateSummaryTransferTests
{
    private const string Source = @"
class C
{
    string F() { return null; }

    void M()
    {
        string s = F();
    }
}";

    [Fact]
    public void Invocation_WithDefinitelyNullSummary_TracksDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, StateOfS(new FixedLookup(NullState.DefinitelyNull)));
    }

    [Fact]
    public void Invocation_WithNotNullSummary_TracksNotNull()
    {
        Assert.Equal(NullState.NotNull, StateOfS(new FixedLookup(NullState.NotNull)));
    }

    [Fact]
    public void Invocation_WithoutLookup_StaysUnknown()
    {
        Assert.Equal(NullState.Unknown, StateOfS(summaries: null));
    }

    private static NullState StateOfS(INullabilitySummaryLookup? summaries)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(Source, methodName: "M");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index, summaries);
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
        return state[versions[0]];
    }

    private sealed class FixedLookup : INullabilitySummaryLookup
    {
        private readonly NullState _state;

        public FixedLookup(NullState state) => _state = state;

        public NullState ReturnStateOf(IMethodSymbol method) => _state;
    }
}
