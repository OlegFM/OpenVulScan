using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3008 — a variable is assigned values twice successively with no intervening read;
/// the first assignment is a dead store and likely a mistake.
/// </summary>
/// <remarks>
/// <para>
/// Intra-basic-block analysis over the lowered CFG (the V3151 precedent): within one block
/// statement order is total and branches split blocks, so "successively" is exactly "same
/// block, no read in between". Cross-block dead stores are deliberately out of scope —
/// they need dominance/liveness and carry a much higher false-positive risk.
/// </para>
/// <para>
/// Conservative resets: any statement other than a simple assignment/declaration clears the
/// pending entries for every local it references anywhere in its subtree — this covers
/// compound assignments, increments, <c>ref</c>/<c>out</c> arguments, and nested lambdas
/// without special-casing them.
/// </para>
/// </remarks>
[Rule("V3008", RuleSeverity.Level2, "CWE-563", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3008DoubleAssignment : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3008",
        "Variable assigned twice successively",
        "The '{0}' variable is assigned values twice successively. Perhaps this is a mistake.",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnMethodDeclaration(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.CancellationToken;
        var operation = context.SemanticModel.GetOperation(context.Node, cancellationToken);
        if (operation is not IMethodBodyOperation methodBody)
        {
            return;
        }

        var cfg = ControlFlowGraph.Create(methodBody, cancellationToken);

        foreach (var block in cfg.Blocks)
        {
            var pending = new Dictionary<ISymbol, IOperation>(SymbolEqualityComparer.Default);

            foreach (var statement in block.Operations)
            {
                ProcessStatement(statement, pending, context);
            }
        }
    }

    private static void ProcessStatement(
        IOperation statement,
        Dictionary<ISymbol, IOperation> pending,
        SyntaxNodeContext context)
    {
        var op = statement;
        if (op is IExpressionStatementOperation exprStmt)
        {
            op = exprStmt.Operation;
        }

        switch (op)
        {
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation targetRef } assign:
                ClearReferencedLocals(assign.Value, pending);
                ReportIfPending(targetRef.Local, assign, pending, context);
                pending[targetRef.Local] = assign;
                return;

            // Non-lowered safety net: a declarator with an initializer is a write too.
            case IVariableDeclaratorOperation { Initializer.Value: { } initValue } declarator:
                ClearReferencedLocals(initValue, pending);
                ReportIfPending(declarator.Symbol, declarator, pending, context);
                pending[declarator.Symbol] = declarator;
                return;

            default:
                // Anything else may read or mutate locals in ways we do not model
                // (compound assignment, ++/--, ref/out, lambda capture) — drop every
                // local the statement touches.
                ClearReferencedLocals(op, pending);
                return;
        }
    }

    private static void ReportIfPending(
        ISymbol symbol,
        IOperation secondWrite,
        Dictionary<ISymbol, IOperation> pending,
        SyntaxNodeContext context)
    {
        if (pending.ContainsKey(symbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                secondWrite.Syntax.GetLocation(),
                symbol.Name));
        }
    }

    private static void ClearReferencedLocals(IOperation operation, Dictionary<ISymbol, IOperation> pending)
    {
        if (operation is ILocalReferenceOperation localRef)
        {
            pending.Remove(localRef.Local);
        }

        foreach (var child in operation.ChildOperations)
        {
            ClearReferencedLocals(child, pending);
        }
    }
}
