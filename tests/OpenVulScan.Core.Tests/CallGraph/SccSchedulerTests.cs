using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class SccSchedulerTests
{
    private const string ChainSource = @"
class C
{
    void Top() { Mid(); }
    void Mid() { Leaf(); }
    void Leaf() { }
}";

    private const string CycleSource = @"
class C
{
    void A() { B(); }
    void B() { C2(); }
    void C2() { A(); }
}";

    [Fact]
    public void ComputeSccs_AcyclicChain_EmitsCalleesFirst()
    {
        var graph = Build(ChainSource);

        var components = SccCondensation.ComputeSccs(graph);

        Assert.Equal(3, components.Length);
        Assert.All(components, c => Assert.Single(c));
        Assert.True(IndexOf(components, "Leaf") < IndexOf(components, "Mid"));
        Assert.True(IndexOf(components, "Mid") < IndexOf(components, "Top"));
    }

    [Fact]
    public void ComputeSccs_ThreeMethodCycle_SingleComponent()
    {
        var graph = Build(CycleSource);

        var components = SccCondensation.ComputeSccs(graph);

        var cycle = Assert.Single(components);
        var names = cycle.Select(m => m.Name).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();
        string[] expected = ["A", "B", "C2"];
        Assert.Equal(expected, names);
    }

    [Fact]
    public void Run_Chain_LeafFactReachesRootInOnePass()
    {
        var graph = Build(ChainSource);

        var summaries = BottomUpSummaryScheduler.Run(
            graph,
            (method, current) => AnalyzeThrows(graph, method, current, ownThrow: method.Name == "Leaf" ? "LeafEx" : null),
            CancellationToken.None);

        var top = summaries[FindMethod(graph, "Top")];
        Assert.Contains("LeafEx", top.Throws);
    }

    [Fact]
    public void Run_ThreeMethodCycle_ThrowsUnionStabilizes()
    {
        var graph = Build(CycleSource);

        var summaries = BottomUpSummaryScheduler.Run(
            graph,
            (method, current) => AnalyzeThrows(graph, method, current, ownThrow: method.Name + "Ex"),
            CancellationToken.None);

        string[] expected = ["AEx", "BEx", "C2Ex"];
        foreach (var name in new[] { "A", "B", "C2" })
        {
            var summary = summaries[FindMethod(graph, name)];
            string[] actual = [.. summary.Throws.OrderBy(t => t, System.StringComparer.Ordinal)];
            Assert.Equal(expected, actual);
        }
    }

    private static MethodSummary AnalyzeThrows(
        CallGraph graph,
        IMethodSymbol method,
        IReadOnlyDictionary<IMethodSymbol, MethodSummary> current,
        string? ownThrow)
    {
        var throws = new SortedSet<string>(System.StringComparer.Ordinal);
        if (ownThrow is not null)
        {
            throws.Add(ownThrow);
        }

        foreach (var edge in graph.Callees(method))
        {
            foreach (var candidate in edge.Candidates)
            {
                if (current.TryGetValue(candidate, out var calleeSummary))
                {
                    throws.UnionWith(calleeSummary.Throws);
                }
            }
        }

        return new MethodSummary(method.Name, NullState.Unknown, [], [.. throws], IsPure: false, []);
    }

    private static int IndexOf(
        System.Collections.Immutable.ImmutableArray<System.Collections.Immutable.ImmutableArray<IMethodSymbol>> components,
        string methodName)
    {
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].Any(m => m.Name == methodName))
            {
                return i;
            }
        }

        return -1;
    }

    private static CallGraph Build(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return ChaBuilder.Build(compilation, CancellationToken.None);
    }

    private static IMethodSymbol FindMethod(CallGraph graph, string name)
        => graph.Methods.Single(m => m.Name == name);
}
