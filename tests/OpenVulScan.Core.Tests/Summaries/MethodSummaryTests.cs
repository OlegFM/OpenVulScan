using Xunit;

namespace OpenVulScan.Tests;

public class MethodSummaryTests
{
    [Fact]
    public void StructuralEquality_IgnoresArrayIdentity()
    {
        var first = new MethodSummary(
            "M:App.C.M",
            NullState.NotNull,
            [new ParameterNullability(0, NullState.MaybeNull)],
            ["System.FormatException"],
            IsPure: true,
            [0]);
        var second = new MethodSummary(
            "M:App.C.M",
            NullState.NotNull,
            [new ParameterNullability(0, NullState.MaybeNull)],
            ["System.FormatException"],
            IsPure: true,
            [0]);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void StructuralEquality_DetectsThrowsDifference()
    {
        var first = new MethodSummary("M:App.C.M", NullState.Unknown, [], [], IsPure: false, []);
        var second = first with { Throws = ["System.Exception"] };

        Assert.NotEqual(first, second);
    }
}
