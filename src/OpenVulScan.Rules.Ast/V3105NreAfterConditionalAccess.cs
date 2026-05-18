using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3105", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3105NreAfterConditionalAccess : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3105",
        "Potential NRE after null-conditional access",
        "Potential NullReferenceException after null-conditional operator: member access '{0}' may be called on null",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Reflection dispatcher requires instance method")]
    private void OnConditionalAccessExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return;
        }

        var current = conditionalAccess.WhenNotNull;
        while (current is MemberAccessExpressionSyntax memberAccess)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                memberAccess.Name.GetLocation(),
                memberAccess.Name.Identifier.Text));
            current = memberAccess.Expression;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Reflection dispatcher requires instance method")]
    private void OnSimpleMemberAccessExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var expression = memberAccess.Expression;
        if (expression is ParenthesizedExpressionSyntax paren)
        {
            expression = paren.Expression;
        }

        if (expression is ConditionalAccessExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                memberAccess.Name.GetLocation(),
                memberAccess.Name.Identifier.Text));
        }
    }
}
