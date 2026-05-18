using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

public static class SuppressMessageAttributeParser
{
    public static IReadOnlyList<SuppressionRange> Parse(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var ranges = new List<SuppressionRange>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetRoot();

            foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                if (!IsSuppressMessageAttribute(attribute))
                {
                    continue;
                }

                if (!TryExtractArguments(attribute, semanticModel, out var category, out var checkId))
                {
                    continue;
                }

                if (!string.Equals(category, "OpenVulScan", StringComparison.Ordinal))
                {
                    continue;
                }

                var ruleCodes = ParseRuleCodes(checkId);
                var (startLine, endLine) = GetTargetLines(attribute);
                var filePath = tree.FilePath;

                ranges.Add(new SuppressionRange(filePath, startLine, endLine, ruleCodes));
            }
        }

        return ranges;
    }

    private static bool IsSuppressMessageAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        return name is "SuppressMessage" or "SuppressMessageAttribute" or "System.Diagnostics.CodeAnalysis.SuppressMessage" or "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute";
    }

    private static bool TryExtractArguments(AttributeSyntax attribute, SemanticModel semanticModel, out string category, out string checkId)
    {
        category = string.Empty;
        checkId = string.Empty;

        var argumentList = attribute.ArgumentList;
        if (argumentList == null || argumentList.Arguments.Count < 2)
        {
            return false;
        }

        var cat = GetConstantStringValue(argumentList.Arguments[0], semanticModel);
        var chk = GetConstantStringValue(argumentList.Arguments[1], semanticModel);

        if (cat == null)
        {
            return false;
        }

        category = cat;
        checkId = chk ?? string.Empty;

        return true;
    }

    private static string? GetConstantStringValue(AttributeArgumentSyntax argument, SemanticModel semanticModel)
    {
        var expression = argument.Expression;
        if (expression == null)
        {
            return null;
        }

        var constantValue = semanticModel.GetConstantValue(expression);
        if (constantValue.HasValue && constantValue.Value is string strValue)
        {
            return strValue;
        }

        // Fallback: try to get the literal text directly
        if (expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.ValueText;
        }

        return null;
    }

    private static HashSet<string> ParseRuleCodes(string checkId)
    {
        if (string.IsNullOrEmpty(checkId))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in checkId.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                codes.Add(trimmed);
            }
        }

        return codes;
    }

    private static (int StartLine, int EndLine) GetTargetLines(AttributeSyntax attribute)
    {
        var parent = attribute.Parent;

        // Attribute is inside an AttributeListSyntax
        if (parent is AttributeListSyntax attributeList)
        {
            parent = attributeList.Parent;
        }

        if (parent == null)
        {
            var location = attribute.GetLocation().GetLineSpan();
            return (location.StartLinePosition.Line, location.EndLinePosition.Line);
        }

        var parentLocation = parent.GetLocation().GetLineSpan();
        return (parentLocation.StartLinePosition.Line, parentLocation.EndLinePosition.Line);
    }
}
