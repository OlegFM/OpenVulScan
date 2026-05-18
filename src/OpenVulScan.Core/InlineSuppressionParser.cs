using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenVulScan;

public static class InlineSuppressionParser
{
    public static IReadOnlyList<SuppressionRange> Parse(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var root = tree.GetRoot();
        var filePath = tree.FilePath;
        var lastLine = tree.GetText().Lines.Count - 1;

        var ranges = new List<SuppressionRange>();
        var openBlocks = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var trivia in root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                continue;
            }

            var commentText = trivia.ToString();
            if (!TryParseMarker(commentText, out var command, out var rules))
            {
                continue;
            }

            var line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line;

            switch (command)
            {
                case "disable":
                    ranges.Add(new SuppressionRange(filePath, line, line, rules));
                    break;

                case "disable-next-line":
                    ranges.Add(new SuppressionRange(filePath, line + 1, line + 1, rules));
                    break;

                case "disable-block":
                    var disableKey = GetBlockKey(rules);
                    if (!openBlocks.ContainsKey(disableKey))
                    {
                        openBlocks[disableKey] = line;
                    }
                    break;

                case "enable-block":
                    var enableKey = GetBlockKey(rules);
                    if (openBlocks.TryGetValue(enableKey, out var startLine))
                    {
                        ranges.Add(new SuppressionRange(filePath, startLine, line, rules));
                        openBlocks.Remove(enableKey);
                    }
                    break;
            }
        }

        foreach (var kvp in openBlocks)
        {
            var rules = ParseBlockKey(kvp.Key);
            ranges.Add(new SuppressionRange(filePath, kvp.Value, lastLine, rules));
        }

        return ranges;
    }

    private static bool TryParseMarker(string commentText, out string command, out IReadOnlySet<string> rules)
    {
        command = string.Empty;
        rules = new HashSet<string>(StringComparer.Ordinal);

        var text = commentText.TrimStart();
        if (!text.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        text = text[2..].TrimStart();
        if (!text.StartsWith("ovs:", StringComparison.Ordinal))
        {
            return false;
        }

        text = text[4..];
        var spaceIndex = text.IndexOf(' ', StringComparison.Ordinal);
        string commandStr;
        string rulesStr;

        if (spaceIndex >= 0)
        {
            commandStr = text[..spaceIndex];
            rulesStr = text[(spaceIndex + 1)..].TrimStart();
        }
        else
        {
            commandStr = text;
            rulesStr = string.Empty;
        }

        if (commandStr != "disable" &&
            commandStr != "disable-next-line" &&
            commandStr != "disable-block" &&
            commandStr != "enable-block")
        {
            return false;
        }

        command = commandStr;

        if (!string.IsNullOrEmpty(rulesStr))
        {
            var ruleSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in rulesStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    ruleSet.Add(trimmed);
                }
            }

            rules = ruleSet;
        }

        return true;
    }

    private static string GetBlockKey(IReadOnlySet<string> rules)
    {
        if (rules.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", rules.Order(StringComparer.Ordinal));
    }

    private static HashSet<string> ParseBlockKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>(key.Split(','), StringComparer.Ordinal);
    }
}
