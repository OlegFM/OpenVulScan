using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class AstRuleTests
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

    #region SyntaxNodeContext Tests

    [Fact]
    public void SyntaxNodeContextStoresNodeAndModelAndCompilation()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot();
        var token = new CancellationToken();

        var context = new SyntaxNodeContext(node, model, compilation, token);

        Assert.Same(node, context.Node);
        Assert.Same(model, context.SemanticModel);
        Assert.Same(compilation, context.Compilation);
        Assert.Equal(token, context.CancellationToken);
    }

    [Fact]
    public void SyntaxNodeContextReportDiagnosticAddsToList()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot();

        var context = new SyntaxNodeContext(node, model, compilation, CancellationToken.None);

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("TEST001", "Test", "Message", "Test", DiagnosticSeverity.Warning, true),
            Location.None);

        context.ReportDiagnostic(diagnostic);

        Assert.Single(context.Diagnostics);
        Assert.Same(diagnostic, context.Diagnostics[0]);
    }

    [Fact]
    public void SyntaxNodeContextReportDiagnosticMultipleDiagnostics()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot();

        var context = new SyntaxNodeContext(node, model, compilation, CancellationToken.None);
        var descriptor = new DiagnosticDescriptor("TEST001", "Test", "Message", "Test", DiagnosticSeverity.Warning, true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));

        Assert.Equal(2, context.Diagnostics.Count);
    }

    #endregion

    #region AstRule Tests

    private sealed class DummyIfRule : AstRule
    {
        public List<SyntaxNode> VisitedNodes { get; } = new();

        protected override void OnIfStatement(SyntaxNodeContext context)
        {
            VisitedNodes.Add(context.Node);
        }
    }

    private sealed class DummyMultiRule : AstRule
    {
        public List<SyntaxNode> VisitedNodes { get; } = new();

        protected override void OnIfStatement(SyntaxNodeContext context)
        {
            VisitedNodes.Add(context.Node);
        }

        protected override void OnInvocationExpression(SyntaxNodeContext context)
        {
            VisitedNodes.Add(context.Node);
        }
    }

    private sealed class DummyNoOverrideRule : AstRule
    {
    }

    [Fact]
    public void AstRuleSupportedSyntaxKindsDiscoversIfStatement()
    {
        var rule = new DummyIfRule();
        var kinds = rule.SupportedSyntaxKinds;

        Assert.Single(kinds);
        Assert.Contains(SyntaxKind.IfStatement, kinds);
    }

    [Fact]
    public void AstRuleSupportedSyntaxKindsDiscoversMultipleKinds()
    {
        var rule = new DummyMultiRule();
        var kinds = rule.SupportedSyntaxKinds;

        Assert.Equal(2, kinds.Count);
        Assert.Contains(SyntaxKind.IfStatement, kinds);
        Assert.Contains(SyntaxKind.InvocationExpression, kinds);
    }

    [Fact]
    public void AstRuleSupportedSyntaxKindsEmptyWhenNoOverrides()
    {
        var rule = new DummyNoOverrideRule();
        var kinds = rule.SupportedSyntaxKinds;

        Assert.Empty(kinds);
    }

    [Fact]
    public void AstRuleVisitDispatchesToOnIfStatement()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var ifNode = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().First();

        var rule = new DummyIfRule();
        var context = new SyntaxNodeContext(ifNode, model, compilation, CancellationToken.None);

        rule.Visit(ifNode, context);

        Assert.Single(rule.VisitedNodes);
        Assert.Same(ifNode, rule.VisitedNodes[0]);
    }

    [Fact]
    public void AstRuleVisitThrowsOnUnregisteredKind()
    {
        var source = "class C { void M() { var x = 1 + 2; } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var binaryNode = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().First();

        var rule = new DummyIfRule();
        var context = new SyntaxNodeContext(binaryNode, model, compilation, CancellationToken.None);

        Assert.Throws<ArgumentException>(() => rule.Visit(binaryNode, context));
    }

    [Fact]
    public void AstRuleVisitDispatchesMultipleKinds()
    {
        var source = "class C { void M() { if (true) { M(); } } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var ifNode = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().First();
        var invokeNode = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var rule = new DummyMultiRule();

        rule.Visit(ifNode, new SyntaxNodeContext(ifNode, model, compilation, CancellationToken.None));
        rule.Visit(invokeNode, new SyntaxNodeContext(invokeNode, model, compilation, CancellationToken.None));

        Assert.Equal(2, rule.VisitedNodes.Count);
    }

    #endregion

    #region AstRuleDispatcher Tests

    [Fact]
    public void AstRuleDispatcherWalksTreeAndCallsMatchingRules()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);
        var rule = new DummyIfRule();
        var dispatcher = new AstRuleDispatcher(new[] { rule }, compilation);

        dispatcher.Run(CancellationToken.None);

        Assert.Single(rule.VisitedNodes);
        Assert.IsType<IfStatementSyntax>(rule.VisitedNodes[0]);
    }

    [Fact]
    public void AstRuleDispatcherRespectsCancellationToken()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);
        var rule = new DummyIfRule();
        var dispatcher = new AstRuleDispatcher(new[] { rule }, compilation);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Record.Exception(() => dispatcher.Run(cts.Token));
        Assert.IsType<OperationCanceledException>(ex);
    }

    [Fact]
    public void AstRuleDispatcherReportDiagnosticWorks()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);

        var rule = new DummyDiagnosticRule();
        var dispatcher = new AstRuleDispatcher(new[] { rule }, compilation);

        dispatcher.Run(CancellationToken.None);

        Assert.Single(rule.ReportedDiagnostics);
    }

    private sealed class DummyDiagnosticRule : AstRule
    {
        public List<Diagnostic> ReportedDiagnostics { get; } = new();

        protected override void OnIfStatement(SyntaxNodeContext context)
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor("TEST001", "Test", "Message", "Test", DiagnosticSeverity.Warning, true),
                context.Node.GetLocation());
            context.ReportDiagnostic(diagnostic);
            ReportedDiagnostics.AddRange(context.Diagnostics);
        }
    }

    [Fact]
    public void AstRuleDispatcherNoRulesDoesNotThrow()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var dispatcher = new AstRuleDispatcher(Array.Empty<AstRule>(), compilation);

        dispatcher.Run(CancellationToken.None);
    }

    #endregion
}
