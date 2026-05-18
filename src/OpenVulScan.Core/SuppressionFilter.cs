using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public static class SuppressionFilter
{
    public static IReadOnlyList<Diagnostic> Apply(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<SuppressionRange> suppressions)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(suppressions);

        if (suppressions.Count == 0)
        {
            return diagnostics;
        }

        return diagnostics.Where(d => !IsSuppressed(d, suppressions)).ToList();
    }

    private static bool IsSuppressed(Diagnostic diagnostic, IReadOnlyList<SuppressionRange> suppressions)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return false;
        }

        var lineSpan = diagnostic.Location.GetLineSpan();
        var path = lineSpan.Path;
        var line = lineSpan.StartLinePosition.Line;

        foreach (var suppression in suppressions)
        {
            if (!PathsEqual(path, suppression.FilePath))
            {
                continue;
            }

            if (line < suppression.StartLine || line > suppression.EndLine)
            {
                continue;
            }

            if (suppression.RuleCodes.Count > 0 && !suppression.RuleCodes.Contains(diagnostic.Id))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool PathsEqual(string a, string b)
    {
        return a.Replace("\\", "/", StringComparison.Ordinal) == b.Replace("\\", "/", StringComparison.Ordinal);
    }
}
