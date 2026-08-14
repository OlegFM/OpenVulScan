using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests;

/// <summary>
/// Widening support in <see cref="WorklistSolver{T}"/>: when the lattice implements
/// <see cref="IWideningLattice{T}"/>, back-edge targets (loop headers) apply
/// <see cref="IWideningLattice{T}.Widen"/> so infinite-height domains reach a fixpoint.
/// </summary>
public class WideningSolverTests
{
    private const string CountingLoop = @"
class C
{
    int M(int n)
    {
        int i = 0;
        while (i < n)
        {
            i = i + 1;
        }
        return i;
    }
}";

    [Fact]
    public void Solve_CountingLoop_WideningLattice_Converges()
    {
        var cfg = CompileAndGetCfg(CountingLoop);
        var solver = new WorklistSolver<IntervalValue>(
            new IntervalLattice(), new IncrementingTransfer(), maxIterations: 200);

        var result = solver.Solve(cfg, IntervalValue.Constant(0));

        Assert.True(result.Converged);
        // The loop header's state must have been driven to an unbounded upper end.
        Assert.Contains(result.InStates.Values, v => !v.IsEmpty && v.UpperIsInfinite);
    }

    [Fact]
    public void Solve_CountingLoop_PlainLattice_HitsIterationLimit()
    {
        // Contrast guard: the same strictly-ascending analysis under a NON-widening lattice
        // must exhaust the iteration budget — this is exactly why IWideningLattice exists,
        // and pins that the solver widens only when the lattice opts in.
        var cfg = CompileAndGetCfg(CountingLoop);
        var solver = new WorklistSolver<IntervalValue>(
            new NonWideningIntervalLattice(), new IncrementingTransfer(), maxIterations: 200);

        var result = solver.Solve(cfg, IntervalValue.Constant(0));

        Assert.False(result.Converged);
    }

    [Fact]
    public void MapLattice_IsWideningLattice_WidensPointwise()
    {
        var lattice = new MapLattice<string, IntervalLattice, IntervalValue>();

        var widening = Assert.IsAssignableFrom<IWideningLattice<ImmutableDictionary<string, IntervalValue>>>(lattice);

        var previous = ImmutableDictionary<string, IntervalValue>.Empty
            .Add("x", IntervalValue.Range(0, 0));
        var incoming = previous
            .SetItem("x", IntervalValue.Range(0, 5))
            .Add("y", IntervalValue.Range(1, 2));

        var widened = widening.Widen(previous, incoming);

        Assert.True(widened["x"].UpperIsInfinite, "the moving upper bound of x must widen to +∞");
        Assert.Equal(0, widened["x"].Lower);
        Assert.Equal(IntervalValue.Range(1, 2), widened["y"]);
    }

    // --- Test lattice and transfer ---

    /// <summary>
    /// Adds <c>[1, 1]</c> in every block that carries operations — a structural stand-in for
    /// a counting loop's <c>i = i + 1</c>: the joined loop-header state strictly ascends
    /// ([0,0] ⊑ [0,1] ⊑ …) and can only stabilise through widening.
    /// </summary>
    private sealed class IncrementingTransfer : ITransfer<IntervalValue>
    {
        public IntervalValue Apply(IntervalValue state, IOperation operation) => state;

        public IntervalValue Apply(IntervalValue state, BasicBlock block)
            => block.Operations.IsEmpty ? state : state.Add(IntervalValue.Constant(1));
    }

    /// <summary>The interval order without the widening interface — join-only.</summary>
    private sealed class NonWideningIntervalLattice : ILattice<IntervalValue>
    {
        private readonly IntervalLattice _inner = new();

        public IntervalValue Bottom => _inner.Bottom;

        public IntervalValue Top => _inner.Top;

        public IntervalValue Join(IntervalValue left, IntervalValue right) => _inner.Join(left, right);

        public bool LessOrEqual(IntervalValue left, IntervalValue right) => _inner.LessOrEqual(left, right);
    }

    private static ControlFlowGraph CompileAndGetCfg(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create(
            "Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var operation = model.GetOperation(method) ?? throw new InvalidOperationException("Failed to get operation for method");

        return operation switch
        {
            Microsoft.CodeAnalysis.Operations.IMethodBodyOperation methodBodyOp => ControlFlowGraph.Create(methodBodyOp),
            Microsoft.CodeAnalysis.Operations.IBlockOperation blockOp => ControlFlowGraph.Create(blockOp),
            _ => throw new InvalidOperationException($"Unsupported operation type: {operation.Kind}")
        };
    }
}
