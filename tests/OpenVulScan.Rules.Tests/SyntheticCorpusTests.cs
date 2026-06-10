using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class SyntheticCorpusTests
{
    private static readonly string[] s_quartet = ["V3080", "V3105", "V3153", "V3168"];

    [Fact]
    public async Task QuartetProducesZeroDiagnosticsOnSyntheticCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic.cs");
        var source = await File.ReadAllTextAsync(path);

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SyntheticAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var registry = new RuleRegistry();
        registry.Scan(typeof(DataFlowRulesPlaceholder).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None);

        var falsePositives = diagnostics
            .Where(d => s_quartet.Contains(d.Id))
            .Select(d => $"{d.Id} at {d.Location.GetLineSpan()}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();

        Assert.Empty(falsePositives);
    }
}
