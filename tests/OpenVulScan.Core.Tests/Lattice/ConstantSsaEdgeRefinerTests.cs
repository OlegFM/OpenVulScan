using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests.Lattice;

public class ConstantSsaEdgeRefinerTests
{
    private static (ControlFlowGraph Cfg, SsaIndex Index) Build(string snippet)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(snippet);
        return (cfg, SsaBuilder.Build(cfg, model));
    }

    private static ImmutableDictionary<SsaId, ConstantLatticeValue> Solve(
        ControlFlowGraph cfg, SsaIndex index, BasicBlock block)
    {
        var lattice = new MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>();
        var transfer = new ConstantSsaTransfer(index);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
            lattice, transfer, new ConstantSsaEdgeRefiner(index));
        var result = solver.Solve(cfg, CancellationToken.None);
        return transfer.ApplyPhis(result.InStates[block], block);
    }

    private static BasicBlock BlockAssigning(ControlFlowGraph cfg, string localName)
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

    // The RHS of "t = x;" reads x — look up the SSA use version of that reference
    // in the (edge-refined) entry state of the assigning block.
    private static ConstantLatticeValue StateOfRhsRead(
        ControlFlowGraph cfg, SsaIndex index, ImmutableDictionary<SsaId, ConstantLatticeValue> state, string localName)
    {
        var assign = cfg.Blocks.SelectMany(b => b.Operations)
            .SelectMany(DescendAll)
            .OfType<ISimpleAssignmentOperation>()
            .First(a => a.Target is ILocalReferenceOperation l && l.Local.Name == localName);
        var rhs = Unwrap(assign.Value);
        TrackedKey key = rhs switch
        {
            IParameterReferenceOperation p => new TrackedKey.Symbol(p.Parameter),
            ILocalReferenceOperation l => new TrackedKey.Symbol(l.Local),
            _ => throw new InvalidOperationException($"Unexpected RHS operation {rhs.Kind}"),
        };
        var use = index.UseAt(rhs, key);
        Assert.NotNull(use);
        return state.TryGetValue(use.Value, out var s) ? s : ConstantLatticeValue.Top;

        static IEnumerable<IOperation> DescendAll(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in DescendAll(child)) yield return d;
            }
        }

        static IOperation Unwrap(IOperation op)
        {
            while (op is IConversionOperation c) op = c.Operand;
            while (op is IParenthesizedOperation p) op = p.Operand;
            return op;
        }
    }

    [Fact]
    public void EqualsConst_ThenBranch_NarrowsToConst()
    {
        var (cfg, index) = Build(@"
class C { void M(int x) { int t;
    if (x == 5) { t = x; } } }");

        var thenState = Solve(cfg, index, BlockAssigning(cfg, "t"));

        Assert.Equal(ConstantLatticeValue.Const(5), StateOfRhsRead(cfg, index, thenState, "t"));
    }

    [Fact]
    public void EqualsConst_ElseBranch_StaysUnknown()
    {
        // x != 5 on the else edge tells us nothing representable in a constant
        // lattice (we cannot express "anything but 5"), so x stays ⊤.
        var (cfg, index) = Build(@"
class C { void M(int x) { int t; int u;
    if (x == 5) { t = x; } else { u = x; } } }");

        var elseState = Solve(cfg, index, BlockAssigning(cfg, "u"));

        Assert.Equal(ConstantLatticeValue.Top, StateOfRhsRead(cfg, index, elseState, "u"));
    }

    [Fact]
    public void NotEqualsConst_ElseBranch_NarrowsToConst()
    {
        // The else edge of `x != 5` is the `x == 5` arm.
        var (cfg, index) = Build(@"
class C { void M(int x) { int t;
    if (x != 5) { } else { t = x; } } }");

        var elseState = Solve(cfg, index, BlockAssigning(cfg, "t"));

        Assert.Equal(ConstantLatticeValue.Const(5), StateOfRhsRead(cfg, index, elseState, "t"));
    }

    [Fact]
    public void NegatedEquals_InvertsBranchSense()
    {
        // !(x == 5) flips: the else edge is the `x == 5` arm.
        var (cfg, index) = Build(@"
class C { void M(int x) { int t;
    if (!(x == 5)) { } else { t = x; } } }");

        var elseState = Solve(cfg, index, BlockAssigning(cfg, "t"));

        Assert.Equal(ConstantLatticeValue.Const(5), StateOfRhsRead(cfg, index, elseState, "t"));
    }

    [Fact]
    public void GuardOnOtherVariable_DoesNotRefine()
    {
        var (cfg, index) = Build(@"
class C { void M(int x, int y) { int t;
    if (y == 5) { t = x; } } }");

        var thenState = Solve(cfg, index, BlockAssigning(cfg, "t"));

        Assert.Equal(ConstantLatticeValue.Top, StateOfRhsRead(cfg, index, thenState, "t"));
    }

    [Fact]
    public void BareBool_ThenBranch_NarrowsToTrue()
    {
        var (cfg, index) = Build(@"
class C { void M(bool b) { bool t;
    if (b) { t = b; } } }");

        var thenState = Solve(cfg, index, BlockAssigning(cfg, "t"));

        Assert.Equal(ConstantLatticeValue.Const(true), StateOfRhsRead(cfg, index, thenState, "t"));
    }

    [Fact]
    public void ConditionalAnd_TrueBranch_RefinesBothOperands()
    {
        var (cfg, index) = Build(@"
class C { void M(int x, int y) { int t;
    if (x == 5 && y == 6) { t = x; } } }");

        var thenState = Solve(cfg, index, BlockAssigning(cfg, "t"));

        Assert.Equal(ConstantLatticeValue.Const(5), StateOfRhsRead(cfg, index, thenState, "t"));
    }
}
