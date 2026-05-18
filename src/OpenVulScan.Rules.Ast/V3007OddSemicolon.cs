using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3007", RuleSeverity.Level1, "CWE-691", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3007OddSemicolon : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3007",
        "Odd semicolon",
        "A semicolon is probably missing after the '{0}' statement",
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

        if (ifStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "if", empty);
        }
    }

    protected override void OnForStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ForStatementSyntax forStatement)
        {
            return;
        }

        if (forStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "for", empty);
        }
    }

    protected override void OnWhileStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not WhileStatementSyntax whileStatement)
        {
            return;
        }

        if (whileStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "while", empty);
        }
    }

    protected override void OnDoStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not DoStatementSyntax doStatement)
        {
            return;
        }

        if (doStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "do", empty);
        }
    }

    protected override void OnForEachStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ForEachStatementSyntax foreachStatement)
        {
            return;
        }

        if (foreachStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "foreach", empty);
        }
    }

    protected override void OnLockStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not LockStatementSyntax lockStatement)
        {
            return;
        }

        if (lockStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "lock", empty);
        }
    }

    protected override void OnUsingStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not UsingStatementSyntax usingStatement)
        {
            return;
        }

        if (usingStatement.Statement is EmptyStatementSyntax empty)
        {
            ReportDiagnostic(context, "using", empty);
        }
    }

    private static void ReportDiagnostic(SyntaxNodeContext context, string keyword, EmptyStatementSyntax empty)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            s_descriptor,
            empty.GetLocation(),
            keyword));
    }
}
