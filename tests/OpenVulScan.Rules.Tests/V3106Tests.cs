using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class V3106Tests
{
    [Fact]
    public void ConstantIndexEqualToLengthDetected()
    {
        var diagnostics = Run(@"
class C
{
    int M()
    {
        var a = new int[10];
        return a[10];
    }
}");
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("V3106", diagnostic.Id);
    }

    [Fact]
    public void NegativeConstantIndexDetected()
    {
        var diagnostics = Run(@"
class C
{
    int M()
    {
        var a = new int[10];
        int i = -1;
        return a[i];
    }
}");
        Assert.Single(diagnostics);
    }

    [Fact]
    public void GuardProvenOutOfRangeDetected()
    {
        var diagnostics = Run(@"
class C
{
    int M(int i)
    {
        var a = new int[10];
        if (i >= 10)
        {
            return a[i];
        }
        return 0;
    }
}");
        Assert.Single(diagnostics);
    }

    [Fact]
    public void InRangeConstantIndexNotFlagged()
    {
        var diagnostics = Run(@"
class C
{
    int M()
    {
        var a = new int[10];
        return a[9];
    }
}");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownIndexNotFlagged()
    {
        var diagnostics = Run(@"
class C
{
    int M(int i)
    {
        var a = new int[10];
        return a[i];
    }
}");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnknownArrayLengthNotFlagged()
    {
        var diagnostics = Run(@"
class C
{
    int M(int[] a)
    {
        return a[100];
    }
}");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CountingLoopNotFlagged()
    {
        // Widening pushes i to [0, +inf] at the loop header; the i < 10 edge refinement
        // must bring the body's view back to [0, 9] - no diagnostic.
        var diagnostics = Run(@"
class C
{
    void M()
    {
        var a = new int[10];
        for (int i = 0; i < 10; i = i + 1)
        {
            a[i] = 0;
        }
    }
}");
        Assert.Empty(diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var rule = new V3106IndexOutOfBounds();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, IntervalValue>>(
            new[] { rule }, compilation);
        return dispatcher.Run(CancellationToken.None);
    }
}
