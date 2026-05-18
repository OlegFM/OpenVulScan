using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

internal static class AnalysisRunner
{
    public static Task<(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<AnalysisFail> Fails, RuleRegistry Registry)> RunAnalysisAsync(
        string path,
        IReadOnlyList<string>? includePatterns,
        IReadOnlyList<string>? excludePatterns,
        CancellationToken cancellationToken)
    {
        return RunAnalysisAsync(path, includePatterns, excludePatterns, null, cancellationToken);
    }

    public static async Task<(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<AnalysisFail> Fails, RuleRegistry Registry)> RunAnalysisAsync(
        string path,
        IReadOnlyList<string>? includePatterns,
        IReadOnlyList<string>? excludePatterns,
        string? baselinePath,
        CancellationToken cancellationToken)
    {
        var loader = new ProjectLoader();

        IReadOnlyList<LoadedProject> projects;
        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            var solution = await loader.LoadSolutionAsync(path, cancellationToken).ConfigureAwait(false);
            projects = solution.Projects;
        }
        else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await loader.LoadProjectAsync(path, cancellationToken).ConfigureAwait(false);
            projects = [project];
        }
        else
        {
            throw new ProjectLoadException($"Unsupported file type: {path}. Expected .sln, .slnx, or .csproj.");
        }

        var registry = CreateRuleRegistry();
        var scheduler = new RuleScheduler(registry, _ => { });
        var allDiagnostics = new List<Diagnostic>();
        var allSuppressions = new List<SuppressionRange>();
        var allFails = new List<AnalysisFail>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnostics = await scheduler.AnalyzeAsync(project.Compilation, cancellationToken).ConfigureAwait(false);
            allDiagnostics.AddRange(diagnostics);

            foreach (var tree in project.Compilation.SyntaxTrees)
            {
                allSuppressions.AddRange(InlineSuppressionParser.Parse(tree));
            }

            allFails.AddRange(FailDetector.Detect(project));
        }

        var afterSuppressions = SuppressionFilter.Apply(allDiagnostics, allSuppressions);

        if (!string.IsNullOrEmpty(baselinePath))
        {
            var baseline = BaselineFile.Read(baselinePath);
            afterSuppressions = BaselineFilter.Apply(afterSuppressions, baseline);
        }

        var filtered = ApplyFilters(afterSuppressions, includePatterns, excludePatterns);
        return (filtered, allFails, registry);
    }

    private static RuleRegistry CreateRuleRegistry()
    {
        var registry = new RuleRegistry();
        var baseDir = AppContext.BaseDirectory;
        var ruleDlls = Directory.GetFiles(baseDir, "OpenVulScan.Rules.*.dll");

        foreach (var dll in ruleDlls)
        {
#pragma warning disable CA1031
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                registry.Scan(assembly);
            }
            catch
            {
                // Ignore unloadable assemblies
            }
#pragma warning restore CA1031
        }

        return registry;
    }

    private static IReadOnlyList<Diagnostic> ApplyFilters(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string>? includePatterns,
        IReadOnlyList<string>? excludePatterns)
    {
        if ((includePatterns == null || includePatterns.Count == 0) &&
            (excludePatterns == null || excludePatterns.Count == 0))
        {
            return diagnostics;
        }

        return diagnostics.Where(d => ShouldInclude(d, includePatterns, excludePatterns)).ToList();
    }

    private static bool ShouldInclude(Diagnostic diagnostic, IReadOnlyList<string>? includePatterns, IReadOnlyList<string>? excludePatterns)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return true;
        }

        var path = diagnostic.Location.GetLineSpan().Path;
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        if (excludePatterns != null)
        {
            foreach (var pattern in excludePatterns)
            {
                if (MatchesGlob(path, pattern))
                {
                    return false;
                }
            }
        }

        if (includePatterns != null && includePatterns.Count > 0)
        {
            foreach (var pattern in includePatterns)
            {
                if (MatchesGlob(path, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static bool MatchesGlob(string path, string pattern)
    {
        var normalizedPath = path.Replace("\\", "/", StringComparison.Ordinal);
        var normalizedPattern = pattern.Replace("\\", "/", StringComparison.Ordinal);

        var regexPattern = "^" + Regex.Escape(normalizedPattern)
            .Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(
            normalizedPath,
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }
}
