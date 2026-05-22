using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests;

public class WorklistSolverResultTests
{
    [Fact]
    public void Result_HoldsConvergedFlag()
    {
        var dict = ImmutableDictionary<BasicBlock, bool>.Empty;
        var result = new WorklistSolverResult<bool>(dict, converged: true);
        Assert.True(result.Converged);
        Assert.Same(dict, result.InStates);
    }
}

public class WorklistSolverTests
{
    [Fact]
    public void Solver_CanBeConstructed()
    {
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.Top);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);
        Assert.NotNull(solver);
    }

    private sealed class ConstantBoolTransfer : ITransfer<BoolLatticeValue>
    {
        private readonly BoolLatticeValue _value;
        public ConstantBoolTransfer(BoolLatticeValue value) => _value = value;
        public BoolLatticeValue Apply(BoolLatticeValue state, IOperation operation) => _value;
        public BoolLatticeValue Apply(BoolLatticeValue state, BasicBlock block) => _value;
    }

    [Fact]
    public void Linear_FixpointInOnePass()
    {
        var code = @"
class C
{
    void M()
    {
        int x = 1;
        int y = 2;
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

        var result = solver.Solve(cfg);

        Assert.True(result.Converged);
        foreach (var block in cfg.Blocks.Where(b => b.Kind != BasicBlockKind.Entry && !b.Predecessors.IsEmpty))
        {
            Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
        }
    }

    [Fact]
    public void IfElse_JoinsAtMerge()
    {
        var code = @"
class C
{
    void M(bool cond)
    {
        int x;
        if (cond)
            x = 1;
        else
            x = 2;
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        foreach (var block in cfg.Blocks.Where(b => b.Kind != BasicBlockKind.Entry && !b.Predecessors.IsEmpty))
        {
            Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
        }
    }

    [Fact]
    public void While_ConvergesInMultipleIterations()
    {
        var code = @"
class C
{
    void M()
    {
        int i = 0;
        while (i < 10)
            i++;
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        foreach (var block in cfg.Blocks.Where(b => b.Kind != BasicBlockKind.Entry && !b.Predecessors.IsEmpty))
        {
            Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
        }
    }

    [Fact]
    public void Switch_JoinsAtMerge()
    {
        var code = @"
class C
{
    void M(int n)
    {
        switch (n)
        {
            case 1: break;
            case 2: break;
            default: break;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        foreach (var block in cfg.Blocks.Where(b => b.Kind != BasicBlockKind.Entry && !b.Predecessors.IsEmpty))
        {
            Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
        }
    }

    [Fact]
    public void TryCatch_HandlesExceptionFlow()
    {
        var code = @"
class C
{
    void M()
    {
        try
        {
            int x = 1;
        }
        catch
        {
            int y = 2;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        foreach (var block in cfg.Blocks.Where(b => b.Kind != BasicBlockKind.Entry && !b.Predecessors.IsEmpty))
        {
            Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
        }
    }

    [Fact]
    public void MaxIterations_ReturnsBestApproximation()
    {
        var code = @"
class C
{
    void M()
    {
        int x = 1;
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new BoolFlatLattice();
        var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
        var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer, maxIterations: 0);

        var result = solver.Solve(cfg);
        Assert.False(result.Converged);
    }

    [Fact]
    public void NullState_Integration_SimpleMethod()
    {
        var code = @"
class C
{
    void M(string? s)
    {
        var len = s.Length;
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new NullStateLattice();
        var transfer = new NullStateTransfer();
        var solver = new WorklistSolver<NullState>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);
        Assert.NotEmpty(result.InStates);
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
