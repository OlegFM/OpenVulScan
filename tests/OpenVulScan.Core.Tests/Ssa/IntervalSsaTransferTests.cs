using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class IntervalSsaTransferTests
{
    [Fact]
    public void Declaration_WithLiteralAndArithmetic_TracksInterval()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int a = 5;
        int b = a + 2;
        int c = b * 3;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        Assert.Equal(IntervalValue.Constant(21), LastVersionValue(state, model, index, "c"));
    }

    [Fact]
    public void ArrayCreation_TracksLengthIntervalForArrayVariable()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        var arr = new int[10];
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        Assert.Equal(IntervalValue.Constant(10), LastVersionValue(state, model, index, "arr"));
    }

    [Fact]
    public void Branch_JoinsAtPhi_ProducesHull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int M(bool f)
    {
        int x = 1;
        if (f) { x = 5; }
        return x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        // The φ at the merge must be the convex hull [1, 5].
        var sym = LocalSymbol(model, "x");
        var versions = index.AllVersions(new TrackedKey.Symbol(sym));
        Assert.Contains(
            IntervalValue.Range(1, 5),
            versions.Select(v => state.TryGetValue(v, out var s) ? s : IntervalValue.Empty));
    }

    [Fact]
    public void UnknownParameter_IsTop()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(int n)
    {
        int y = n;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunToFixpoint(cfg, index);

        Assert.Equal(IntervalValue.Top, LastVersionValue(state, model, index, "y"));
    }

    private static ImmutableDictionary<SsaId, IntervalValue> RunToFixpoint(
        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph cfg, SsaIndex index)
    {
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, IntervalValue>>(
            new MapLattice<SsaId, IntervalLattice, IntervalValue>(),
            new IntervalSsaTransfer(index));
        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        // The exit block's out-state carries the final map.
        return result.OutStates[cfg.Blocks.Last()];
    }

    private static ISymbol LocalSymbol(SemanticModel model, string name)
        => model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot().DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .First(v => v.Identifier.ValueText == name))!;

    private static IntervalValue LastVersionValue(
        ImmutableDictionary<SsaId, IntervalValue> state, SemanticModel model, SsaIndex index, string name)
    {
        var versions = index.AllVersions(new TrackedKey.Symbol(LocalSymbol(model, name)));
        return state[versions[^1]];
    }
}
