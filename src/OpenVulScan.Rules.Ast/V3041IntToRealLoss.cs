using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3041", RuleSeverity.Level1, "CWE-682", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3041IntToRealLoss : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3041",
        "Possible loss of precision",
        "Implicit conversion from integer type to floating-point type may cause precision loss",
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

        var leftType = context.SemanticModel.GetTypeInfo(assignment.Left).Type;
        var rightType = context.SemanticModel.GetTypeInfo(assignment.Right).Type;

        if (IsIntegerType(rightType) && IsFloatingPointType(leftType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                assignment.GetLocation()));
        }
    }

    protected override void OnEqualsValueClause(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not EqualsValueClauseSyntax equalsValue)
        {
            return;
        }

        var targetType = GetTargetType(equalsValue, context.SemanticModel);
        var valueType = context.SemanticModel.GetTypeInfo(equalsValue.Value).Type;

        if (IsIntegerType(valueType) && IsFloatingPointType(targetType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                equalsValue.GetLocation()));
        }
    }

    protected override void OnInvocationExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol is null)
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var parameters = methodSymbol.Parameters;

        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            var argType = context.SemanticModel.GetTypeInfo(arguments[i].Expression).Type;
            if (IsIntegerType(argType) && IsFloatingPointType(parameters[i].Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    arguments[i].GetLocation()));
            }
        }
    }

    private static ITypeSymbol? GetTargetType(EqualsValueClauseSyntax equalsValue, SemanticModel semanticModel)
    {
        var parent = equalsValue.Parent;
        return parent switch
        {
            VariableDeclaratorSyntax declarator when declarator.Parent is VariableDeclarationSyntax declaration =>
                semanticModel.GetTypeInfo(declaration.Type).Type,
            PropertyDeclarationSyntax property =>
                semanticModel.GetTypeInfo(property.Type).Type,
            ParameterSyntax parameter =>
                parameter.Type is not null ? semanticModel.GetTypeInfo(parameter.Type).Type : null,
            _ => null
        };
    }

    private static bool IsIntegerType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return type.SpecialType switch
        {
            SpecialType.System_SByte => true,
            SpecialType.System_Byte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            _ => false
        };
    }

    private static bool IsFloatingPointType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return type.SpecialType switch
        {
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            _ => false
        };
    }
}
