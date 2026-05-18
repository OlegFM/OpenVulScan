using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3025", RuleSeverity.Level1, "CWE-628", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3025FormatMismatch : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3025",
        "Format string argument count mismatch",
        "The format string contains {0} placeholder(s) but {1} argument(s) were passed",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnInvocationExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        var firstArg = invocation.ArgumentList.Arguments[0];
        if (firstArg.Expression is not LiteralExpressionSyntax literal || !literal.Token.IsKind(SyntaxKind.StringLiteralToken))
        {
            return;
        }

        if (!IsFormatMethod(invocation, context.SemanticModel))
        {
            return;
        }

        var formatContent = GetLiteralContent(literal);
        var placeholderCount = CountPlaceholders(formatContent);
        var argumentCount = invocation.ArgumentList.Arguments.Count - 1;

        if (placeholderCount != argumentCount)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                literal.GetLocation(),
                placeholderCount,
                argumentCount));
        }
    }

    private static bool IsFormatMethod(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.ValueText;

            // string.Format or String.Format
            if (memberName == "Format")
            {
                if (memberAccess.Expression is PredefinedTypeSyntax predefined &&
                    predefined.Keyword.IsKind(SyntaxKind.StringKeyword))
                {
                    return true;
                }

                if (memberAccess.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "String")
                {
                    return true;
                }
            }

            // Console.WriteLine or Console.Write
            if (memberName is "WriteLine" or "Write")
            {
                if (memberAccess.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "Console")
                {
                    return true;
                }
            }
        }

        // Fall back to semantic analysis for generic detection
        return IsFormatMethodBySemantic(invocation, semanticModel);
    }

    private static bool IsFormatMethodBySemantic(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        try
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol method)
            {
                return false;
            }

            if (method.Parameters.Length == 0)
            {
                return false;
            }

            var firstParam = method.Parameters[0];
            return firstParam.Name == "format" && firstParam.Type.SpecialType == SpecialType.System_String;
        }
#pragma warning disable CA1031
        catch
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static ReadOnlySpan<char> GetLiteralContent(LiteralExpressionSyntax literal)
    {
        var text = literal.Token.Text.AsSpan();

        // Raw string literal: """..."""
        if (text.StartsWith("\"\"\""))
        {
            var end = text.LastIndexOf("\"\"\"".AsSpan());
            if (end > 3)
            {
                return text.Slice(3, end - 3);
            }

            return text;
        }

        // Verbatim string: @"..."
        if (text.StartsWith("@\""))
        {
            return text.Slice(2, text.Length - 3);
        }

        // Regular string: "..."
        if (text.StartsWith("\""))
        {
            return text.Slice(1, text.Length - 2);
        }

        return literal.Token.ValueText.AsSpan();
    }

    private static int CountPlaceholders(ReadOnlySpan<char> format)
    {
        int count = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '{')
            {
                if (i + 1 < format.Length && format[i + 1] == '{')
                {
                    i++; // escaped {
                }
                else if (i + 1 < format.Length && char.IsDigit(format[i + 1]))
                {
                    count++;
                    while (i < format.Length && format[i] != '}')
                    {
                        i++;
                    }
                }
                else
                {
                    // Skip to closing brace or end
                    while (i < format.Length && format[i] != '}')
                    {
                        i++;
                    }
                }
            }
            else if (format[i] == '}')
            {
                if (i + 1 < format.Length && format[i + 1] == '}')
                {
                    i++; // escaped }
                }
            }
        }

        return count;
    }
}
