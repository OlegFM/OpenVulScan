using System.Globalization;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public static class TextEmitter
{
    public static async Task WriteAsync(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<AnalysisFail> fails,
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(fails);
        ArgumentNullException.ThrowIfNull(outputStream);

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
}
