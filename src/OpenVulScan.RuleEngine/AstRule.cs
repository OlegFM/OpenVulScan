using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenVulScan;

public abstract class AstRule
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<SyntaxKind, MethodInfo>> s_kindCache = new();

    private IReadOnlyDictionary<SyntaxKind, MethodInfo>? _kindMap;

    public IReadOnlySet<SyntaxKind> SupportedSyntaxKinds => GetKindMap().Keys.ToHashSet();

    private IReadOnlyDictionary<SyntaxKind, MethodInfo> GetKindMap()
    {
        if (_kindMap is not null)
        {
            return _kindMap;
        }

        var type = GetType();
        _kindMap = s_kindCache.GetOrAdd(type, static t => BuildKindMap(t));
        return _kindMap;
    }

    private static readonly SyntaxKind[] s_binaryExpressionKinds =
    [
        SyntaxKind.AddExpression,
        SyntaxKind.SubtractExpression,
        SyntaxKind.MultiplyExpression,
        SyntaxKind.DivideExpression,
        SyntaxKind.ModuloExpression,
        SyntaxKind.LeftShiftExpression,
        SyntaxKind.RightShiftExpression,
        SyntaxKind.LogicalOrExpression,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.BitwiseOrExpression,
        SyntaxKind.BitwiseAndExpression,
        SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.EqualsExpression,
        SyntaxKind.NotEqualsExpression,
        SyntaxKind.LessThanExpression,
        SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.IsExpression,
        SyntaxKind.AsExpression,
        SyntaxKind.CoalesceExpression,
        SyntaxKind.UnsignedRightShiftExpression,
    ];

    private static Dictionary<SyntaxKind, MethodInfo> BuildKindMap(Type type)
    {
        var map = new Dictionary<SyntaxKind, MethodInfo>();
        var astRuleType = typeof(AstRule);
        var contextType = typeof(SyntaxNodeContext);

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.DeclaringType == astRuleType)
            {
                continue;
            }

            if (!method.Name.StartsWith("On", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != contextType)
            {
                continue;
            }

            var kindName = method.Name.Substring(2);
            if (kindName == "BinaryExpression")
            {
                foreach (var kind in s_binaryExpressionKinds)
                {
                    map[kind] = method;
                }

                continue;
            }

            if (!Enum.TryParse<SyntaxKind>(kindName, out var syntaxKind))
            {
                continue;
            }

            map[syntaxKind] = method;
        }

        return map;
    }

    public void Visit(SyntaxNode node, SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);

        var kindMap = GetKindMap();
        if (!kindMap.TryGetValue(node.Kind(), out var method))
        {
            throw new ArgumentException(
                $"No handler registered for syntax kind '{node.Kind()}'.",
                nameof(node));
        }

        method.Invoke(this, new object[] { context });
    }

    protected virtual void OnIfStatement(SyntaxNodeContext context) { }
    protected virtual void OnBinaryExpression(SyntaxNodeContext context) { }
    protected virtual void OnInvocationExpression(SyntaxNodeContext context) { }
    protected virtual void OnAssignmentExpression(SyntaxNodeContext context) { }
    protected virtual void OnMemberAccessExpression(SyntaxNodeContext context) { }
    protected virtual void OnLiteralExpression(SyntaxNodeContext context) { }
    protected virtual void OnVariableDeclaration(SyntaxNodeContext context) { }
    protected virtual void OnMethodDeclaration(SyntaxNodeContext context) { }
    protected virtual void OnClassDeclaration(SyntaxNodeContext context) { }
    protected virtual void OnStructDeclaration(SyntaxNodeContext context) { }
    protected virtual void OnInterfaceDeclaration(SyntaxNodeContext context) { }
    protected virtual void OnEnumDeclaration(SyntaxNodeContext context) { }
    protected virtual void OnForStatement(SyntaxNodeContext context) { }
    protected virtual void OnForEachStatement(SyntaxNodeContext context) { }
    protected virtual void OnWhileStatement(SyntaxNodeContext context) { }
    protected virtual void OnDoStatement(SyntaxNodeContext context) { }
    protected virtual void OnSwitchStatement(SyntaxNodeContext context) { }
    protected virtual void OnCatchClause(SyntaxNodeContext context) { }
    protected virtual void OnTryStatement(SyntaxNodeContext context) { }
    protected virtual void OnUsingStatement(SyntaxNodeContext context) { }
    protected virtual void OnLockStatement(SyntaxNodeContext context) { }
    protected virtual void OnReturnStatement(SyntaxNodeContext context) { }
    protected virtual void OnThrowStatement(SyntaxNodeContext context) { }
    protected virtual void OnExpressionStatement(SyntaxNodeContext context) { }
    protected virtual void OnArgument(SyntaxNodeContext context) { }
    protected virtual void OnParameter(SyntaxNodeContext context) { }
    protected virtual void OnAttribute(SyntaxNodeContext context) { }
    protected virtual void OnArrayCreationExpression(SyntaxNodeContext context) { }
    protected virtual void OnObjectCreationExpression(SyntaxNodeContext context) { }
    protected virtual void OnCastExpression(SyntaxNodeContext context) { }
    protected virtual void OnConditionalExpression(SyntaxNodeContext context) { }
    protected virtual void OnSimpleLambdaExpression(SyntaxNodeContext context) { }
    protected virtual void OnParenthesizedLambdaExpression(SyntaxNodeContext context) { }
    protected virtual void OnArrowExpressionClause(SyntaxNodeContext context) { }
    protected virtual void OnEqualsValueClause(SyntaxNodeContext context) { }
    protected virtual void OnInterpolatedStringExpression(SyntaxNodeContext context) { }
    protected virtual void OnAwaitExpression(SyntaxNodeContext context) { }
    protected virtual void OnElementAccessExpression(SyntaxNodeContext context) { }
    protected virtual void OnPostfixUnaryExpression(SyntaxNodeContext context) { }
    protected virtual void OnPrefixUnaryExpression(SyntaxNodeContext context) { }
    protected virtual void OnBaseExpression(SyntaxNodeContext context) { }
    protected virtual void OnThisExpression(SyntaxNodeContext context) { }
    protected virtual void OnTypeOfExpression(SyntaxNodeContext context) { }
    protected virtual void OnDefaultExpression(SyntaxNodeContext context) { }
    protected virtual void OnNameOfExpression(SyntaxNodeContext context) { }
    protected virtual void OnCheckedExpression(SyntaxNodeContext context) { }
    protected virtual void OnUncheckedExpression(SyntaxNodeContext context) { }
    protected virtual void OnSizeOfExpression(SyntaxNodeContext context) { }
    protected virtual void OnTupleExpression(SyntaxNodeContext context) { }
    protected virtual void OnRefExpression(SyntaxNodeContext context) { }
    protected virtual void OnDeclarationExpression(SyntaxNodeContext context) { }
    protected virtual void OnThrowExpression(SyntaxNodeContext context) { }
    protected virtual void OnIsPatternExpression(SyntaxNodeContext context) { }
    protected virtual void OnRecursivePattern(SyntaxNodeContext context) { }
    protected virtual void OnVarPattern(SyntaxNodeContext context) { }
    protected virtual void OnConstantPattern(SyntaxNodeContext context) { }
    protected virtual void OnParenthesizedPattern(SyntaxNodeContext context) { }
    protected virtual void OnRelationalPattern(SyntaxNodeContext context) { }
    protected virtual void OnTypePattern(SyntaxNodeContext context) { }
    protected virtual void OnOrPattern(SyntaxNodeContext context) { }
    protected virtual void OnAndPattern(SyntaxNodeContext context) { }
    protected virtual void OnNotPattern(SyntaxNodeContext context) { }
}
