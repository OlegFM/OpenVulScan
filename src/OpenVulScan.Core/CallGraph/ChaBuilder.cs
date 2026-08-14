using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Builds a <see cref="CallGraph"/> by Class Hierarchy Analysis: every call site gets the
/// candidate set implied by the receiver's static type hierarchy — all overrides for
/// virtual calls, all source implementations for interface calls, the single target for
/// statically bound calls.
/// </summary>
/// <remarks>
/// <para>
/// The subtype index enumerates the SOURCE assembly only; candidates from referenced
/// assemblies are not discovered (documented CHA limitation until RTA, ovs-xwx.2). For
/// interface calls the interface method itself stays in the candidate set — implementers
/// outside the compilation may exist.
/// </para>
/// <para>
/// Delegates, lambdas-as-values and function pointers produce no edges in v1.
/// </para>
/// </remarks>
public static class ChaBuilder
{
    public static CallGraph Build(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var sourceTypes = CollectSourceTypes(compilation.Assembly.GlobalNamespace, cancellationToken);
        var subtypes = BuildSubtypeIndex(sourceTypes);

        var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var callees = ImmutableDictionary.CreateBuilder<IMethodSymbol, ImmutableArray<CallEdge>>(
            (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default);
        var callers = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);

            foreach (var member in tree.GetRoot(cancellationToken).DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(member, cancellationToken) is not IMethodSymbol caller)
                {
                    continue;
                }

                caller = caller.OriginalDefinition;
                methods.Add(caller);

                var body = model.GetOperation(member, cancellationToken);
                if (body is null)
                {
                    continue;
                }

                var edges = ImmutableArray.CreateBuilder<CallEdge>();
                foreach (var operation in body.DescendantsAndSelf())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (operation)
                    {
                        case IInvocationOperation invocation:
                            edges.Add(new CallEdge(
                                caller,
                                invocation.Syntax,
                                ResolveCandidates(invocation, subtypes)));
                            break;

                        case IObjectCreationOperation { Constructor: { } ctor } creation:
                            edges.Add(new CallEdge(
                                caller,
                                creation.Syntax,
                                [ctor.OriginalDefinition]));
                            break;

                        default:
                            break;
                    }
                }

                var built = edges.ToImmutable();
                callees[caller] = built;

                foreach (var edge in built)
                {
                    foreach (var candidate in edge.Candidates)
                    {
                        if (!callers.TryGetValue(candidate, out var set))
                        {
                            set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                            callers[candidate] = set;
                        }

                        set.Add(caller);
                    }
                }
            }
        }

        var callersImmutable = ImmutableDictionary.CreateBuilder<IMethodSymbol, ImmutableArray<IMethodSymbol>>(
            (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default);
        foreach (var (target, set) in callers)
        {
            callersImmutable[target] = [.. set];
        }

        return new CallGraph(callees.ToImmutable(), callersImmutable.ToImmutable(), methods.ToImmutable());
    }

    private static ImmutableArray<IMethodSymbol> ResolveCandidates(
        IInvocationOperation invocation,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> subtypes)
    {
        var target = invocation.TargetMethod.OriginalDefinition;

        // Statically bound: static, non-virtual instance, or a sealed receiver type.
        bool dispatches = target.IsAbstract || target.IsVirtual || target.IsOverride
            || target.ContainingType?.TypeKind == TypeKind.Interface;
        if (!dispatches || invocation.Instance?.Type is INamedTypeSymbol { IsSealed: true })
        {
            return [target];
        }

        var candidates = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { target };

        if (target.ContainingType is { TypeKind: TypeKind.Interface } iface)
        {
            if (subtypes.TryGetValue(iface.OriginalDefinition, out var implementors))
            {
                foreach (var implementor in implementors)
                {
                    if (implementor.FindImplementationForInterfaceMember(target) is IMethodSymbol impl)
                    {
                        candidates.Add(impl.OriginalDefinition);
                    }
                }
            }

            return [.. candidates];
        }

        if (target.ContainingType is { } declaringType
            && subtypes.TryGetValue(declaringType.OriginalDefinition, out var derived))
        {
            foreach (var subtype in derived)
            {
                foreach (var member in subtype.GetMembers(target.Name).OfType<IMethodSymbol>())
                {
                    if (OverridesTarget(member, target))
                    {
                        candidates.Add(member.OriginalDefinition);
                    }
                }
            }
        }

        return [.. candidates];
    }

    private static bool OverridesTarget(IMethodSymbol member, IMethodSymbol target)
    {
        for (var overridden = member.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, target))
            {
                return true;
            }
        }

        return false;
    }

    private static List<INamedTypeSymbol> CollectSourceTypes(INamespaceSymbol root, CancellationToken cancellationToken)
    {
        var types = new List<INamedTypeSymbol>();
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol ns:
                        pending.Push(ns);
                        break;

                    case INamedTypeSymbol type:
                        types.Add(type);
                        pending.Push(type);
                        break;

                    default:
                        break;
                }
            }
        }

        return types;
    }

    private static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> BuildSubtypeIndex(
        List<INamedTypeSymbol> sourceTypes)
    {
        var subtypes = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        void Register(INamedTypeSymbol key, INamedTypeSymbol subtype)
        {
            if (!subtypes.TryGetValue(key, out var list))
            {
                list = new List<INamedTypeSymbol>();
                subtypes[key] = list;
            }

            list.Add(subtype);
        }

        foreach (var type in sourceTypes)
        {
            for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                Register(baseType.OriginalDefinition, type);
            }

            foreach (var iface in type.AllInterfaces)
            {
                Register(iface.OriginalDefinition, type);
            }
        }

        return subtypes;
    }
}
