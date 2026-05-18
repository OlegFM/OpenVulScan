using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3005", RuleSeverity.Level1, "CWE-480", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3005SelfAssignment : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3005",
        "Self assignment",
        "The variable is assigned to itself",
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

        if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
        {
            return;
        }

        if (assignment.Left.IsEquivalentTo(assignment.Right, topLevel: false))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                assignment.GetLocation()));
        }
    }
}
