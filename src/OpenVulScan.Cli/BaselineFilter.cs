using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

internal static class BaselineFilter
{
    public static IReadOnlyList<Diagnostic> Apply(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<BaselineEntry> baseline,
        int fuzzyWindow = 5)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(baseline);

        if (baseline.Count == 0)
        {
            return diagnostics;
        }

        return diagnostics.Where(d => !IsSuppressed(d, baseline, fuzzyWindow)).ToList();
    }

    private static bool IsSuppressed(Diagnostic diagnostic, IReadOnlyList<BaselineEntry> baseline, int fuzzyWindow)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return false;
        }

        var lineSpan = diagnostic.Location.GetLineSpan();
        var path = lineSpan.Path;
        var line = lineSpan.StartLinePosition.Line + 1; // 1-based
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostic);

        foreach (var entry in baseline)
        {
            if (!string.Equals(entry.RuleCode, diagnostic.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (!PathsEqual(entry.FilePath, path))
            {
                continue;
            }

            if (line < entry.Line - fuzzyWindow || line > entry.Line + fuzzyWindow)
            {
                continue;
            }

            if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool PathsEqual(string a, string? b)
    {
        if (b is null)
        {
            return false;
        }

        var normA = a.Replace("\\", "/", StringComparison.Ordinal);
        var normB = b.Replace("\\", "/", StringComparison.Ordinal);

        return normA.Equals(normB, StringComparison.OrdinalIgnoreCase)
            || normA.EndsWith("/" + normB, StringComparison.OrdinalIgnoreCase)
            || normB.EndsWith("/" + normA, StringComparison.OrdinalIgnoreCase);
    }
}
