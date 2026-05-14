using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public abstract class SymbolRule
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<SymbolKind, MethodInfo>> s_kindCache = new();

    private static readonly Dictionary<string, SymbolKind> s_methodNameToKind = new(StringComparer.Ordinal)
    {
        ["VisitMethod"] = SymbolKind.Method,
        ["VisitClass"] = SymbolKind.NamedType,
        ["VisitProperty"] = SymbolKind.Property,
        ["VisitField"] = SymbolKind.Field,
    };

    private IReadOnlyDictionary<SymbolKind, MethodInfo>? _kindMap;

    public IReadOnlySet<SymbolKind> SupportedSymbolKinds => GetKindMap().Keys.ToHashSet();

    private IReadOnlyDictionary<SymbolKind, MethodInfo> GetKindMap()
    {
        if (_kindMap is not null)
        {
            return _kindMap;
        }

        var type = GetType();
        _kindMap = s_kindCache.GetOrAdd(type, static t => BuildKindMap(t));
        return _kindMap;
    }

    private static Dictionary<SymbolKind, MethodInfo> BuildKindMap(Type type)
    {
        var map = new Dictionary<SymbolKind, MethodInfo>();
        var symbolRuleType = typeof(SymbolRule);

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.DeclaringType == symbolRuleType)
            {
                continue;
            }

            if (!s_methodNameToKind.TryGetValue(method.Name, out var kind))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[1].ParameterType != typeof(SymbolContext))
            {
                continue;
            }

            map[kind] = method;
        }

        return map;
    }

    public void Visit(ISymbol symbol, SymbolContext context)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(context);

        var kindMap = GetKindMap();
        if (!kindMap.TryGetValue(symbol.Kind, out var method))
        {
            return;
        }

        method.Invoke(this, new object[] { symbol, context });
    }

    protected virtual void VisitMethod(IMethodSymbol symbol, SymbolContext context) { }

    protected virtual void VisitClass(INamedTypeSymbol symbol, SymbolContext context) { }

    protected virtual void VisitProperty(IPropertySymbol symbol, SymbolContext context) { }

    protected virtual void VisitField(IFieldSymbol symbol, SymbolContext context) { }
}
