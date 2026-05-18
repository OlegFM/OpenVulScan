using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public static class JsonEmitter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new RuleDescriptorJsonConverter() },
    };

    public static async Task WriteAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<AnalysisFail> fails,
        IReadOnlyList<RuleDescriptor> rules,
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(fails);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(outputStream);

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
}
