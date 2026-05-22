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
        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody ?? throw new InvalidOperationException("Method has no body");

        var cfg = ControlFlowGraph.Create(body, model);
        return cfg ?? throw new InvalidOperationException("Failed to create CFG");
    }
}
