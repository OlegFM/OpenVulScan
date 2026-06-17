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
        //
        // Two cases:
        //  (a) operation-parent escape — the standard path: parent is IReturnOperation,
        //      IArgumentOperation, or assignment-to-field/property.
        //  (b) branch-value return escape — in Roslyn's CFG a `return expr;` lowers to the
        //      block's BranchValue with FallThroughSuccessor.Semantics == Return, without an
        //      IReturnOperation wrapper. The BranchValue's Parent is null in the CFG, so we
        //      must detect this case by checking the block's branch semantics.
        foreach (var block in cfg.Blocks)
        {
            // (b) Branch-value return: BranchValue with Return semantics.
            // Unwrap conversions and parentheses so `return (IDisposable)r;` is recognised.
            if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return
                && block.BranchValue is not null
                && Unwrap(block.BranchValue) is ILocalReferenceOperation returnRef)
            {
                var key = new TrackedKey.Symbol(returnRef.Local);
                sites.Remove(key);
            }

            // (a) Operation-parent escape.
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

        // Finally-dispose pre-filter: Roslyn's CFG models finally blocks as
        // StructuredExceptionHandling branches — they are NOT on the normal predecessor path to
        // the Exit block, so the worklist solver never sees a finally Dispose(). We compensate by
        // removing any resource that has a Dispose() call in a finally block from the tracked set.
        //
        // KNOWN LIMITATION (v1): this removes the resource even when the Dispose() is guarded by a
        // condition inside the finally (e.g. `finally { if (c) r.Dispose(); }`), which is a false
        // negative. A precise fix requires walking the SEH subgraph; tracked as a follow-up.
        foreach (var block in cfg.Blocks)
        {
            if (block.FallThroughSuccessor?.Semantics != ControlFlowBranchSemantics.StructuredExceptionHandling)
                continue;

            foreach (var op in OperationTree.Enumerate(block))
            {
                if (TryGetDisposedResource(op) is { } disposed)
                    sites.Remove(disposed);
            }
        }

        // Null-conditional dispose pre-filter: `r?.Dispose()` lowers to `if (r != null) r.Dispose();`,
        // whose null-skip path would leave the resource Open at the join (a false positive). Treat
        // `?.Dispose()` as a dispose on all paths (null ⇒ nothing to leak) by dropping the resource.
        foreach (var key in CollectNullConditionalDisposed(cfg))
            sites.Remove(key);

        return sites;
    }

    /// <summary>
    /// Keys of resources disposed via a null-conditional call (<c>x?.Dispose()</c>) anywhere in
    /// <paramref name="cfg"/>. Such a dispose is safe on all paths (null ⇒ nothing to leak), so the
    /// leak rules treat it as a full dispose and drop the resource from tracking.
    /// </summary>
    /// <remarks>
    /// The search is performed on the unlowered <see cref="IOperation"/> tree rooted at
    /// <paramref name="body"/> because Roslyn's CFG lowers <see cref="IConditionalAccessOperation"/>
    /// into separate blocks, making parent-chain detection impossible in the CFG representation.
    /// </remarks>
    public static HashSet<TrackedKey> CollectNullConditionalDisposed(ControlFlowGraph cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        // The CFG root operation preserves the unlowered tree: OriginalOperation is the
        // IMethodBodyOperation that was passed to ControlFlowGraph.Create().
        return CollectNullConditionalDisposed(cfg.OriginalOperation);
    }

    /// <summary>
    /// Keys of resources disposed via a null-conditional call (<c>x?.Dispose()</c>) anywhere in
    /// the unlowered operation tree rooted at <paramref name="body"/>.
    /// </summary>
    public static HashSet<TrackedKey> CollectNullConditionalDisposed(IOperation body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var result = new HashSet<TrackedKey>();
        foreach (var op in OperationTree.Enumerate(body))
        {
            if (TryGetNullConditionalDisposedResource(op) is { } key)
                result.Add(key);
        }

        return result;
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

    /// <summary>
    /// If <paramref name="op"/> is a null-conditional <c>Dispose()</c>/<c>DisposeAsync()</c>
    /// (<c>x?.Dispose()</c>) on a local, parameter, or instance field, returns its key. The
    /// receiver is recovered from the enclosing <see cref="IConditionalAccessOperation"/>.
    /// </summary>
    private static TrackedKey? TryGetNullConditionalDisposedResource(IOperation op)
    {
        if (op is not IInvocationOperation inv)
            return null;
        if (inv.TargetMethod.Name is not ("Dispose" or "DisposeAsync"))
            return null;
        if (!inv.TargetMethod.Parameters.IsEmpty)
            return null;
        if (inv.Instance is not IConditionalAccessInstanceOperation)
            return null;

        for (IOperation? p = inv.Parent; p is not null; p = p.Parent)
        {
            if (p is IConditionalAccessOperation conditional)
                return ResolveResourceKey(conditional.Operation);
        }

        return null;
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
