using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyTests;

namespace OpenVulScan.Tests;

internal static class SnapshotTestHarness
{
    public static async Task RunRuleSnapshotAsync(
        string ruleCode,
        string testCaseName,
        string sourceCode,
        [CallerFilePath] string sourceFile = "")
    {
        var compilation = CreateTestCompilation(sourceCode);

        var registry = new RuleRegistry();
        registry.Scan(typeof(AstRulesPlaceholder).Assembly);
        registry.Scan(typeof(DataFlowRulesPlaceholder).Assembly);

        var scheduler = new RuleScheduler(registry);
        var diagnostics = await scheduler.AnalyzeAsync(compilation, CancellationToken.None).ConfigureAwait(false);

        var filteredDiagnostics = diagnostics
            .Where(d => d.Id == ruleCode)
            .OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Character)
            .ToList();

        var snapshot = new
        {
            RuleCode = ruleCode,
            TestCase = testCaseName,
            Source = sourceCode,
            Diagnostics = filteredDiagnostics.Select(d => new
            {
                Id = d.Id,
                Message = d.GetMessage(CultureInfo.InvariantCulture),
                Severity = d.Severity.ToString(),
                Location = d.Location.IsInSource
                    ? new
                    {
                        StartLine = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                        StartColumn = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                        EndLine = d.Location.GetLineSpan().EndLinePosition.Line + 1,
                        EndColumn = d.Location.GetLineSpan().EndLinePosition.Character + 1
                    }
                    : null
            }).ToList()
        };

        var settings = new VerifySettings();
        settings.UseFileName($"{ruleCode}.{testCaseName}");

        await Verifier.Verify(snapshot, settings, sourceFile);
    }

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
}
