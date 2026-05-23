using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3063", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3063PartialAlwaysTrueFalse : DataFlowRule<ImmutableDictionary<string, ConstantLatticeValue>>
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3063",
        "Partial always true/false condition",
        "A part of conditional expression is always {0}",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ConstantMapTransfer s_transfer = new();
    private static readonly ConstantEdgeRefiner s_edgeRefiner = new();

    public override ILattice<ImmutableDictionary<string, ConstantLatticeValue>> Lattice { get; } = new MapLattice<string, ConstantLattice, ConstantLatticeValue>();

    public override ITransfer<ImmutableDictionary<string, ConstantLatticeValue>> Transfer => s_transfer;

    public override IEdgeRefiner<ImmutableDictionary<string, ConstantLatticeValue>>? EdgeRefiner => s_edgeRefiner;

    protected override void OnState(IOperation operation, ImmutableDictionary<string, ConstantLatticeValue> state, DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsSubConditionExpression(operation))
        {
            return;
        }

        var value = ConstantMapTransfer.EvaluateExpression(operation, state);

        if (value.Kind == LatticeElementKind.Const && value.Value is bool boolValue)
        {
            var diagnostic = Diagnostic.Create(
                s_descriptor,
                operation.Syntax.GetLocation(),
                boolValue ? "true" : "false");

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsSubConditionExpression(IOperation operation)
    {
        if (operation.Type?.SpecialType != SpecialType.System_Boolean)
        {
            return false;
        }

        var syntax = operation.Syntax;
        var parentSyntax = syntax.Parent;

        while (parentSyntax is ParenthesizedExpressionSyntax paren)
        {
            parentSyntax = paren.Parent;
        }

        if (parentSyntax is not BinaryExpressionSyntax binary)
        {
            return false;
        }

        if (!binary.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken) &&
            !binary.OperatorToken.IsKind(SyntaxKind.BarBarToken))
        {
            return false;
        }

        return IsInsideConditionSyntax(binary);
    }

    private static bool IsInsideConditionSyntax(SyntaxNode? node)
    {
        while (node is not null)
        {
            if (node is IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax
                or DoStatementSyntax or ConditionalExpressionSyntax)
            {
                return true;
            }

            if (node is StatementSyntax)
            {
                return false;
            }

            node = node.Parent;
        }

        return false;
    }
}
