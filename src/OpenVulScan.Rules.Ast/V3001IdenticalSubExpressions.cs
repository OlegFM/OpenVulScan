using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3001IdenticalSubExpressions : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3001",
        "Identical sub-expressions",
        "There are identical sub-expressions to the left and to the right of the '{0}' operator",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnBinaryExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not BinaryExpressionSyntax binary)
        {
            return;
        }

        if (binary.Left.IsEquivalentTo(binary.Right, topLevel: false))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                binary.GetLocation(),
                binary.OperatorToken.Text));
        }
    }
}
