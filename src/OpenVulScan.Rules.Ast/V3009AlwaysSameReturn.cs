using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3009", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Symbol)]
public sealed class V3009AlwaysSameReturn : SymbolRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3009",
        "Always same return value",
        "All branches of the method return the same value",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void VisitMethod(IMethodSymbol symbol, SymbolContext context)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(context);

        if (symbol.ReturnsVoid)
        {
            return;
        }

        if (symbol.IsAbstract || symbol.IsExtern)
        {
            return;
        }

        var returns = new List<ReturnStatementSyntax>();

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(context.CancellationToken);
            var body = syntax switch
            {
                MethodDeclarationSyntax method => method.Body as SyntaxNode ?? method.ExpressionBody,
                AccessorDeclarationSyntax accessor => accessor.Body as SyntaxNode ?? accessor.ExpressionBody,
                _ => null
            };

            if (body is not null)
            {
                returns.AddRange(body.DescendantNodes().OfType<ReturnStatementSyntax>());
            }
        }

        if (returns.Count < 2)
        {
            return;
        }

        var literalValues = new List<object?>();
        foreach (var ret in returns)
        {
            var value = GetLiteralValue(ret.Expression);
            literalValues.Add(value);
        }

        if (literalValues.All(v => Equals(v, literalValues[0])))
        {
            var location = returns[0].GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, location));
        }
    }

    private static object? GetLiteralValue(ExpressionSyntax? expression)
    {
        if (expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.Value;
        }

        return null;
    }
}
