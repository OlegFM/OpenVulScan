using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3016", RuleSeverity.Level1, "CWE-691", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3016NestedLoopVariable : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3016",
        "Nested loop variable shadows outer loop variable",
        "The loop variable '{0}' has the same name as a variable in an outer loop",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnForStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ForStatementSyntax forStatement)
        {
            return;
        }

        if (forStatement.Declaration is null)
        {
            return;
        }

        foreach (var variable in forStatement.Declaration.Variables)
        {
            var variableName = variable.Identifier.ValueText;
            if (IsVariableDeclaredInOuterLoop(forStatement, variableName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    variable.Identifier.GetLocation(),
                    variableName));
            }
        }
    }

    protected override void OnForEachStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ForEachStatementSyntax forEachStatement)
        {
            return;
        }

        var variableName = forEachStatement.Identifier.ValueText;
        if (IsVariableDeclaredInOuterLoop(forEachStatement, variableName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                forEachStatement.Identifier.GetLocation(),
                variableName));
        }
    }

    private static bool IsVariableDeclaredInOuterLoop(SyntaxNode node, string variableName)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            if (parent is ForStatementSyntax outerFor && outerFor.Declaration is not null)
            {
                foreach (var variable in outerFor.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText == variableName)
                    {
                        return true;
                    }
                }
            }
            else if (parent is ForEachStatementSyntax outerForEach)
            {
                if (outerForEach.Identifier.ValueText == variableName)
                {
                    return true;
                }
            }

            parent = parent.Parent;
        }

        return false;
    }
}
