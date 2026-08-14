using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// Drives per-method summary computation bottom-up over the call graph: components from
/// <see cref="SccCondensation"/> are processed callees-first, so <paramref name="analyze"/>
/// sees finished callee summaries at every call site. Within a cyclic component the
/// computation iterates until summaries stop changing (structural
/// <see cref="MethodSummary"/> equality), so the analyze callback must be monotone over a
/// finite domain to terminate.
/// </summary>
public static class BottomUpSummaryScheduler
{
    private const int MaxIterationsPerComponent = 10_000;

    public static ImmutableDictionary<IMethodSymbol, MethodSummary> Run(
        CallGraph graph,
        Func<IMethodSymbol, IReadOnlyDictionary<IMethodSymbol, MethodSummary>, MethodSummary> analyze,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(analyze);

        var summaries = new Dictionary<IMethodSymbol, MethodSummary>(SymbolEqualityComparer.Default);

        foreach (var component in SccCondensation.ComputeSccs(graph))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var method in component)
            {
                summaries[method] = analyze(method, summaries);
            }

            // A lone method with no self-call cannot depend on its own summary; the seeding
            // pass above is already its fixed point.
            if (component.Length == 1 && !HasSelfLoop(graph, component[0]))
            {
                continue;
            }

            for (int iteration = 0; ; iteration++)
            {
                if (iteration >= MaxIterationsPerComponent)
                {
                    throw new InvalidOperationException(
                        $"Summary computation did not stabilize within {MaxIterationsPerComponent} iterations for a component of {component.Length} method(s).");
                }

                cancellationToken.ThrowIfCancellationRequested();

                bool changed = false;
                foreach (var method in component)
                {
                    var updated = analyze(method, summaries);
                    if (!updated.Equals(summaries[method]))
                    {
                        summaries[method] = updated;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        return summaries.ToImmutableDictionary((IEqualityComparer<IMethodSymbol>)SymbolEqualityComparer.Default);
    }

    private static bool HasSelfLoop(CallGraph graph, IMethodSymbol method)
    {
        foreach (var edge in graph.Callees(method))
        {
            foreach (var candidate in edge.Candidates)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate, method))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
