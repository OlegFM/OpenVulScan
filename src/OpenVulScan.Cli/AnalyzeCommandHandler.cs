using System.Globalization;
using System.Text.Json;
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

    public static async Task<int> ExecuteAsync(AnalyzeOptions options, Stream outputStream, CancellationToken cancellationToken)
    {
        try
        {
            var (filteredDiagnostics, fails, registry) = await AnalysisRunner.RunAnalysisAsync(
                options.Path,
                options.Include,
                options.Exclude,
                options.Suppress,
                cancellationToken).ConfigureAwait(false);

            await WriteOutputAsync(filteredDiagnostics, fails, registry.GetAll(), options.Format, outputStream, cancellationToken).ConfigureAwait(false);

            var hasErrors = filteredDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            var hasFails = fails.Count > 0;
            return hasErrors || hasFails ? 1 : 0;
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

    private static async Task WriteOutputAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<AnalysisFail> fails,
        IReadOnlyList<RuleDescriptor> rules,
        string format,
        Stream outputStream,
        CancellationToken cancellationToken)
    {
        if (string.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase))
        {
            WriteSarif(diagnostics, fails, rules, outputStream);
        }
        else if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(diagnostics, fails, rules, outputStream, cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(diagnostics, fails, outputStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new NotSupportedException($"Unsupported format: {format}");
        }
    }

    private static void WriteSarif(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<AnalysisFail> fails,
        IReadOnlyList<RuleDescriptor> rules,
        Stream outputStream)
    {
        var writer = new SarifWriter(
            "OpenVulScan",
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        writer.Write(diagnostics, fails, rules, outputStream);
    }

    private static async Task WriteJsonAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<AnalysisFail> fails,
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

        var jsonFails = fails.Select(f => new
        {
            f.Code,
            f.Message,
            f.FilePath,
            f.Line,
        }).ToList();

        var output = new
        {
            Rules = rules,
            Diagnostics = jsonDiagnostics,
            Fails = jsonFails,
        };

        await JsonSerializer.SerializeAsync(outputStream, output, s_jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteTextAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<AnalysisFail> fails,
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

        foreach (var fail in fails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fail.FilePath != null && fail.Line.HasValue)
            {
                await writer.WriteLineAsync(
                    $"{fail.FilePath}({fail.Line.Value},1): [FAIL] {fail.Code}: {fail.Message}")
                    .ConfigureAwait(false);
            }
            else if (fail.FilePath != null)
            {
                await writer.WriteLineAsync(
                    $"{fail.FilePath}: [FAIL] {fail.Code}: {fail.Message}")
                    .ConfigureAwait(false);
            }
            else
            {
                await writer.WriteLineAsync(
                    $"[FAIL] {fail.Code}: {fail.Message}")
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
