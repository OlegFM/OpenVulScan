using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3022", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3022AlwaysTrueFalse : DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3022",
        "Always true/false condition",
        "Expression is always {0}",
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

        if (!IsTopLevelCondition(operation))
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

    private static bool IsTopLevelCondition(IOperation operation)
    {
        if (operation.Type?.SpecialType != SpecialType.System_Boolean)
        {
            return false;
        }

        var syntax = operation.Syntax;

        while (syntax is ParenthesizedExpressionSyntax paren)
        {
            syntax = paren.Expression;
        }

        // Direct condition or inside a ! expression that is the condition
        return IsDirectCondition(syntax) || IsInsideNegatedCondition(syntax);
    }

    private static bool IsDirectCondition(SyntaxNode syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax paren)
        {
            syntax = paren.Expression;
        }

        if (syntax.Parent is IfStatementSyntax ifStmt && ifStmt.Condition == syntax)
        {
            return true;
        }

        if (syntax.Parent is WhileStatementSyntax whileStmt && whileStmt.Condition == syntax)
        {
            return true;
        }

        if (syntax.Parent is ForStatementSyntax forStmt && forStmt.Condition == syntax)
        {
            return true;
        }

        if (syntax.Parent is DoStatementSyntax doStmt && doStmt.Condition == syntax)
        {
            return true;
        }

        if (syntax.Parent is ConditionalExpressionSyntax condExpr && condExpr.Condition == syntax)
        {
            return true;
        }

        return false;
    }

    private static bool IsInsideNegatedCondition(SyntaxNode syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax paren)
        {
            syntax = paren.Expression;
        }

        if (syntax.Parent is PrefixUnaryExpressionSyntax prefix &&
            prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            return IsDirectCondition(prefix);
        }

        return false;
    }
}
