using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

internal sealed class AnalyzeCommandHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new RuleDescriptorJsonConverter() },
    };

    private readonly ProjectLoader _loader;

    public AnalyzeCommandHandler()
    {
        _loader = new ProjectLoader();
    }

    public async Task<int> ExecuteAsync(AnalyzeOptions options, Stream outputStream, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<LoadedProject> projects;
            if (options.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                options.Path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await _loader.LoadSolutionAsync(options.Path, cancellationToken).ConfigureAwait(false);
                projects = solution.Projects;
            }
            else if (options.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await _loader.LoadProjectAsync(options.Path, cancellationToken).ConfigureAwait(false);
                projects = [project];
            }
            else
            {
                await WriteErrorAsync(outputStream, $"Unsupported file type: {options.Path}. Expected .sln, .slnx, or .csproj.").ConfigureAwait(false);
                return 2;
            }

            var registry = CreateRuleRegistry();
            var scheduler = new RuleScheduler(registry, _ => { });
            var allDiagnostics = new List<Diagnostic>();

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var diagnostics = await scheduler.AnalyzeAsync(project.Compilation, cancellationToken).ConfigureAwait(false);
                allDiagnostics.AddRange(diagnostics);
            }

            var filteredDiagnostics = ApplyFilters(allDiagnostics, options.Include, options.Exclude);

            if (!string.IsNullOrEmpty(options.Suppress))
            {
                await WriteWarningAsync(outputStream, "Suppress option is provided but suppression logic is not yet implemented.").ConfigureAwait(false);
            }

            await WriteOutputAsync(filteredDiagnostics, registry.GetAll(), options.Format, outputStream, cancellationToken).ConfigureAwait(false);

            var hasErrors = filteredDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            return hasErrors ? 1 : 0;
        }
        catch (MissingSdkException ex)
        {
            await WriteErrorAsync(outputStream, ex.Message).ConfigureAwait(false);
            return 2;
        }
        catch (MissingReferenceException ex)
        {
            await WriteErrorAsync(outputStream, ex.Message).ConfigureAwait(false);
            return 2;
        }
        catch (ProjectLoadException ex)
        {
            await WriteErrorAsync(outputStream, ex.Message).ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            await WriteErrorAsync(outputStream, $"Unexpected error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
#pragma warning restore CA1031
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

    private static async Task WriteOutputAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<RuleDescriptor> rules,
        string format,
        Stream outputStream,
        CancellationToken cancellationToken)
    {
        if (string.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase))
        {
            WriteSarif(diagnostics, rules, outputStream);
        }
        else if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(diagnostics, rules, outputStream, cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(diagnostics, outputStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new NotSupportedException($"Unsupported format: {format}");
        }
    }

    private static void WriteSarif(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<RuleDescriptor> rules,
        Stream outputStream)
    {
        var writer = new SarifWriter(
            "OpenVulScan",
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        writer.Write(diagnostics, rules, outputStream);
    }

    private static async Task WriteJsonAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<RuleDescriptor> rules,
        Stream outputStream,
        CancellationToken cancellationToken)
    {
        var jsonDiagnostics = diagnostics.Select(d => new
        {
            Id = d.Id,
            Severity = d.Severity.ToString(),
            Message = d.GetMessage(CultureInfo.InvariantCulture),
            Location = d.Location.IsInSource
                ? new
                {
                    Path = d.Location.GetLineSpan().Path,
                    Line = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                    Column = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                }
                : null,
        }).ToList();

        var output = new
        {
            Rules = rules,
            Diagnostics = jsonDiagnostics,
        };

        await JsonSerializer.SerializeAsync(outputStream, output, s_jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteTextAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        Stream outputStream,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(outputStream, leaveOpen: true);
        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var severity = diagnostic.Severity.ToString().ToUpperInvariant();
            var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            if (diagnostic.Location.IsInSource)
            {
                var lineSpan = diagnostic.Location.GetLineSpan();
                await writer.WriteLineAsync(
                    $"{lineSpan.Path}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1}): [{severity}] {diagnostic.Id}: {message}")
                    .ConfigureAwait(false);
            }
            else
            {
                await writer.WriteLineAsync(
                    $"[{severity}] {diagnostic.Id}: {message}")
                    .ConfigureAwait(false);
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(Stream outputStream, string message)
    {
        using var writer = new StreamWriter(outputStream, leaveOpen: true);
        await writer.WriteLineAsync($"Error: {message}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WriteWarningAsync(Stream outputStream, string message)
    {
        using var writer = new StreamWriter(outputStream, leaveOpen: true);
        await writer.WriteLineAsync($"Warning: {message}").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
