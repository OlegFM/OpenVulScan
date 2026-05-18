using Microsoft.CodeAnalysis;

namespace OpenVulScan;

internal sealed class AnalyzeCommandHandler
{
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
            await JsonEmitter.WriteAsync(diagnostics, fails, rules, outputStream, cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
        {
            await TextEmitter.WriteAsync(diagnostics, fails, outputStream, cancellationToken).ConfigureAwait(false);
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
