using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3014", RuleSeverity.Level1, "CWE-691", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3014WrongLoopVariable : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3014",
        "Wrong loop variable",
        "The variable '{0}' is incremented in the 'for' loop but is not declared in the loop initialization",
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

        var declaredVariables = GetDeclaredVariables(forStatement);
        if (declaredVariables.Count == 0)
        {
            return;
        }

        foreach (var incrementor in forStatement.Incrementors)
        {
            var variableName = GetIncrementedVariableName(incrementor);
            if (variableName is not null && !declaredVariables.Contains(variableName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    incrementor.GetLocation(),
                    variableName));
            }
        }
    }

    private static HashSet<string> GetDeclaredVariables(ForStatementSyntax forStatement)
    {
        var variables = new HashSet<string>();

        if (forStatement.Declaration is not null)
        {
            foreach (var variable in forStatement.Declaration.Variables)
            {
                variables.Add(variable.Identifier.ValueText);
            }
        }

        foreach (var initializer in forStatement.Initializers)
        {
            if (initializer is AssignmentExpressionSyntax assignment && assignment.Left is IdentifierNameSyntax identifier)
            {
                variables.Add(identifier.Identifier.ValueText);
            }
        }

        return variables;
    }

    private static string? GetIncrementedVariableName(ExpressionSyntax expression)
    {
        return expression switch
        {
            PostfixUnaryExpressionSyntax postfix when postfix.Operand is IdentifierNameSyntax id => id.Identifier.ValueText,
            PrefixUnaryExpressionSyntax prefix when prefix.Operand is IdentifierNameSyntax id => id.Identifier.ValueText,
            AssignmentExpressionSyntax assignment when assignment.Left is IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null
        };
    }
}
