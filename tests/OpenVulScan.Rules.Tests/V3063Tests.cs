using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class V3063Tests
{
    private static CSharpCompilation CreateTestCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation;
    }

    [Fact]
    public void TrueInAndExpressionDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 5;
        if (true && x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // Both operands of && are constant true when x = 5
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("V3063", d.Id));
        Assert.Contains(diagnostics, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("always true", StringComparison.Ordinal));
    }

    [Fact]
    public void FalseInOrExpressionDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 5;
        if (false || x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // false is always false, x == 5 is always true
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("V3063", d.Id));
    }

    [Fact]
    public void KnownConstantInAndExpressionDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 5;
        if (x == 5 && x > 3) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // Both operands are constant true when x = 5
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("V3063", d.Id));
        Assert.All(diagnostics, d => Assert.Contains("always true", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public void KnownConstantInOrExpressionDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 5;
        if (x == 5 || false) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // x == 5 is always true, false is always false
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("V3063", d.Id));
    }

    [Fact]
    public void UintGreaterOrEqualZeroInAndExpressionDetected()
    {
        var source = @"
class C
{
    void M()
    {
        uint i = 0;
        if (i >= 0 && i < 10) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // i >= 0 is always true for uint, i < 10 is true when i = 0
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("V3063", d.Id));
        Assert.All(diagnostics, d => Assert.Contains("always true", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownVariablesInAndExpressionNotDetected()
    {
        var source = @"
class C
{
    void M(int x, int y)
    {
        if (x == 5 && y == 3) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void NegatedKnownConstantInAndExpressionDetected()
    {
        var source = @"
class C
{
    void M()
    {
        bool b = true;
        if (!b && b) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3063PartialAlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<string, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // CFG lowers !b to b in first operand, only second b is detected
        Assert.Single(diagnostics);
        Assert.Equal("V3063", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
