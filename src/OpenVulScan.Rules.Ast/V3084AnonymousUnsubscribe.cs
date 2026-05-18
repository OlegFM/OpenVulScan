using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3084", RuleSeverity.Level1, "CWE-391", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3084AnonymousUnsubscribe : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3084",
        "Anonymous method unsubscription",
        "Unsubscription from an event with an anonymous method or lambda will not work because a new delegate instance is created each time",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnAssignmentExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not AssignmentExpressionSyntax assignment)
        {
            return;
        }

        if (!assignment.OperatorToken.IsKind(SyntaxKind.MinusEqualsToken))
        {
            return;
        }

        if (assignment.Right is not (AnonymousMethodExpressionSyntax or ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_descriptor,
            assignment.Right.GetLocation()));
    }
}
