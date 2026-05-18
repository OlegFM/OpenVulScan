using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3037", RuleSeverity.Level1, "CWE-480", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3037SwapWithoutTemp : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3037",
        "Swap without temporary variable",
        "This assignment does not swap values; consider using a temporary variable",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnExpressionStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ExpressionStatementSyntax currentExpr)
        {
            return;
        }

        if (currentExpr.Expression is not AssignmentExpressionSyntax currentAssign ||
            currentAssign.Kind() != SyntaxKind.SimpleAssignmentExpression)
        {
            return;
        }

        if (currentExpr.Parent is not BlockSyntax block)
        {
            return;
        }

        var index = block.Statements.IndexOf(currentExpr);
        if (index <= 0)
        {
            return;
        }

        if (block.Statements[index - 1] is not ExpressionStatementSyntax prevExpr)
        {
            return;
        }

        if (prevExpr.Expression is not AssignmentExpressionSyntax prevAssign ||
            prevAssign.Kind() != SyntaxKind.SimpleAssignmentExpression)
        {
            return;
        }

        if (IsSimpleTarget(prevAssign.Left) &&
            IsSimpleTarget(prevAssign.Right) &&
            IsSimpleTarget(currentAssign.Left) &&
            IsSimpleTarget(currentAssign.Right) &&
            prevAssign.Left.IsEquivalentTo(currentAssign.Right, topLevel: false) &&
            prevAssign.Right.IsEquivalentTo(currentAssign.Left, topLevel: false))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                currentAssign.GetLocation()));
        }
    }

    private static bool IsSimpleTarget(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax;
    }
}
