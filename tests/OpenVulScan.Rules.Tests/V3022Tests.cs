using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class V3022Tests
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
    public void LiteralTrueDetected()
    {
        var source = @"
class C
{
    void M()
    {
        if (true) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void LiteralFalseDetected()
    {
        var source = @"
class C
{
    void M()
    {
        if (false) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always false", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownConstantVariableEqualsDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 5;
        if (x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownConstantVariableNotEqualsDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 5;
        if (x != 3) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownConstantBoolVariableDetected()
    {
        var source = @"
class C
{
    void M()
    {
        bool b = true;
        if (b) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // Only the condition 'b' in 'if (b)' should be reported
        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownVariableNotDetected()
    {
        var source = @"
class C
{
    void M(int x)
    {
        if (x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void WhileWithKnownConstantDetected()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 10;
        while (x > 5) { break; }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void NegatedKnownConstantDetected()
    {
        var source = @"
class C
{
    void M()
    {
        bool b = false;
        if (!b) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // CFG lowers !b to b with swapped branches; we detect b as always false
        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always false", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression test for a crash caused by <c>NotSupportedException</c> escaping
    /// <see cref="ConstantSsaEvaluator"/> when evaluating bitwise operations on
    /// narrow integer types (e.g. <c>short | int</c>). V3022 must not throw; it
    /// should simply produce no diagnostic because the result is not a compile-time
    /// constant bool.
    /// </summary>
    [Fact]
    public void DoesNotCrashOnShortBitwiseOr()
    {
        var source = @"
class C
{
    void M()
    {
        short s = 1;
        if ((s | 2) != 0) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        // Must not throw — that was the regression. The short literal is stored as int
        // by the Roslyn constant evaluator, so BitwiseOr succeeds and (1|2)!=0 folds to
        // always-true. Either outcome (diagnostic or no diagnostic) is acceptable here;
        // what matters is that no NotSupportedException escapes the evaluator.
        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.NotNull(diagnostics);
    }

    /// <summary>
    /// Capture conservativeness (ovs-tr6): the ternary's flow capture carries a
    /// different constant in each arm, so the phi join must yield an unknown
    /// value — <c>x == 5</c> is neither always true nor always false.
    /// </summary>
    [Fact]
    public void TernaryWithDifferentConstantArmsProducesNoDiagnostic()
    {
        var source = @"
class C
{
    void M(bool c)
    {
        var x = c ? 5 : 6;
        if (x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Path-sensitive narrowing (ovs-tr6, <see cref="ConstantSsaEdgeRefiner"/>): an
    /// equality guard narrows the parameter to a known constant on the then-edge, so a
    /// nested re-test of the same equality folds to always-true. Without the edge refiner
    /// the parameter stays unknown and neither condition is reported.
    /// </summary>
    [Fact]
    public void NestedEqualityGuardFoldsInnerCondition()
    {
        var source = @"
class C
{
    void M(int x)
    {
        if (x == 5)
        {
            if (x == 5) { }
        }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // Outer `x == 5` is unknown (x is a parameter); only the inner re-test folds.
        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Meet conservativeness (ovs-tr6): the outer guard is always false (x is 7), so its
    /// then-branch is infeasible. The solver still visits that block, so the edge refiner
    /// must meet x to ⊥ — not overwrite it with Const(5) — to keep the dead inner
    /// `x == 5` from being mis-reported as always true. Only the outer always-false fires.
    /// </summary>
    [Fact]
    public void ContradictoryGuardDoesNotDoubleReport()
    {
        var source = @"
class C
{
    void M()
    {
        int x = 7;
        if (x == 5)
        {
            if (x == 5) { }
        }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always false", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Assignment-as-expression precision (ovs-tr6): the value of <c>(x = 42)</c> is 42, so
    /// <c>z</c> binds the constant flowing through the nested assignment. Without the
    /// <see cref="ISimpleAssignmentOperation"/> case in the SSA evaluator, <c>z</c> degrades
    /// to ⊤ and the always-true condition is missed.
    /// </summary>
    [Fact]
    public void NestedAssignmentExpressionPropagatesConstant()
    {
        var source = @"
class C
{
    void M()
    {
        int x;
        int z = (x = 42);
        if (z == 42) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        Assert.Single(diagnostics);
        Assert.Equal("V3022", diagnostics[0].Id);
        Assert.Contains("always true", diagnostics[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void CrossBranchInvariantDetected()
    {
        var source = @"
class C
{
    void M(bool c)
    {
        int x = 5;
        if (c)
        {
            x = 5;
        }
        if (x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // x is 5 on both paths into the join; the φ-join is Const(5), so
        // `x == 5` is always true. Requires φ-results to be visible to rules.
        Assert.Contains(diagnostics, d =>
            d.Id == "V3022" &&
            d.GetMessage(CultureInfo.InvariantCulture).Contains("always true", StringComparison.Ordinal));
    }
}
