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
            var (filteredDiagnostics, registry) = await AnalysisRunner.RunAnalysisAsync(
                options.Path,
                options.Include,
                options.Exclude,
                cancellationToken).ConfigureAwait(false);

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
