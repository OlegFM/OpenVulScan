using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// Strongly connected components of a <see cref="CallGraph"/> via Tarjan's algorithm,
/// emitted in reverse topological order of the condensation: every component appears
/// before any component that calls into it (callees first, callers last).
/// </summary>
public static class SccCondensation
{
    public static ImmutableArray<ImmutableArray<IMethodSymbol>> ComputeSccs(CallGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var methodSet = new HashSet<IMethodSymbol>(graph.Methods, SymbolEqualityComparer.Default);
        var successors = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var method in methodSet)
        {
            var targets = new List<IMethodSymbol>();
            foreach (var edge in graph.Callees(method))
            {
                foreach (var candidate in edge.Candidates)
                {
                    if (methodSet.Contains(candidate))
                    {
                        targets.Add(candidate);
                    }
                }
            }

            successors[method] = targets;
        }

        var index = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var lowlink = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
        var onStack = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var componentStack = new Stack<IMethodSymbol>();
        var components = ImmutableArray.CreateBuilder<ImmutableArray<IMethodSymbol>>();
        int nextIndex = 0;

        // Iterative Tarjan: a work-stack frame is (node, position of the next successor to
        // visit); resuming a frame at position > 0 means its previous child just completed.
        var work = new Stack<(IMethodSymbol Node, int Position)>();
        foreach (var root in methodSet)
        {
            if (index.ContainsKey(root))
            {
                continue;
            }

            work.Push((root, 0));
            while (work.Count > 0)
            {
                var (node, position) = work.Pop();
                if (position == 0)
                {
                    index[node] = nextIndex;
                    lowlink[node] = nextIndex;
                    nextIndex++;
                    componentStack.Push(node);
                    onStack.Add(node);
                }

                bool descended = false;
                var targets = successors[node];
                for (int i = position; i < targets.Count; i++)
                {
                    var next = targets[i];
                    if (!index.ContainsKey(next))
                    {
                        work.Push((node, i + 1));
                        work.Push((next, 0));
                        descended = true;
                        break;
                    }

                    if (onStack.Contains(next))
                    {
                        lowlink[node] = Math.Min(lowlink[node], index[next]);
                    }
                }

                if (descended)
                {
                    continue;
                }

                if (lowlink[node] == index[node])
                {
                    var component = ImmutableArray.CreateBuilder<IMethodSymbol>();
                    IMethodSymbol member;
                    do
                    {
                        member = componentStack.Pop();
                        onStack.Remove(member);
                        component.Add(member);
                    }
                    while (!SymbolEqualityComparer.Default.Equals(member, node));

                    components.Add(component.ToImmutable());
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    lowlink[parent] = Math.Min(lowlink[parent], lowlink[node]);
                }
            }
        }

        return components.ToImmutable();
    }
}
