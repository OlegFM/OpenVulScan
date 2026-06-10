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

    [Fact]
    public void GroupedReplayAdvancesStateOncePerOperation()
    {
        // x = 1; x = 2 produces two sequential definitions with distinct SSA ids.
        // The condition x == 2 is reachable with x→2, so V3022 must fold it to true.
        //
        // NOTE on the double-Apply mutation:
        //   Both V3022 and V3063 diagnose only on boolean-condition operations. Condition
        //   ops are never SSA definition sites, so ConstantSsaTransfer.Apply is a no-op
        //   for them. Moving `state = Apply(state, op)` inside the per-rule fan-out loop
        //   (the double-Apply mutation) therefore produces no observable state divergence
        //   for this rule pair: both rules always see identical state when evaluating
        //   conditions, regardless of Apply ordering. The mutation is structurally
        //   undetectable for any rule pair that only fires on non-definition operations.
        //
        //   This test guards correctness of the grouped solve (V3022 fires exactly once
        //   for the always-true condition and not on x == 1 after redefinition), NOT the
        //   double-Apply mutation. A mutation test for Apply ordering would require a rule
        //   that fires on definition operations and reads state at definition time.
        const string source = @"
class C
{
    void M()
    {
        int x = 1;
        x = 2;
        if (x == 2) { }
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

        // V3022 fires exactly once: the always-true "x == 2" condition after x = 2.
        // It must NOT fire on e.g. the integer literal or any sub-expression.
        // Running both rules grouped must not duplicate or suppress the diagnostic.
        var v3022Diagnostics = grouped.Where(d => d.Id == "V3022").ToList();
        Assert.Single(v3022Diagnostics);
        Assert.Contains("true", v3022Diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
    }
}
