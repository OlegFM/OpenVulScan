#pragma warning disable CA1812 // Avoid uninstantiated internal classes - these are discovered via reflection
#pragma warning disable CA1852 // Seal internal types

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class RuleSchedulerTests
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

    [Fact]
    public async Task AnalyzeAsyncWithDummyAstRuleReportsDiagnostic()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);
        var registry = new RuleRegistry();
        registry.Scan(typeof(RuleSchedulerTests).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostics[0].Id);
    }

    [Fact]
    public async Task AnalyzeAsyncWithNoRulesReturnsEmptyDiagnostics()
    {
        var source = "class C { }";
        var compilation = CreateTestCompilation(source);
        var registry = new RuleRegistry();

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AnalyzeAsyncWithBrokenRuleSkipsAndDoesNotCrash()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);
        var registry = new RuleRegistry();
        registry.Scan(typeof(RuleSchedulerTests).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        // Broken rule is skipped; dummy rule should still produce its diagnostic.
        Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostics[0].Id);
    }

    [Fact]
    public async Task AnalyzeAsyncCancellationTokenIsRespected()
    {
        var source = "class C { void M() { if (true) { } } }";
        var compilation = CreateTestCompilation(source);
        var registry = new RuleRegistry();
        registry.Scan(typeof(RuleSchedulerTests).Assembly);

        var scheduler = new RuleScheduler(registry);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => scheduler.AnalyzeAsync(compilation, cts.Token));
    }

    #region Dummy Rules

    [Rule("TEST001", RuleSeverity.Level1, "CWE-000", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
    internal class DummyIfDetectionRule : AstRule
    {
        protected override void OnIfStatement(SyntaxNodeContext context)
        {
            if (context.Node is IfStatementSyntax ifStmt
                && ifStmt.Statement is BlockSyntax block
                && block.Statements.Count == 0)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "TEST001",
                        "Empty if body",
                        "If statement has empty body",
                        "Test",
                        DiagnosticSeverity.Warning,
                        true),
                    ifStmt.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    [Rule("BROKEN01", RuleSeverity.Level1, "CWE-000", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
    internal class BrokenInstantiationRule : AstRule
    {
        public BrokenInstantiationRule()
        {
            throw new InvalidOperationException("I am broken");
        }

        protected override void OnIfStatement(SyntaxNodeContext context)
        {
        }
    }

    #endregion
}
