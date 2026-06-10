using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class SchedulerDataFlowTests
{
    [Fact]
    public async Task SchedulerRunsDataFlowRules()
    {
        var tree = CSharpSyntaxTree.ParseText(@"
class C { void M() { if (true) { } } }");
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var registry = new RuleRegistry();
        registry.Scan(typeof(DataFlowRulesPlaceholder).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        Assert.Contains(diagnostics, d => d.Id == "V3022");
    }
}
