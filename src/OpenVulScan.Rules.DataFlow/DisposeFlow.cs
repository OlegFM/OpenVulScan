using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Stateless classification helpers shared by the dispose rules (V3114, V3073, V3178):
/// IDisposable detection, dispose-call and creation recognition, owned-resource collection
/// with an escape pre-filter, and disposable-field collection. Mirrors the role of
/// <c>NullDerefClassifier</c> for the null-deref family.
/// </summary>
internal static class DisposeFlow
{
    /// <summary>True if <paramref name="type"/> implements IDisposable or IAsyncDisposable.</summary>
    public static bool ImplementsDisposable(ITypeSymbol? type, Compilation compilation)
    {
        if (type is null)
            return false;

        var iDisposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var iAsyncDisposable = compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        return ImplementsDisposable(type, iDisposable, iAsyncDisposable);
    }

    /// <summary>
    /// If <paramref name="op"/> is a <c>Dispose()</c>/<c>DisposeAsync()</c> call on a local,
    /// parameter, or instance field, returns the resource's <see cref="TrackedKey"/>.
    /// Conversions and parentheses on the receiver are unwrapped, so the lowered
    /// <c>((IDisposable)x).Dispose()</c> form (and explicit casts) are recognised.
    /// Only the parameterless overloads are matched, so <c>Dispose(bool)</c> is ignored.
    /// </summary>
    public static TrackedKey? TryGetDisposedResource(IOperation op)
    {
        if (op is not IInvocationOperation inv)
            return null;

        var name = inv.TargetMethod.Name;
        if (name is not ("Dispose" or "DisposeAsync"))
            return null;

        if (!inv.TargetMethod.Parameters.IsEmpty)
            return null;

        if (inv.Instance is null)
            return null;

        return ResolveResourceKey(inv.Instance);
    }

    /// <summary>
    /// If <paramref name="op"/> creates an IDisposable via <c>new</c> and binds it to a local
    /// (declarator or simple assignment), returns the local's <see cref="TrackedKey"/> and the
    /// creation location. Factory-method creation is out of scope for v1 (follow-up).
    /// </summary>
    public static (TrackedKey Key, Location Location)? TryGetCreatedResource(
        IOperation op, Compilation compilation)
    {
        switch (op)
        {
            case IVariableDeclaratorOperation { Symbol: ILocalSymbol local, Initializer.Value: var init }
                when IsObjectCreation(init) && ImplementsDisposable(local.Type, compilation):
                return (new TrackedKey.Symbol(local), op.Syntax.GetLocation());

            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation lref, Value: var value }
                when IsObjectCreation(value) && ImplementsDisposable(lref.Local.Type, compilation):
                return (new TrackedKey.Symbol(lref.Local), op.Syntax.GetLocation());

            default:
                return null;
        }
    }

    /// <summary>
    /// Collects locals that own an IDisposable created via <c>new</c> in <paramref name="method"/>
    /// and are candidates for a leak: not declared by a <c>using</c> (those dispose by
    /// construction) and not escaping (returned / assigned to a field or property / passed as an
    /// argument / captured by a lambda or local function). Returns the creation location per key.
    /// </summary>
    public static IReadOnlyDictionary<TrackedKey, Location> CollectOwnedLocals(
        ControlFlowGraph cfg,
        MethodDeclarationSyntax method,
        SemanticModel model,
        Compilation compilation)
    {
        var usingLocals = CollectUsingLocals(method, model);
        var captured = CollectLambdaCaptured(method, model);

        var sites = new Dictionary<TrackedKey, Location>();
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in OperationTree.Enumerate(block))
            {
                if (TryGetCreatedResource(op, compilation) is { } created
                    && created.Key is TrackedKey.Symbol { Variable: ILocalSymbol local }
                    && !usingLocals.Contains(local)
                    && !captured.Contains(local))
                {
                    sites[created.Key] = created.Location;
                }
            }
        }

        // Escape pre-filter: drop any candidate that escapes the method anywhere.
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in OperationTree.Enumerate(block))
            {
                if (op is ILocalReferenceOperation { Local: var refLocal } reference)
                {
                    var key = new TrackedKey.Symbol(refLocal);
                    if (sites.ContainsKey(key) && Escapes(reference))
                        sites.Remove(key);
                }
            }
        }

        return sites;
    }

    /// <summary>
    /// Instance fields of <paramref name="classSymbol"/> whose type implements IDisposable.
    /// Used by V3073 to seed the analysis at <c>Dispose</c> entry.
    /// </summary>
    public static IReadOnlyList<IFieldSymbol> CollectDisposableFields(
        INamedTypeSymbol classSymbol, Compilation compilation)
    {
        var iDisposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var iAsyncDisposable = compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        return classSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst && ImplementsDisposable(f.Type, iDisposable, iAsyncDisposable))
            .ToList();
    }

    // --- helpers ---

    private static bool ImplementsDisposable(
        ITypeSymbol type, INamedTypeSymbol? iDisposable, INamedTypeSymbol? iAsyncDisposable)
    {
        if (SymbolEqualityComparer.Default.Equals(type, iDisposable)
            || SymbolEqualityComparer.Default.Equals(type, iAsyncDisposable))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, iDisposable)
                || SymbolEqualityComparer.Default.Equals(iface, iAsyncDisposable))
                return true;
        }

        return false;
    }

    private static bool IsObjectCreation(IOperation value)
        => Unwrap(value) is IObjectCreationOperation;

    private static TrackedKey? ResolveResourceKey(IOperation instance)
    {
        return Unwrap(instance) switch
        {
            ILocalReferenceOperation l => new TrackedKey.Symbol(l.Local),
            IParameterReferenceOperation p => new TrackedKey.Symbol(p.Parameter),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f =>
                new TrackedKey.InstanceField(f.Field),
            _ => null,
        };
    }

    private static bool Escapes(ILocalReferenceOperation reference)
    {
        // A resource handed out via `yield return` transfers ownership to the caller iterator;
        // check syntactically before climbing the operation tree.
        if (reference.Syntax.FirstAncestorOrSelf<YieldStatementSyntax>() is { } y
            && y.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.YieldReturnStatement))
            return true;

        var parent = reference.Parent;
        while (parent is IConversionOperation or IParenthesizedOperation)
            parent = parent.Parent;

        return parent switch
        {
            IReturnOperation => true,
            IArgumentOperation => true,
            ISimpleAssignmentOperation { Target: IFieldReferenceOperation or IPropertyReferenceOperation } => true,
            _ => false,
        };
    }

    private static HashSet<ILocalSymbol> CollectUsingLocals(MethodDeclarationSyntax method, SemanticModel model)
    {
        var result = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        // `using (var s = ...) { }`
        foreach (var u in method.DescendantNodes().OfType<UsingStatementSyntax>())
            AddDeclared(u.Declaration, model, result);

        // `using var s = ...;`
        foreach (var d in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!d.UsingKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None))
                AddDeclared(d.Declaration, model, result);
        }

        return result;
    }

    private static HashSet<ILocalSymbol> CollectLambdaCaptured(MethodDeclarationSyntax method, SemanticModel model)
    {
        var result = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var lambdaBodies = method.DescendantNodes()
            .Where(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

        foreach (var body in lambdaBodies)
        {
            foreach (var id in body.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(id).Symbol is ILocalSymbol local)
                    result.Add(local);
            }
        }

        return result;
    }

    private static void AddDeclared(
        VariableDeclarationSyntax? declaration, SemanticModel model, HashSet<ILocalSymbol> sink)
    {
        if (declaration is null)
            return;

        foreach (var variable in declaration.Variables)
        {
            if (model.GetDeclaredSymbol(variable) is ILocalSymbol local)
                sink.Add(local);
        }
    }

    private static IOperation Unwrap(IOperation op) => op switch
    {
        IConversionOperation c => Unwrap(c.Operand),
        IParenthesizedOperation p => Unwrap(p.Operand),
        _ => op,
    };
}
