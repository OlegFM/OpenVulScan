using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3004", RuleSeverity.Level1, "CWE-670", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3004IdenticalBranches : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3004",
        "Identical then and else blocks",
        "The 'then' and 'else' branches of the conditional operator are identical",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnIfStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not IfStatementSyntax ifStatement)
        {
            return;
        }

        if (ifStatement.Else is null)
        {
            return;
        }

        if (ifStatement.Else.Statement is IfStatementSyntax)
        {
            return;
        }

        if (IsEmptyBlock(ifStatement.Statement) && IsEmptyBlock(ifStatement.Else.Statement))
        {
            return;
        }

        if (ifStatement.Statement.IsEquivalentTo(ifStatement.Else.Statement, topLevel: false))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                ifStatement.IfKeyword.GetLocation()));
        }
    }

    private static bool IsEmptyBlock(SyntaxNode node)
    {
        return node is BlockSyntax block && block.Statements.Count == 0;
    }
}
