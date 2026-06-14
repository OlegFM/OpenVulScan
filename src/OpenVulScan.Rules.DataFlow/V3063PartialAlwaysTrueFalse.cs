using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3063", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3063PartialAlwaysTrueFalse : DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3063",
        "Partial always true/false condition",
        "A part of conditional expression is always {0}",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ILattice<ImmutableDictionary<SsaId, ConstantLatticeValue>> Lattice { get; }
        = new MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>();

    public override ITransfer<ImmutableDictionary<SsaId, ConstantLatticeValue>> CreateTransfer(SsaIndex ssaIndex)
        => new ConstantSsaTransfer(ssaIndex);

    public override IEdgeRefiner<ImmutableDictionary<SsaId, ConstantLatticeValue>>? CreateEdgeRefiner(SsaIndex ssaIndex)
        => new ConstantSsaEdgeRefiner(ssaIndex);

    protected override void OnState(IOperation operation, ImmutableDictionary<SsaId, ConstantLatticeValue> state, DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsSubConditionExpression(operation))
        {
            return;
        }

        var value = ConstantSsaEvaluator.Evaluate(operation, state, context.SsaIndex);

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
