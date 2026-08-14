using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// Rapid Type Analysis over a CHA <see cref="CallGraph"/>: narrows dispatched call sites
/// (more than one candidate) to targets whose declaring type can actually be the runtime
/// type of a receiver — i.e. a type instantiated somewhere in the graph, or a base of one.
/// The instantiated-type set is recovered from the constructor edges CHA already recorded,
/// so no re-walk of the compilation is needed.
/// </summary>
/// <remarks>
/// Conservative fallbacks keep the analysis sound for open-world code: statically bound
/// edges are untouched, and if narrowing would leave a dispatched edge with zero targets
/// (e.g. every implementor is created via reflection or DI), the original CHA candidate
/// set is kept.
/// </remarks>
public static class RtaRefiner
{
    public static CallGraph Refine(CallGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var viableOwners = CollectInstantiatedTypeClosure(graph);

        var callees = ImmutableDictionary.CreateBuilder<IMethodSymbol, ImmutableArray<CallEdge>>(
            (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default);
        var callers = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

        foreach (var method in graph.Methods)
        {
            var refined = ImmutableArray.CreateBuilder<CallEdge>();
            foreach (var edge in graph.Callees(method))
            {
                refined.Add(RefineEdge(edge, viableOwners));
            }

            var built = refined.ToImmutable();
            callees[method] = built;

            foreach (var edge in built)
            {
                foreach (var candidate in edge.Candidates)
                {
                    if (!callers.TryGetValue(candidate, out var set))
                    {
                        set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                        callers[candidate] = set;
                    }

                    set.Add(method);
                }
            }
        }

        var callersImmutable = ImmutableDictionary.CreateBuilder<IMethodSymbol, ImmutableArray<IMethodSymbol>>(
            (IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default);
        foreach (var (target, set) in callers)
        {
            callersImmutable[target] = [.. set];
        }

        return new CallGraph(callees.ToImmutable(), callersImmutable.ToImmutable(), graph.Methods);
    }

    private static CallEdge RefineEdge(CallEdge edge, HashSet<INamedTypeSymbol> viableOwners)
    {
        if (edge.Candidates.Length <= 1)
        {
            return edge;
        }

        var viable = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var candidate in edge.Candidates)
        {
            if (candidate.IsAbstract || candidate.ContainingType is not { } owner)
            {
                continue;
            }

            // The interface method itself is CHA's stand-in for unknown external
            // implementors; once concrete instantiated implementations exist, they are
            // the dispatch targets.
            if (owner.TypeKind == TypeKind.Interface)
            {
                continue;
            }

            if (viableOwners.Contains(owner.OriginalDefinition))
            {
                viable.Add(candidate);
            }
        }

        return viable.Count == 0 ? edge : edge with { Candidates = viable.ToImmutable() };
    }

    private static HashSet<INamedTypeSymbol> CollectInstantiatedTypeClosure(CallGraph graph)
    {
        var closure = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var method in graph.Methods)
        {
            foreach (var edge in graph.Callees(method))
            {
                foreach (var candidate in edge.Candidates)
                {
                    if (candidate.MethodKind != MethodKind.Constructor
                        || candidate.ContainingType is not { } instantiated)
                    {
                        continue;
                    }

                    // A receiver statically typed as any base of an instantiated type can
                    // hold it at runtime, so the whole base chain stays viable.
                    for (var type = instantiated; type is not null; type = type.BaseType)
                    {
                        closure.Add(type.OriginalDefinition);
                    }
                }
            }
        }

        return closure;
    }
}
