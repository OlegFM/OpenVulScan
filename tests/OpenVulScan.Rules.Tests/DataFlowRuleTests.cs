using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests;

public class DataFlowRuleTests
{
    private static CSharpCompilation CreateTestCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation;
    }

    #region DataFlowContext Tests

    [Fact]
    public void DataFlowContextStoresOperationAndModelAndCompilation()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var operation = model.GetOperation(method)!;
        var token = new CancellationToken();

        var context = new DataFlowContext(operation, model, compilation, SsaIndex.Empty, token);

        Assert.Same(operation, context.Operation);
        Assert.Same(model, context.SemanticModel);
        Assert.Same(compilation, context.Compilation);
        Assert.Equal(token, context.CancellationToken);
    }

    [Fact]
    public void DataFlowContextReportDiagnosticAddsToList()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var operation = model.GetOperation(method)!;

        var context = new DataFlowContext(operation, model, compilation, SsaIndex.Empty, CancellationToken.None);
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("TEST001", "Test", "Message", "Test", DiagnosticSeverity.Warning, true),
            Location.None);

        context.ReportDiagnostic(diagnostic);

        Assert.Single(context.Diagnostics);
        Assert.Same(diagnostic, context.Diagnostics[0]);
    }

    #endregion

    #region DataFlowRule Tests

    private sealed class DummyNreRule : DataFlowRule<NullState>
    {
        public override ILattice<NullState> Lattice => new NullStateLattice();
        public override ITransfer<NullState> Transfer => new DummyNreTransfer();

        protected override void OnState(IOperation operation, NullState state, DataFlowContext context)
        {
            if (operation is IMemberReferenceOperation && state == NullState.DefinitelyNull)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor("NRE001", "NRE", "Possible null dereference", "Test", DiagnosticSeverity.Warning, true),
                    operation.Syntax.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private sealed class DummyNreTransfer : ITransfer<NullState>
    {
        public NullState Apply(NullState state, IOperation operation) => NullState.DefinitelyNull;
        public NullState Apply(NullState state, BasicBlock block) => NullState.DefinitelyNull;
    }

    [Fact]
    public void DataFlowRuleLatticeAndTransferAreAccessible()
    {
        var rule = new DummyNreRule();
        Assert.NotNull(rule.Lattice);
        Assert.NotNull(rule.Transfer);
    }

    [Fact]
    public void DataFlowRuleDispatcherCallsOnStateForOperations()
    {
        var source = @"
class C
{
    void M()
    {
        string? s = null;
        var x = s.Length;
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new DummyNreRule();
        var dispatcher = new DataFlowRuleDispatcher<NullState>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("NRE001", diagnostics[0].Id);
    }

    [Fact]
    public void DataFlowRuleDispatcherRespectsCancellationToken()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var rule = new DummyNreRule();
        var dispatcher = new DataFlowRuleDispatcher<NullState>(new[] { rule }, compilation);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Record.Exception(() => dispatcher.Run(cts.Token));
        Assert.IsType<OperationCanceledException>(ex);
    }

    [Fact]
    public void DataFlowRuleDispatcherNoRulesDoesNotThrow()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var dispatcher = new DataFlowRuleDispatcher<NullState>(Array.Empty<DataFlowRule<NullState>>(), compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);
        Assert.Empty(diagnostics);
    }

    #endregion
}
