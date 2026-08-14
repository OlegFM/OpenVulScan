using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class RtaRefinerTests
{
    private const string FourImplementorsSource = @"
interface IWorker
{
    void Run();
}

class First : IWorker { public void Run() { } }
class Second : IWorker { public void Run() { } }
class Third : IWorker { public void Run() { } }
class Fourth : IWorker { public void Run() { } }

class Factory
{
    object MakeFirst() { return new First(); }
    object MakeSecond() { return new Second(); }
}

class Caller
{
    void Call(IWorker w)
    {
        w.Run();
    }
}";

    [Fact]
    public void InterfaceDispatch_NarrowsToInstantiatedImplementations()
    {
        var cha = Build(FourImplementorsSource);
        var chaEdge = DispatchEdge(cha);
        Assert.Equal(5, chaEdge.Candidates.Length);

        var rta = RtaRefiner.Refine(cha);

        var rtaEdge = DispatchEdge(rta);
        var owners = rtaEdge.Candidates
            .Select(c => c.ContainingType.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();
        string[] expected = ["First", "Second"];
        Assert.Equal(expected, owners);
    }

    [Fact]
    public void InterfaceDispatch_NoInstantiations_KeepsChaCandidates()
    {
        var source = @"
interface IWorker
{
    void Run();
}

class First : IWorker { public void Run() { } }
class Second : IWorker { public void Run() { } }

class Caller
{
    void Call(IWorker w)
    {
        w.Run();
    }
}";
        var cha = Build(source);
        var chaEdge = DispatchEdge(cha);

        var rta = RtaRefiner.Refine(cha);

        var rtaEdge = DispatchEdge(rta);
        Assert.Equal(chaEdge.Candidates.Length, rtaEdge.Candidates.Length);
    }

    [Fact]
    public void VirtualDispatch_DropsUninstantiatedOverride()
    {
        var source = @"
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

class Factory
{
    object Make() { return new B(); }
}

class Caller
{
    void Call(A a)
    {
        a.M();
    }
}";
        var cha = Build(source);
        var chaEdge = DispatchEdge(cha);
        Assert.Equal(3, chaEdge.Candidates.Length);

        var rta = RtaRefiner.Refine(cha);

        var rtaEdge = DispatchEdge(rta);
        var owners = rtaEdge.Candidates
            .Select(c => c.ContainingType.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();
        string[] expected = ["A", "B"];
        Assert.Equal(expected, owners);
    }

    private static CallEdge DispatchEdge(CallGraph graph)
    {
        var caller = graph.Methods.Single(m => m.Name == "Call");
        return Assert.Single(graph.Callees(caller));
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
}
