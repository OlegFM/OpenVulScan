using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class ChaBuilderTests
{
    [Fact]
    public void StaticCall_SingleCandidate()
    {
        var graph = Build(@"
class C
{
    static void Helper() { }

    void M()
    {
        Helper();
    }
}");
        var edges = graph.Callees(FindMethod(graph, "M"));

        var edge = Assert.Single(edges);
        var candidate = Assert.Single(edge.Candidates);
        Assert.Equal("Helper", candidate.Name);
    }

    [Fact]
    public void VirtualDispatch_AllOverridesAreCandidates()
    {
        var graph = Build(@"
class A
{
    public virtual void M() { }
}

class B : A
{
    public override void M() { }
}

class C2 : B
{
    public override void M() { }
}

class Caller
{
    void Call(A a)
    {
        a.M();
    }
}");
        var edges = graph.Callees(FindMethod(graph, "Call"));

        var edge = Assert.Single(edges);
        var owners = edge.Candidates.Select(c => c.ContainingType.Name).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();
        string[] expected = ["A", "B", "C2"];
        Assert.Equal(expected, owners);
    }

    [Fact]
    public void InterfaceDispatch_AllImplementationsAreCandidates()
    {
        var graph = Build(@"
interface IWorker
{
    void Run();
}

class First : IWorker
{
    public void Run() { }
}

class Second : IWorker
{
    public void Run() { }
}

class Caller
{
    void Call(IWorker w)
    {
        w.Run();
    }
}");
        var edges = graph.Callees(FindMethod(graph, "Call"));

        var edge = Assert.Single(edges);
        var owners = edge.Candidates.Select(c => c.ContainingType.Name).ToHashSet(System.StringComparer.Ordinal);
        Assert.Contains("First", owners);
        Assert.Contains("Second", owners);
    }

    [Fact]
    public void SealedReceiver_SingleCandidate()
    {
        var graph = Build(@"
class A
{
    public virtual void M() { }
}

sealed class B : A
{
    public override void M() { }
}

class Caller
{
    void Call(B b)
    {
        b.M();
    }
}");
        var edges = graph.Callees(FindMethod(graph, "Call"));

        var edge = Assert.Single(edges);
        var candidate = Assert.Single(edge.Candidates);
        Assert.Equal("B", candidate.ContainingType.Name);
    }

    [Fact]
    public void ObjectCreation_ConstructorEdge()
    {
        var graph = Build(@"
class Widget
{
    public Widget(int size) { }
}

class Caller
{
    object Call()
    {
        return new Widget(3);
    }
}");
        var edges = graph.Callees(FindMethod(graph, "Call"));

        var edge = Assert.Single(edges);
        var candidate = Assert.Single(edge.Candidates);
        Assert.Equal(MethodKind.Constructor, candidate.MethodKind);
        Assert.Equal("Widget", candidate.ContainingType.Name);
    }

    [Fact]
    public void CallersIndex_IsInverseOfCandidates()
    {
        var graph = Build(@"
class C
{
    static void Helper() { }

    void M1()
    {
        Helper();
    }

    void M2()
    {
        Helper();
    }
}");
        var helper = graph.Methods.Single(m => m.Name == "Helper");
        var callers = graph.Callers(helper).Select(c => c.Name).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

        string[] expected = ["M1", "M2"];
        Assert.Equal(expected, callers);
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
