using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;
using static OpenVulScan.Tests.Ssa.CfgTestHarness;

namespace OpenVulScan.Tests.Ssa;

public class SsaTransferCaptureTests
{
    [Fact]
    public void CaptureDef_TakesNullStateOfCapturedExpression()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string M()
    {
        string a = ""x"";
        var t = a ?? ""y"";
        return t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, NullState>.Empty;

        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);

        // The capture of `a` (the ?? receiver) must carry a's value: NotNull.
        var captureOfA = AllOps(cfg).OfType<IFlowCaptureOperation>()
            .Single(c => c.Value is ILocalReferenceOperation);
        var captureId = index.DefinitionAt(captureOfA);
        Assert.NotNull(captureId);
        Assert.Equal(NullState.NotNull, state[captureId.Value]);
    }

    [Fact]
    public void CaptureReference_FlowsNullStateToConsumer()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string M()
    {
        string a = ""x"";
        var t = a ?? ""y"";
        return t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);
        var lattice = new MapLattice<SsaId, NullStateLattice, NullState>();
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, NullState>>(lattice, transfer);
        var result = solver.Solve(cfg, CancellationToken.None);

        // Both ?? arms are NotNull ("x" via the capture chain, "y" literal),
        // so t must be NotNull. Without capture evaluation it degrades to Unknown.
        var tSym = FindLocal(cfg, "t");
        var tVersion = index.AllVersions(new TrackedKey.Symbol(tSym)).Single();

        var exit = cfg.Blocks.Single(b => b.Kind == BasicBlockKind.Exit);
        Assert.Equal(NullState.NotNull, result.InStates[exit][tVersion]);
    }

    [Fact]
    public void CaptureReference_FlowsConstantToConsumer()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int M(bool c)
    {
        var t = c ? 5 : 5;
        return t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);
        var lattice = new MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>();
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, ConstantLatticeValue>>(lattice, transfer);
        var result = solver.Solve(cfg, CancellationToken.None);

        // Both ternary arms write Const(5) into the same capture; the join is
        // Const(5) and t reads it through the capture reference.
        var tSym = FindLocal(cfg, "t");
        var tVersion = index.AllVersions(new TrackedKey.Symbol(tSym)).Single();

        var exit = cfg.Blocks.Single(b => b.Kind == BasicBlockKind.Exit);
        Assert.Equal(ConstantLatticeValue.Const(5), result.InStates[exit][tVersion]);
    }

    private static ILocalSymbol FindLocal(ControlFlowGraph cfg, string name)
        => AllOps(cfg).OfType<ILocalReferenceOperation>().Select(l => l.Local)
            .Concat(AllOps(cfg).OfType<IVariableDeclaratorOperation>().Select(d => d.Symbol))
            .First(s => s.Name == name);
}
