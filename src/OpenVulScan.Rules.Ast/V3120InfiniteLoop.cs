using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

/// <summary>
/// V3120 — potentially infinite loop: no variable from the loop exit condition changes its
/// value between iterations, and the body offers no other way out.
/// </summary>
/// <remarks>
/// <para>
/// Definite-only v1: the condition must consist purely of local/parameter identifiers,
/// literals, and side-effect-free operators — any invocation, member/element access, or
/// <c>await</c> makes the loop state opaque and the rule stays silent. The body must not
/// write any condition variable (assignment, compound assignment, <c>++</c>/<c>--</c>,
/// <c>ref</c>/<c>out</c> argument), must not hand one to a lambda or local function, and
/// must not contain <c>break</c>/<c>return</c>/<c>goto</c>/<c>throw</c>/<c>yield</c>.
/// </para>
/// <para>
/// For <c>for</c> loops the incrementor list counts as part of the body for mutation
/// purposes; a loop with no condition (<c>for(;;)</c> / <c>while(true)</c>) is a deliberate
/// idiom and out of scope.
/// </para>
/// </remarks>
[Rule("V3120", RuleSeverity.Level1, "CWE-835", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3120InfiniteLoop : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3120",
        "Potentially infinite loop",
        "Potentially infinite loop. The variable from the loop exit condition does not change its value between iterations.",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnWhileStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is WhileStatementSyntax whileStatement)
        {
            Check(whileStatement.Condition, [whileStatement.Statement], context);
        }
    }

    protected override void OnDoStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is DoStatementSyntax doStatement)
        {
            Check(doStatement.Condition, [doStatement.Statement], context);
        }
    }

    protected override void OnForStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is ForStatementSyntax { Condition: { } condition } forStatement)
        {
            var mutationScopes = new List<SyntaxNode> { forStatement.Statement };
            mutationScopes.AddRange(forStatement.Incrementors);
            Check(condition, mutationScopes, context);
        }
    }

    private static void Check(
        ExpressionSyntax condition,
        IReadOnlyList<SyntaxNode> mutationScopes,
        SyntaxNodeContext context)
    {
        var conditionVars = TryCollectConditionLocals(condition, context);
        if (conditionVars is null || conditionVars.Count == 0)
        {
            return;
        }

        foreach (var scope in mutationScopes)
        {
            if (ScopeCanExitOrMutate(scope, conditionVars, context))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(s_descriptor, condition.GetLocation()));
    }

    /// <summary>
    /// Collects the locals/parameters the condition depends on, or <see langword="null"/>
    /// when the condition contains anything whose value could change without a visible
    /// write (calls, members, elements, patterns, assignments, …).
    /// </summary>
    private static HashSet<ISymbol>? TryCollectConditionLocals(ExpressionSyntax condition, SyntaxNodeContext context)
    {
        var vars = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var node in condition.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IdentifierNameSyntax identifier:
                    var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
                    if (symbol is ILocalSymbol or IParameterSymbol)
                    {
                        vars.Add(symbol);
                    }
                    else
                    {
                        return null; // field / property / method group — state we cannot track
                    }

                    break;

                case LiteralExpressionSyntax:
                case BinaryExpressionSyntax:
                case ParenthesizedExpressionSyntax:
                case CastExpressionSyntax:
                case PredefinedTypeSyntax:
                    break;

                case PrefixUnaryExpressionSyntax prefix
                    when prefix.IsKind(SyntaxKind.LogicalNotExpression) || prefix.IsKind(SyntaxKind.UnaryMinusExpression):
                    break;

                default:
                    return null;
            }
        }

        return vars;
    }

    private static bool ScopeCanExitOrMutate(
        SyntaxNode scope,
        HashSet<ISymbol> conditionVars,
        SyntaxNodeContext context)
    {
        foreach (var node in scope.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case BreakStatementSyntax:
                case ReturnStatementSyntax:
                case GotoStatementSyntax:
                case ThrowStatementSyntax:
                case ThrowExpressionSyntax:
                case YieldStatementSyntax:
                    return true;

                case AssignmentExpressionSyntax assignment
                    when ReferencesConditionVar(assignment.Left, conditionVars, context):
                    return true;

                case PrefixUnaryExpressionSyntax prefix
                    when (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression))
                         && ReferencesConditionVar(prefix.Operand, conditionVars, context):
                    return true;

                case PostfixUnaryExpressionSyntax postfix
                    when (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression))
                         && ReferencesConditionVar(postfix.Operand, conditionVars, context):
                    return true;

                case ArgumentSyntax { RefOrOutKeyword.RawKind: not (int)SyntaxKind.None } argument
                    when ReferencesConditionVar(argument.Expression, conditionVars, context):
                    return true;

                case AnonymousFunctionExpressionSyntax lambda
                    when ReferencesAnyConditionVar(lambda, conditionVars, context):
                    return true;

                case LocalFunctionStatementSyntax localFunction
                    when ReferencesAnyConditionVar(localFunction, conditionVars, context):
                    return true;

                default:
                    break;
            }
        }

        return false;
    }

    private static bool ReferencesConditionVar(
        ExpressionSyntax expression,
        HashSet<ISymbol> conditionVars,
        SyntaxNodeContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        return symbol is not null && conditionVars.Contains(symbol);
    }

    private static bool ReferencesAnyConditionVar(
        SyntaxNode scope,
        HashSet<ISymbol> conditionVars,
        SyntaxNodeContext context)
        => scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => ReferencesConditionVar(identifier, conditionVars, context));
}
