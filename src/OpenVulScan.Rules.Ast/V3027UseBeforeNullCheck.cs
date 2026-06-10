using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3027", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3027UseBeforeNullCheck : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3027",
        "Variable used before null-check in the same logical expression",
        "Variable '{0}' was used in the logical expression before it was verified against null",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnBinaryExpression(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not BinaryExpressionSyntax binary || !IsLogical(binary))
        {
            return;
        }

        // Analyse each logical chain once, from its root. Nested logical nodes are skipped
        // because their first non-parenthesised ancestor is itself a logical expression.
        if (IsLogical(StripParenthesesUp(binary.Parent)))
        {
            return;
        }

        var operands = new List<ExpressionSyntax>();
        FlattenOperands(binary, operands);

        var model = context.SemanticModel;
        var ct = context.CancellationToken;
        var firstDeref = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        var firstNullCheck = new Dictionary<ISymbol, NullCheck>(SymbolEqualityComparer.Default);

        for (var i = 0; i < operands.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            CollectDerefs(operands[i], model, i, firstDeref, ct);
            CollectNullChecks(operands[i], model, i, firstNullCheck, ct);
        }

        foreach (var (symbol, check) in firstNullCheck)
        {
            if (firstDeref.TryGetValue(symbol, out var derefIndex) && derefIndex < check.Index)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_descriptor,
                    check.Node.GetLocation(),
                    check.Name));
            }
        }
    }

    private readonly record struct NullCheck(int Index, SyntaxNode Node, string Name);

    private static bool IsLogical(SyntaxNode? node)
        => node is BinaryExpressionSyntax b
           && b.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression;

    private static SyntaxNode? StripParenthesesUp(SyntaxNode? node)
    {
        while (node is ParenthesizedExpressionSyntax paren)
        {
            node = paren.Parent;
        }

        return node;
    }

    private static ExpressionSyntax StripParenthesesDown(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }

    private static void FlattenOperands(ExpressionSyntax expr, List<ExpressionSyntax> operands)
    {
        var stripped = StripParenthesesDown(expr);
        if (stripped is BinaryExpressionSyntax b && IsLogical(b))
        {
            FlattenOperands(b.Left, operands);
            FlattenOperands(b.Right, operands);
        }
        else
        {
            operands.Add(stripped);
        }
    }

    private static void CollectDerefs(
        ExpressionSyntax operand,
        SemanticModel model,
        int index,
        Dictionary<ISymbol, int> firstDeref,
        CancellationToken ct)
    {
        foreach (var node in operand.DescendantNodesAndSelf())
        {
            ct.ThrowIfCancellationRequested();

            ExpressionSyntax? receiver = node switch
            {
                MemberAccessExpressionSyntax m when m.Kind() == SyntaxKind.SimpleMemberAccessExpression => m.Expression,
                ElementAccessExpressionSyntax e => e.Expression,
                _ => null
            };

            if (receiver is null)
            {
                continue;
            }

            var symbol = ResolveTrackedSymbol(receiver, model);
            if (symbol is not null)
            {
                firstDeref.TryAdd(symbol, index);
            }
        }
    }

    private static void CollectNullChecks(
        ExpressionSyntax operand,
        SemanticModel model,
        int index,
        Dictionary<ISymbol, NullCheck> firstNullCheck,
        CancellationToken ct)
    {
        foreach (var node in operand.DescendantNodesAndSelf())
        {
            ct.ThrowIfCancellationRequested();

            var checkedExpr = TryGetNullCheckedExpression(node, out var checkNode);
            if (checkedExpr is null)
            {
                continue;
            }

            var symbol = ResolveTrackedSymbol(checkedExpr, model);
            if (symbol is not null)
            {
                firstNullCheck.TryAdd(symbol, new NullCheck(index, checkNode, GetName(checkedExpr)));
            }
        }
    }

    private static ISymbol? ResolveTrackedSymbol(ExpressionSyntax receiver, SemanticModel model)
    {
        var isTrackedShape = receiver is IdentifierNameSyntax
            || (receiver is MemberAccessExpressionSyntax ma
                && ma.Kind() == SyntaxKind.SimpleMemberAccessExpression
                && ma.Expression is ThisExpressionSyntax);

        if (!isTrackedShape)
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(receiver).Symbol;
        return symbol is { Kind: SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field or SymbolKind.Property }
            ? symbol
            : null;
    }

    private static ExpressionSyntax? TryGetNullCheckedExpression(SyntaxNode node, out SyntaxNode checkNode)
    {
        checkNode = node;

        if (node is BinaryExpressionSyntax binary
            && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
        {
            if (IsNullLiteral(binary.Right))
            {
                return binary.Left;
            }

            if (IsNullLiteral(binary.Left))
            {
                return binary.Right;
            }
        }
        else if (node is IsPatternExpressionSyntax isPattern && IsNullPattern(isPattern.Pattern))
        {
            return isPattern.Expression;
        }

        return null;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
        => expr is LiteralExpressionSyntax lit && lit.Kind() == SyntaxKind.NullLiteralExpression;

    private static bool IsNullPattern(PatternSyntax pattern)
    {
        return pattern switch
        {
            ConstantPatternSyntax constant => IsNullLiteral(constant.Expression),
            UnaryPatternSyntax unary when unary.Kind() == SyntaxKind.NotPattern => IsNullPattern(unary.Pattern),
            _ => false
        };
    }

    private static string GetName(ExpressionSyntax expr)
        => expr switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => expr.ToString()
        };
}
