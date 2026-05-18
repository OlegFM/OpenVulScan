using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3081", RuleSeverity.Level1, "CWE-390", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3081ExceptionNotThrown : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3081",
        "Exception not thrown",
        "An exception object is created but not thrown",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnObjectCreationExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not ObjectCreationExpressionSyntax objectCreation)
        {
            return;
        }

        if (objectCreation.Parent is not ExpressionStatementSyntax)
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(objectCreation).Type;
        if (!IsOrInheritsFromException(type))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_descriptor,
            objectCreation.GetLocation()));
    }

    private static bool IsOrInheritsFromException(ITypeSymbol? type)
    {
        while (type is not null)
        {
            if (type.ToDisplayString() == "System.Exception")
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }
}
