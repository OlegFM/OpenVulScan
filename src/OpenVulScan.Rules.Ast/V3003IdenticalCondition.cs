using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3003", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3003IdenticalCondition : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3003",
        "Identical conditions",
        "The conditional expressions of the 'if' and 'else if' operators are identical",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnIfStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not IfStatementSyntax rootIf)
        {
            return;
        }

        if (rootIf.Parent is ElseClauseSyntax)
        {
            return;
        }

        var conditions = new List<ExpressionSyntax>();
        var current = rootIf;

        while (current is not null)
        {
            foreach (var previousCondition in conditions)
            {
                if (previousCondition.IsEquivalentTo(current.Condition, topLevel: false))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        s_descriptor,
                        current.Condition.GetLocation()));
                    break;
                }
            }

            conditions.Add(current.Condition);

            current = current.Else?.Statement is IfStatementSyntax nextIf
                ? nextIf
                : null;
        }
    }
}
