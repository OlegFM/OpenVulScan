using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests;

public class IntervalSsaEdgeRefinerTests
{
    [Fact]
    public void RelationalGuard_RefinesTrueBranch()
    {
        // Inside `if (n < 10)`, y = n must be ⊑ [-∞, 9].
        var state = SolveAndGetLocal(@"
class C
{
    void M(int n)
    {
        if (n < 10)
        {
            int y = n;
        }
    }
}", "y");

        Assert.False(state.IsEmpty);
        Assert.True(state.Upper <= 9, $"expected upper <= 9, got {state}");
    }

    [Fact]
    public void CompoundGuard_RefinesBothBounds()
    {
        // Inside `if (n >= 0 && n < 10)`, y = n must be ⊑ [0, 9].
        var state = SolveAndGetLocal(@"
class C
{
    void M(int n)
    {
        if (n >= 0 && n < 10)
        {
            int y = n;
        }
    }
}", "y");

        Assert.Equal(IntervalValue.Range(0, 9), state);
    }

    [Fact]
    public void NegatedGuard_RefinesFalseBranchViaElse()
    {
        // In the else of `if (n < 10)`, y = n must be ⊑ [10, +∞].
        var state = SolveAndGetLocal(@"
class C
{
    void M(int n)
    {
        if (n < 10) { } else { int y = n; }
    }
}", "y");

        Assert.False(state.IsEmpty);
        Assert.True(state.Lower >= 10, $"expected lower >= 10, got {state}");
    }

    [Fact]
    public void CountingLoop_WidensThenRefines_BodySeesBoundedIndex()
    {
        // Widening sends i to [0, +∞] at the header; the i < 10 true-edge
        // refinement must bound the BODY's view to [0, 9].
        var state = SolveAndGetLocal(@"
class C
{
    void M()
    {
        for (int i = 0; i < 10; i = i + 1)
        {
            int y = i;
        }
    }
}", "y");

        Assert.Equal(IntervalValue.Range(0, 9), state);
    }

    [Fact]
    public void ReversedOperands_LiteralOnLeft_Mirrored()
    {
        // `10 > n` ≡ `n < 10`.
        var state = SolveAndGetLocal(@"
class C
{
    void M(int n)
    {
        if (10 > n)
        {
            int y = n;
        }
    }
}", "y");

        Assert.False(state.IsEmpty);
        Assert.True(state.Upper <= 9, $"expected upper <= 9, got {state}");
    }

    private static IntervalValue SolveAndGetLocal(string source, string localName)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(source);
        var index = SsaBuilder.Build(cfg, model);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, IntervalValue>>(
            new MapLattice<SsaId, IntervalLattice, IntervalValue>(),
            new IntervalSsaTransfer(index),
            new IntervalSsaEdgeRefiner(index));
        var result = solver.Solve(cfg);
        Assert.True(result.Converged);

        var sym = (ISymbol)model.GetDeclaredSymbol(
            model.SyntaxTree.GetRoot().DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .First(v => v.Identifier.ValueText == localName))!;
        var versions = index.AllVersions(new TrackedKey.Symbol(sym));
        Assert.Single(versions);

        // Read the value from the DEFINING block's out-state: the SSA map flows whole-method
        // state around, so other blocks (e.g. a loop header) carry stale, widened copies.
        foreach (var block in cfg.Blocks)
        {
            // The lowered CFG represents `int y = n;` as a simple assignment to the local.
            bool defines = OperationTree.Enumerate(block).Any(op =>
                (op is Microsoft.CodeAnalysis.Operations.IVariableDeclaratorOperation decl
                 && SymbolEqualityComparer.Default.Equals(decl.Symbol, sym))
                || (op is Microsoft.CodeAnalysis.Operations.ISimpleAssignmentOperation
                    {
                        Target: Microsoft.CodeAnalysis.Operations.ILocalReferenceOperation target,
                    }
                    && SymbolEqualityComparer.Default.Equals(target.Local, sym)));
            if (defines && result.OutStates[block].TryGetValue(versions[0], out var v))
                return v;
        }

        return IntervalValue.Empty;
    }
}
