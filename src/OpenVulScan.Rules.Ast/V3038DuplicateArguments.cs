using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3038", RuleSeverity.Level1, "CWE-628", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3038DuplicateArguments : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3038",
        "Duplicate arguments",
        "The same argument is passed to multiple parameters of the method",
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

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2)
        {
            return;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            for (int j = i + 1; j < arguments.Count; j++)
            {
                if (arguments[i].Expression.IsEquivalentTo(arguments[j].Expression, topLevel: false))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        s_descriptor,
                        arguments[j].GetLocation()));
                    return;
                }
            }
        }
    }
}
