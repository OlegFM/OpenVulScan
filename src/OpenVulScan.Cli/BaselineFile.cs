using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

internal sealed record BaselineEntry(
    string RuleCode,
    string FilePath,
    int Line,
    int Column,
    string Fingerprint);

internal static class BaselineFile
{
    private const string Header = "# OpenVulScan suppression baseline";

    public static IReadOnlyList<BaselineEntry> Read(string path)
    {
        var entries = new List<BaselineEntry>();
        if (!File.Exists(path))
        {
            return entries;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 4)
            {
                continue;
            }

            var ruleCode = parts[0];
            var filePath = parts[1];
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNum))
            {
                continue;
            }
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnNum))
            {
                continue;
            }
            var fingerprint = parts.Length > 4 ? parts[4] : string.Empty;

            entries.Add(new BaselineEntry(ruleCode, filePath, lineNum, columnNum, fingerprint));
        }

        return entries;
    }

    public static void Write(string path, IReadOnlyList<BaselineEntry> entries)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(Header);
        writer.WriteLine($"# Generated: {DateTimeOffset.UtcNow:O}");
        writer.WriteLine();

        foreach (var entry in entries
            .OrderBy(e => e.RuleCode)
            .ThenBy(e => e.FilePath)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.Column))
        {
            writer.WriteLine($"{entry.RuleCode}\t{entry.FilePath}\t{entry.Line}\t{entry.Column}\t{entry.Fingerprint}");
        }
    }

    public static string ComputeFingerprint(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!diagnostic.Location.IsInSource)
        {
            return string.Empty;
        }

        var lineSpan = diagnostic.Location.GetLineSpan();
        var sourceText = diagnostic.Location.SourceTree?.GetText();
        if (sourceText is null)
        {
            return string.Empty;
        }

        var line = lineSpan.StartLinePosition.Line;
        if (line < 0 || line >= sourceText.Lines.Count)
        {
            return string.Empty;
        }

        var lineContent = sourceText.Lines[line].ToString();
        var normalized = Regex.Replace(lineContent, "\\s+", " ").Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..8];
    }
}
