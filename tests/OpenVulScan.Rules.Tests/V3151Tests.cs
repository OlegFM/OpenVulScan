using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class V3151Tests
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
        var rule = new V3151DivisionBeforeZeroCheck();
        var dispatcher = new AstRuleDispatcher(new[] { (AstRule)rule }, compilation);
        return dispatcher.Run(CancellationToken.None);
    }

    [Fact]
    public Task SnapshotDivisionThenZeroCheck()
    {
        const string source = """
            class C {
                int M(int a, int b) {
                    int r = a / b;
                    if (b == 0) { return 0; }
                    return r;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3151", "DivisionThenZeroCheck", source);
    }

    [Fact]
    public void DivisionThenZeroCheckReported()
    {
        // `b` is used as a divisor, then compared to zero afterwards (same block): inconsistent.
        var source = @"
class C
{
    int M(int a, int b)
    {
        int r = a / b;
        if (b == 0) { return 0; }
        return r;
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3151", diagnostics[0].Id);
    }

    [Fact]
    public void ZeroCheckThenDivisionNotReported()
    {
        // Correct order: guard precedes the division. No diagnostic.
        var source = @"
class C
{
    int M(int a, int b)
    {
        if (b == 0) { return 0; }
        return a / b;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DivisionWithoutZeroCheckNotReported()
    {
        var source = @"
class C
{
    int M(int a, int b)
    {
        return a / b;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ModuloThenZeroCheckReported()
    {
        var source = @"
class C
{
    int M(int a, int b)
    {
        int r = a % b;
        if (b != 0) { return r; }
        return 0;
    }
}";
        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal("V3151", diagnostics[0].Id);
    }

    [Fact]
    public void DivisorReassignedBetweenNotReported()
    {
        // `b` is reassigned after the division, so the zero-check inspects a different
        // SSA version than the one that was divided by. No inconsistency.
        var source = @"
class C
{
    int M(int a, int b)
    {
        int r = a / b;
        b = a + 1;
        if (b == 0) { return 0; }
        return r;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ZeroCheckBeforeDivisionInLoopNotReported()
    {
        // Loop guard: the check precedes the division each iteration. An (incorrect)
        // may-analysis would flag this via the back-edge; the intra-block rule does not,
        // because the check and division live in different basic blocks.
        var source = @"
class C
{
    int M(int a, int b)
    {
        int total = 0;
        while (total < 100)
        {
            if (b == 0) { break; }
            total += a / b;
        }
        return total;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ConstantDivisorNotReported()
    {
        // Divisor is a literal, not a variable — nothing to correlate with a zero-check.
        var source = @"
class C
{
    int M(int a, int b)
    {
        int r = a / 2;
        if (b == 0) { return 0; }
        return r;
    }
}";
        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }
}
