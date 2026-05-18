using Microsoft.CodeAnalysis;

namespace OpenVulScan;

[Rule("V3110", RuleSeverity.Level1, "CWE-581", RuleCategory.GeneralAnalysis, AnalysisCapability.Symbol)]
public sealed class V3110EqualsGetHashCode : SymbolRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3110",
        "Equals/GetHashCode symmetry violation",
        "Type '{0}' overrides '{1}' but does not override the corresponding '{2}'",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void VisitClass(INamedTypeSymbol symbol, SymbolContext context)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(context);

        if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return;
        }

        var hasEqualsOverride = HasOverride(symbol, "Equals", SpecialType.System_Object);
        var hasGetHashCodeOverride = HasOverride(symbol, "GetHashCode", null);

        if (hasEqualsOverride && !hasGetHashCodeOverride)
        {
            var location = symbol.Locations.FirstOrDefault();
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    location,
                    symbol.Name,
                    "Equals(object)",
                    "GetHashCode()"));
            }
        }
        else if (!hasEqualsOverride && hasGetHashCodeOverride)
        {
            var location = symbol.Locations.FirstOrDefault();
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    location,
                    symbol.Name,
                    "GetHashCode()",
                    "Equals(object)"));
            }
        }
    }

    private static bool HasOverride(INamedTypeSymbol type, string methodName, SpecialType? parameterType)
    {
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (!method.IsOverride)
            {
                continue;
            }

            if (methodName == "Equals")
            {
                if (method.Parameters.Length == 1 &&
                    method.Parameters[0].Type.SpecialType == parameterType)
                {
                    return true;
                }
            }
            else if (methodName == "GetHashCode")
            {
                if (method.Parameters.Length == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
