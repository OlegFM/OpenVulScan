using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class V3142Tests
{
    private static CSharpCompilation CreateTestCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var compilation = CreateTestCompilation(source);
        var rule = new V3142UnreachableCode();
        var dispatcher = new AstRuleDispatcher(new[] { (AstRule)rule }, compilation);
        return dispatcher.Run(CancellationToken.None);
    }

    [Fact]
    public Task SnapshotCodeAfterReturn()
    {
        const string source = """
            class C {
                void M() {
                    return;
                    int x = 1;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3142", "CodeAfterReturn", source);
    }

    [Fact]
    public void CodeAfterReturnReported()
    {
        var source = @"
class C
{
    void M()
    {
        return;
        int x = 1;
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3142", diagnostics[0].Id);
        // The report must point at the first unreachable statement.
        Assert.Equal(7, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line + 1);
    }

    [Fact]
    public void CodeAfterThrowReported()
    {
        var source = @"
class C
{
    void M()
    {
        throw new System.Exception();
        int x = 1;
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3142", diagnostics[0].Id);
    }

    [Fact]
    public void MultipleStatementsAfterReturnReportedOnce()
    {
        var source = @"
class C
{
    void M()
    {
        return;
        int x = 1;
        int y = 2;
        int z = 3;
    }
}";
        var diagnostics = Run(source);

        // A contiguous dead region is reported once, at its head.
        Assert.Single(diagnostics);
        Assert.Equal(7, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line + 1);
    }

    [Fact]
    public void ReachableCodeNotReported()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 1;
        int y = 2;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ConstantFalseIfBodyReported()
    {
        // Roslyn constant-folds `if (false)`, so the body block is unreachable.
        var source = @"
class C
{
    void M()
    {
        if (false)
        {
            int x = 1;
        }
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3142", diagnostics[0].Id);
    }

    [Fact]
    public void CodeAfterInfiniteLoopReported()
    {
        var source = @"
class C
{
    void M()
    {
        while (true) { }
        int x = 1;
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3142", diagnostics[0].Id);
    }

    [Fact]
    public void EarlyReturnInBranchDoesNotReportFollowingCode()
    {
        // `int x = 1;` is reachable on the false edge of the guard — no diagnostic.
        var source = @"
class C
{
    void M(bool c)
    {
        if (c)
        {
            return;
        }

        int x = 1;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DeadNestedRegionReportedOnce()
    {
        // The whole `if` after the return is one dead region: the `if` statement and
        // the statements nested inside it must collapse to a single diagnostic at the head.
        var source = @"
class C
{
    void M(bool c)
    {
        return;
        if (c)
        {
            int y = 1;
            int z = 2;
        }
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3142", diagnostics[0].Id);
        // Head of the region is the `if` statement.
        Assert.Equal(7, diagnostics[0].Location.GetLineSpan().StartLinePosition.Line + 1);
    }
}
