using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaTransferApplyPhisTests
{
    private const string JoinSnippet = @"
class C
{
    int M(bool c)
    {
        int x = 1;
        if (c)
        {
            x = 1;
        }
        return x;
    }
}";

    [Fact]
    public void ApplyPhis_Constant_JoinsPredecessorVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(JoinSnippet);
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);

        var joinBlock = cfg.Blocks.First(b => index.PhisAt(b).Any(p => p.Result.Key is TrackedKey.Symbol));
        var phi = index.PhisAt(joinBlock)
            .Single(p => p.Result.Key is TrackedKey.Symbol);

        var state = ImmutableDictionary<SsaId, ConstantLatticeValue>.Empty;
        foreach (var operand in phi.Operands)
            state = state.SetItem(operand.Version, ConstantLatticeValue.Const(1));

        var after = transfer.ApplyPhis(state, joinBlock);

        Assert.Equal(ConstantLatticeValue.Const(1), after[phi.Result]);
    }

    [Fact]
    public void ApplyPhis_NullState_JoinsPredecessorVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(JoinSnippet);
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);

        var joinBlock = cfg.Blocks.First(b => index.PhisAt(b).Any(p => p.Result.Key is TrackedKey.Symbol));
        var phi = index.PhisAt(joinBlock)
            .Single(p => p.Result.Key is TrackedKey.Symbol);

        var state = ImmutableDictionary<SsaId, NullState>.Empty;
        foreach (var operand in phi.Operands)
            state = state.SetItem(operand.Version, NullState.NotNull);

        var after = transfer.ApplyPhis(state, joinBlock);

        Assert.Equal(NullState.NotNull, after[phi.Result]);
    }

    [Fact]
    public void ApplyPhis_Constant_DifferingOperands_JoinToTop()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(JoinSnippet);
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);

        var joinBlock = cfg.Blocks.First(b => index.PhisAt(b).Any(p => p.Result.Key is TrackedKey.Symbol));
        var phi = index.PhisAt(joinBlock)
            .Single(p => p.Result.Key is TrackedKey.Symbol);

        // Seed the two φ operands with DIFFERENT constants: the join must be Top.
        var state = ImmutableDictionary<SsaId, ConstantLatticeValue>.Empty;
        var seed = 1;
        foreach (var operand in phi.Operands)
            state = state.SetItem(operand.Version, ConstantLatticeValue.Const(seed++));

        var after = transfer.ApplyPhis(state, joinBlock);

        Assert.Equal(ConstantLatticeValue.Top, after[phi.Result]);
    }
}
