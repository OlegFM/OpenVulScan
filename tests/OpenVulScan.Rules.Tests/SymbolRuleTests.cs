using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class SymbolRuleTests
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

    #region SymbolContext Tests

    [Fact]
    public void SymbolContextStoresSymbolAndModelAndCompilation()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var symbol = model.GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First());
        Assert.NotNull(symbol);
        var token = new CancellationToken();

        var context = new SymbolContext(symbol!, model, compilation, token);

        Assert.Same(symbol, context.Symbol);
        Assert.Same(model, context.SemanticModel);
        Assert.Same(compilation, context.Compilation);
        Assert.Equal(token, context.CancellationToken);
    }

    [Fact]
    public void SymbolContextReportDiagnosticAddsToList()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var symbol = model.GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First());
        Assert.NotNull(symbol);

        var context = new SymbolContext(symbol!, model, compilation, CancellationToken.None);
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("TEST001", "Test", "Message", "Test", DiagnosticSeverity.Warning, true),
            Location.None);

        context.ReportDiagnostic(diagnostic);

        Assert.Single(context.Diagnostics);
        Assert.Same(diagnostic, context.Diagnostics[0]);
    }

    #endregion

    #region SymbolRule Tests

    private sealed class DummyMethodRule : SymbolRule
    {
        public List<ISymbol> VisitedSymbols { get; } = new();

        protected override void VisitMethod(IMethodSymbol symbol, SymbolContext context)
        {
            VisitedSymbols.Add(symbol);
        }
    }

    private sealed class DummyClassRule : SymbolRule
    {
        public List<ISymbol> VisitedSymbols { get; } = new();

        protected override void VisitClass(INamedTypeSymbol symbol, SymbolContext context)
        {
            VisitedSymbols.Add(symbol);
        }
    }

    private sealed class DummyMultiSymbolRule : SymbolRule
    {
        public List<ISymbol> VisitedSymbols { get; } = new();

        protected override void VisitMethod(IMethodSymbol symbol, SymbolContext context)
        {
            VisitedSymbols.Add(symbol);
        }

        protected override void VisitProperty(IPropertySymbol symbol, SymbolContext context)
        {
            VisitedSymbols.Add(symbol);
        }
    }

    private sealed class DummyNoOverrideSymbolRule : SymbolRule
    {
        public List<ISymbol> VisitedSymbols { get; } = new();
    }

    [Fact]
    public void SymbolRuleVisitDispatchesToVisitMethod()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var symbol = model.GetDeclaredSymbol(method);
        Assert.NotNull(symbol);

        var rule = new DummyMethodRule();
        var context = new SymbolContext(symbol!, model, compilation, CancellationToken.None);

        rule.Visit(symbol!, context);

        Assert.Single(rule.VisitedSymbols);
        Assert.Same(symbol, rule.VisitedSymbols[0]);
    }

    [Fact]
    public void SymbolRuleVisitDispatchesToVisitClass()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var classDecl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var symbol = model.GetDeclaredSymbol(classDecl);
        Assert.NotNull(symbol);

        var rule = new DummyClassRule();
        var context = new SymbolContext(symbol!, model, compilation, CancellationToken.None);

        rule.Visit(symbol!, context);

        Assert.Single(rule.VisitedSymbols);
        Assert.Same(symbol, rule.VisitedSymbols[0]);
    }

    [Fact]
    public void SymbolRuleVisitDoesNotDispatchToUnregisteredKind()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var classDecl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var symbol = model.GetDeclaredSymbol(classDecl);
        Assert.NotNull(symbol);

        var rule = new DummyMethodRule();
        var context = new SymbolContext(symbol!, model, compilation, CancellationToken.None);

        rule.Visit(symbol!, context);

        Assert.Empty(rule.VisitedSymbols);
    }

    [Fact]
    public void SymbolRuleVisitDispatchesMultipleKinds()
    {
        var source = "class C { int P { get; set; } void M() { } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var methodSymbol = model.GetDeclaredSymbol(method);
        Assert.NotNull(methodSymbol);
        var property = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().First();
        var propertySymbol = model.GetDeclaredSymbol(property);
        Assert.NotNull(propertySymbol);

        var rule = new DummyMultiSymbolRule();

        rule.Visit(methodSymbol!, new SymbolContext(methodSymbol!, model, compilation, CancellationToken.None));
        rule.Visit(propertySymbol!, new SymbolContext(propertySymbol!, model, compilation, CancellationToken.None));

        Assert.Equal(2, rule.VisitedSymbols.Count);
    }

    [Fact]
    public void SymbolRuleVisitNoOverrideDoesNothing()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var symbol = model.GetDeclaredSymbol(method);
        Assert.NotNull(symbol);

        var rule = new DummyNoOverrideSymbolRule();
        var context = new SymbolContext(symbol!, model, compilation, CancellationToken.None);

        rule.Visit(symbol!, context);

        Assert.Empty(rule.VisitedSymbols);
    }

    #endregion

    #region SymbolRuleDispatcher Tests

    [Fact]
    public void SymbolRuleDispatcherIteratesSymbolsAndCallsMatchingRules()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var rule = new DummyMethodRule();
        var dispatcher = new SymbolRuleDispatcher(new[] { rule }, compilation);

        dispatcher.Run(CancellationToken.None);

        Assert.Equal(2, rule.VisitedSymbols.Count);
        Assert.All(rule.VisitedSymbols, s => Assert.IsAssignableFrom<IMethodSymbol>(s));
    }

    [Fact]
    public void SymbolRuleDispatcherRespectsCancellationToken()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);
        var rule = new DummyMethodRule();
        var dispatcher = new SymbolRuleDispatcher(new[] { rule }, compilation);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Record.Exception(() => dispatcher.Run(cts.Token));
        Assert.IsType<OperationCanceledException>(ex);
    }

    [Fact]
    public void SymbolRuleDispatcherReportDiagnosticWorks()
    {
        var source = "class C { void M() { } }";
        var compilation = CreateTestCompilation(source);

        var rule = new DummySymbolDiagnosticRule();
        var dispatcher = new SymbolRuleDispatcher(new[] { rule }, compilation);

        dispatcher.Run(CancellationToken.None);

        Assert.Equal(2, rule.ReportedDiagnostics.Count);
    }

    private sealed class DummySymbolDiagnosticRule : SymbolRule
    {
        public List<Diagnostic> ReportedDiagnostics { get; } = new();

        protected override void VisitMethod(IMethodSymbol symbol, SymbolContext context)
        {
            var diagnostic = Diagnostic.Create(
                new DiagnosticDescriptor("TEST001", "Test", "Message", "Test", DiagnosticSeverity.Warning, true),
                symbol.Locations.FirstOrDefault() ?? Location.None);
            context.ReportDiagnostic(diagnostic);
            ReportedDiagnostics.AddRange(context.Diagnostics);
        }
    }

    [Fact]
    public void SymbolRuleDispatcherNoRulesDoesNotThrow()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var dispatcher = new SymbolRuleDispatcher(Array.Empty<SymbolRule>(), compilation);

        dispatcher.Run(CancellationToken.None);
    }

    #endregion
}
