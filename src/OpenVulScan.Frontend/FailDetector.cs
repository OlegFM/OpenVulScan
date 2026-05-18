using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace OpenVulScan;

public static class FailDetector
{
    private static readonly HashSet<string> TypeErrorCodes = new(StringComparer.Ordinal)
    {
        "CS0234",
        "CS0246",
        "CS0012",
        "CS0103",
        "CS1061",
        "CS0117",
        "CS0305",
        "CS0311",
    };

    public static IReadOnlyList<AnalysisFail> Detect(LoadedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var fails = new List<AnalysisFail>();

        DetectMissingReferences(project, fails);
        DetectWorkspaceFailures(project, fails);
        DetectTypeFailures(project, fails);

        return fails;
    }

    private static void DetectMissingReferences(LoadedProject project, List<AnalysisFail> fails)
    {
        foreach (var diagnostic in project.WorkspaceDiagnostics)
        {
            if (diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
                IsMissingReferenceMessage(diagnostic.Message))
            {
                fails.Add(new AnalysisFail(
                    "V051",
                    $"Missing reference when loading '{project.FilePath}': {diagnostic.Message}",
                    project.FilePath));
            }
        }
    }

    private static void DetectWorkspaceFailures(LoadedProject project, List<AnalysisFail> fails)
    {
        foreach (var diagnostic in project.WorkspaceDiagnostics)
        {
            if (diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
                !IsMissingReferenceMessage(diagnostic.Message))
            {
                fails.Add(new AnalysisFail(
                    "V052",
                    $"Workspace failure when loading '{project.FilePath}': {diagnostic.Message}",
                    project.FilePath));
            }
        }
    }

    private static void DetectTypeFailures(LoadedProject project, List<AnalysisFail> fails)
    {
        foreach (var diagnostic in project.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error &&
                TypeErrorCodes.Contains(diagnostic.Id))
            {
                var location = diagnostic.Location.IsInSource
                    ? diagnostic.Location.GetLineSpan()
                    : (FileLinePositionSpan?)null;

                fails.Add(new AnalysisFail(
                    "V053",
                    $"Type resolution failed in '{project.FilePath}': {diagnostic.GetMessage(CultureInfo.InvariantCulture)}",
                    location?.Path ?? project.FilePath,
                    location != null ? location.Value.StartLinePosition.Line + 1 : null));
            }
        }
    }

    private static bool IsMissingReferenceMessage(string message)
    {
        var isReferenceOrProject =
            message.Contains("reference", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("project", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ссылка", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("проект", StringComparison.OrdinalIgnoreCase);

        if (!isReferenceOrProject)
        {
            return false;
        }

        return message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("не найден", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("не существует", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("не удалось", StringComparison.OrdinalIgnoreCase);
    }
}
