using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests.Lattice;

public class NullStateSsaEdgeRefinerTests
{
    private static (ControlFlowGraph Cfg, SsaIndex Index) Build(string snippet)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(snippet);
        return (cfg, SsaBuilder.Build(cfg, model));
    }

    private static ImmutableDictionary<SsaId, NullState> Solve(
        ControlFlowGraph cfg, SsaIndex index, BasicBlock block)
    {
        var lattice = new MapLattice<SsaId, NullStateLattice, NullState>();
        var transfer = new NullStateSsaTransfer(index);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, NullState>>(
            lattice, transfer, new NullStateSsaEdgeRefiner(index));
        var result = solver.Solve(cfg, CancellationToken.None);
        return transfer.ApplyPhis(result.InStates[block], block);
    }

    private static BasicBlock BlockDefining(ControlFlowGraph cfg, SsaIndex index, string localName)
    {
        return cfg.Blocks.First(b => b.Operations
            .SelectMany(Descend)
            .Any(op => op is ISimpleAssignmentOperation { Target: ILocalReferenceOperation l }
                && l.Local.Name == localName));

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    private static NullState StateOfLocalInit(
        ControlFlowGraph cfg, SsaIndex index, ImmutableDictionary<SsaId, NullState> state, string localName)
    {
        // The RHS of "t = a;" reads a — look up the use version of the RHS reference.
        var assign = cfg.Blocks.SelectMany(b => b.Operations)
            .SelectMany(DescendAll)
            .OfType<ISimpleAssignmentOperation>()
            .First(a => a.Target is ILocalReferenceOperation l && l.Local.Name == localName);
        var rhs = (IParameterReferenceOperation)assign.Value;
        var use = index.UseAt(rhs, new TrackedKey.Symbol(rhs.Parameter));
        Assert.NotNull(use);
        return state.TryGetValue(use.Value, out var s) ? s : NullState.Unknown;

        static IEnumerable<IOperation> DescendAll(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in DescendAll(child)) yield return d;
            }
        }
    }

    [Fact]
    public void NotEqualsNull_RefinesThenAndElseBranches()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t; string u;
    if (a != null) { t = a; } else { u = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));
        var elseState = Solve(cfg, index, BlockDefining(cfg, index, "u"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
        Assert.Equal(NullState.DefinitelyNull, StateOfLocalInit(cfg, index, elseState, "u"));
    }

    [Fact]
    public void IsNotNullPattern_RefinesThenBranch()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t;
    if (a is not null) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void NegatedEqualsNull_InvertsBranchSense()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t;
    if (!(a == null)) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void ConditionalAnd_TrueBranch_RefinesBothOperands()
    {
        var (cfg, index) = Build(@"
class C { void M(string a, string b) { string t;
    if (a != null && b != null) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void GuardOnOtherVariable_DoesNotRefineThisOne()
    {
        var (cfg, index) = Build(@"
class C { void M(string a, string b) { string t;
    if (b != null) { t = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.Unknown, StateOfLocalInit(cfg, index, thenState, "t"));
    }

    [Fact]
    public void LoweredIsNullBranch_RefinesCaptureOnBothEdges()
    {
        // a ?? "y" lowers to: capture(a); branch on IsNull(captureRef); …
        var (cfg, index) = Build(@"
class C { void M(string a) { var x = a ?? ""y""; } }");

        var branchBlock = cfg.Blocks.First(b =>
            b.BranchValue is IIsNullOperation { Operand: IFlowCaptureReferenceOperation });
        var isNull = (IIsNullOperation)branchBlock.BranchValue!;
        var cref = (IFlowCaptureReferenceOperation)isNull.Operand;
        var use = index.UseAt(cref, new TrackedKey.Capture(cref.Id));
        Assert.NotNull(use);

        var refiner = new NullStateSsaEdgeRefiner(index);
        var seed = ImmutableDictionary<SsaId, NullState>.Empty.SetItem(use.Value, NullState.MaybeNull);

        // IsNull is true on the null edge: when ConditionKind is WhenTrue the
        // conditional successor is the null arm, otherwise the fall-through is.
        var nullEdge = branchBlock.ConditionKind == ControlFlowConditionKind.WhenTrue
            ? branchBlock.ConditionalSuccessor!
            : branchBlock.FallThroughSuccessor!;
        var notNullEdge = ReferenceEquals(nullEdge, branchBlock.ConditionalSuccessor)
            ? branchBlock.FallThroughSuccessor!
            : branchBlock.ConditionalSuccessor!;

        Assert.Equal(NullState.DefinitelyNull, refiner.Refine(seed, nullEdge)[use.Value]);
        Assert.Equal(NullState.NotNull, refiner.Refine(seed, notNullEdge)[use.Value]);
    }

    [Fact]
    public void EqualsNull_RefinesThenAndElseBranches()
    {
        var (cfg, index) = Build(@"
class C { void M(string a) { string t; string u;
    if (a == null) { t = a; } else { u = a; } } }");

        var thenState = Solve(cfg, index, BlockDefining(cfg, index, "t"));
        var elseState = Solve(cfg, index, BlockDefining(cfg, index, "u"));

        Assert.Equal(NullState.DefinitelyNull, StateOfLocalInit(cfg, index, thenState, "t"));
        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, elseState, "u"));
    }

    [Fact]
    public void ConditionalOr_FalseBranch_RefinesBothOperands()
    {
        var (cfg, index) = Build(@"
class C { void M(string a, string b) { string t;
    if (a == null || b == null) { } else { t = a; } } }");

        var elseState = Solve(cfg, index, BlockDefining(cfg, index, "t"));

        Assert.Equal(NullState.NotNull, StateOfLocalInit(cfg, index, elseState, "t"));
    }
}
