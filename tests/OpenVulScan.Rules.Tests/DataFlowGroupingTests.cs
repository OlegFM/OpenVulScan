using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace OpenVulScan.Tests;

public class DataFlowGroupingTests
{
    private static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void GroupedRulesProduceSameDiagnosticsAsSeparateSolves()
    {
        const string source = @"
class C
{
    void M(bool c)
    {
        int x = 5;
        if (c) { x = 5; }
        if (x == 5) { }
        if (true) { }
    }
}";
        var compilation = Compile(source);

        var grouped = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
            new DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>[]
            {
                new V3022AlwaysTrueFalse(),
                new V3063PartialAlwaysTrueFalse(),
            },
            compilation).Run(CancellationToken.None);

        var separate = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
                new DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>[] { new V3022AlwaysTrueFalse() },
                compilation).Run(CancellationToken.None)
            .Concat(new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(
                new DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>[] { new V3063PartialAlwaysTrueFalse() },
                compilation).Run(CancellationToken.None))
            .ToList();

        static string Render(Diagnostic d) =>
            $"{d.Id}|{d.Location.SourceSpan}|{d.GetMessage(CultureInfo.InvariantCulture)}";

        Assert.Equal(
            separate.Select(Render).OrderBy(s => s, StringComparer.Ordinal),
            grouped.Select(Render).OrderBy(s => s, StringComparer.Ordinal));
        Assert.NotEmpty(grouped);
    }
}
