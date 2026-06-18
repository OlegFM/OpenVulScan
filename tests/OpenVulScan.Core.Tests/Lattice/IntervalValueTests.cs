using Xunit;

namespace OpenVulScan.Tests;

public class IntervalValueTests
{
    // ── Construction / normalisation ────────────────────────────────────────────────────

    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(IntervalValue.Empty.IsEmpty);
        Assert.False(IntervalValue.Empty.IsTop);
    }

    [Fact]
    public void Top_IsUnbounded()
    {
        Assert.True(IntervalValue.Top.IsTop);
        Assert.False(IntervalValue.Top.IsEmpty);
        Assert.True(IntervalValue.Top.LowerIsInfinite);
        Assert.True(IntervalValue.Top.UpperIsInfinite);
    }

    [Fact]
    public void Range_WithLowerGreaterThanUpper_IsEmpty()
    {
        Assert.Equal(IntervalValue.Empty, IntervalValue.Range(5, 3));
    }

    [Fact]
    public void Constant_IsSingleton()
    {
        var c = IntervalValue.Constant(7);
        Assert.Equal(7, c.Lower);
        Assert.Equal(7, c.Upper);
        Assert.False(c.IsEmpty);
        Assert.Equal(IntervalValue.Range(7, 7), c);
    }

    [Theory]
    [InlineData(0, 10, 5, true)]
    [InlineData(0, 10, 10, true)]
    [InlineData(0, 10, 11, false)]
    [InlineData(0, 10, -1, false)]
    public void Contains_RespectsBounds(long lo, long hi, long probe, bool expected)
    {
        Assert.Equal(expected, IntervalValue.Range(lo, hi).Contains(probe));
    }

    [Fact]
    public void Contains_Empty_IsAlwaysFalse()
    {
        Assert.False(IntervalValue.Empty.Contains(0));
    }

    [Fact]
    public void Contains_Top_IsAlwaysTrue()
    {
        Assert.True(IntervalValue.Top.Contains(long.MaxValue));
        Assert.True(IntervalValue.Top.Contains(0));
    }

    // ── Addition / subtraction / negation ───────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2, 3, 4, 4, 6)]
    [InlineData(-5, 5, 10, 20, 5, 25)]
    [InlineData(0, 0, -3, 7, -3, 7)]
    public void Add_AddsEndpoints(long a, long b, long c, long d, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).Add(IntervalValue.Range(c, d)));
    }

    [Fact]
    public void Add_WithEmpty_IsEmpty()
    {
        Assert.Equal(IntervalValue.Empty, IntervalValue.Range(0, 5).Add(IntervalValue.Empty));
    }

    [Fact]
    public void Add_WithTop_IsTop()
    {
        Assert.Equal(IntervalValue.Top, IntervalValue.Range(0, 5).Add(IntervalValue.Top));
    }

    [Fact]
    public void Add_OverflowSaturatesToPositiveInfinity()
    {
        const long big = 9_000_000_000_000_000_000L; // 9e18, just under long.MaxValue
        var result = IntervalValue.Constant(big).Add(IntervalValue.Constant(big));
        Assert.True(result.UpperIsInfinite);
    }

    [Theory]
    [InlineData(5, 10, 1, 2, 3, 9)]
    [InlineData(0, 0, 3, 4, -4, -3)]
    public void Subtract_SubtractsEndpoints(long a, long b, long c, long d, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).Subtract(IntervalValue.Range(c, d)));
    }

    [Fact]
    public void Negate_FlipsAndSwapsEndpoints()
    {
        Assert.Equal(IntervalValue.Range(-5, 3), IntervalValue.Range(-3, 5).Negate());
        Assert.Equal(IntervalValue.Top, IntervalValue.Top.Negate());
        Assert.Equal(IntervalValue.Empty, IntervalValue.Empty.Negate());
    }

    // ── Multiplication ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 3, 4, 5, 8, 15)]
    [InlineData(-2, 3, -4, 5, -12, 15)]
    [InlineData(-1, 1, -1, 1, -1, 1)]
    public void Multiply_IsHullOfCornerProducts(long a, long b, long c, long d, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).Multiply(IntervalValue.Range(c, d)));
    }

    [Fact]
    public void Multiply_ZeroByTop_IsZero()
    {
        // ∞ × 0 = 0 — the zero interval absorbs the unbounded factor.
        Assert.Equal(IntervalValue.Constant(0), IntervalValue.Constant(0).Multiply(IntervalValue.Top));
    }

    [Fact]
    public void Multiply_WithEmpty_IsEmpty()
    {
        Assert.Equal(IntervalValue.Empty, IntervalValue.Range(2, 3).Multiply(IntervalValue.Empty));
    }

    [Fact]
    public void Multiply_OverflowSaturates()
    {
        const long big = 4_000_000_000L; // 4e9; squared = 1.6e19 > long.MaxValue
        var result = IntervalValue.Constant(big).Multiply(IntervalValue.Constant(big));
        Assert.True(result.UpperIsInfinite);
    }

    // ── Division ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, 20, 2, 5, 2, 10)]
    [InlineData(10, 20, -5, -2, -10, -2)]
    [InlineData(1, 1, 2, 2, 0, 0)] // integer truncation
    public void Divide_IsHullOfCornerQuotients(long a, long b, long c, long d, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).Divide(IntervalValue.Range(c, d)));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 5)]
    [InlineData(-5, 0)]
    public void Divide_ByIntervalContainingZero_IsTop(long c, long d)
    {
        Assert.Equal(IntervalValue.Top, IntervalValue.Range(10, 20).Divide(IntervalValue.Range(c, d)));
    }

    [Fact]
    public void Divide_WithEmpty_IsEmpty()
    {
        Assert.Equal(IntervalValue.Empty, IntervalValue.Empty.Divide(IntervalValue.Range(1, 2)));
    }

    // ── Bitwise ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 5, 0, 3, 0, 3)]
    [InlineData(6, 12, 0, 7, 0, 7)]
    public void BitwiseAnd_NonNegative_IsBoundedByMinUpper(long a, long b, long c, long d, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).BitwiseAnd(IntervalValue.Range(c, d)));
    }

    [Fact]
    public void BitwiseAnd_OneNonNegative_BoundsByThatOperand()
    {
        // left is signed (−1) but right is non-negative ⇒ x & y ≤ right.Upper.
        Assert.Equal(IntervalValue.Range(0, 3), IntervalValue.Range(-1, 5).BitwiseAnd(IntervalValue.Range(0, 3)));
    }

    [Fact]
    public void BitwiseAnd_BothNegative_IsTop()
    {
        Assert.Equal(IntervalValue.Top, IntervalValue.Range(-5, -1).BitwiseAnd(IntervalValue.Range(-2, -1)));
    }

    [Fact]
    public void BitwiseOr_NonNegative_IsBoundedBySum()
    {
        // max(x,y) ≤ x|y ≤ x+y ⇒ [max(2,1), 2+1] contains the real 2|1 = 3.
        Assert.Equal(IntervalValue.Range(2, 3), IntervalValue.Constant(2).BitwiseOr(IntervalValue.Constant(1)));
        Assert.Equal(IntervalValue.Range(0, 7), IntervalValue.Range(0, 3).BitwiseOr(IntervalValue.Range(0, 4)));
    }

    [Fact]
    public void BitwiseOr_Negative_IsTop()
    {
        Assert.Equal(IntervalValue.Top, IntervalValue.Range(-1, 1).BitwiseOr(IntervalValue.Range(0, 4)));
    }

    [Fact]
    public void BitwiseXor_NonNegative_IsBoundedBySum()
    {
        Assert.Equal(IntervalValue.Range(0, 7), IntervalValue.Range(0, 3).BitwiseXor(IntervalValue.Range(0, 4)));
    }

    [Fact]
    public void BitwiseXor_Negative_IsTop()
    {
        Assert.Equal(IntervalValue.Top, IntervalValue.Range(-1, 1).BitwiseXor(IntervalValue.Range(0, 4)));
    }

    // ── Shifts ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2, 3, 8, 16)]
    [InlineData(-1, 1, 2, -4, 4)]
    public void ShiftLeft_MultipliesByPowerOfTwo(long a, long b, int count, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).ShiftLeft(count));
    }

    [Theory]
    [InlineData(8, 16, 2, 2, 4)]
    [InlineData(-8, 8, 1, -4, 4)]
    [InlineData(-1, -1, 1, -1, -1)] // arithmetic shift of −1 stays −1
    public void ShiftRight_DividesByPowerOfTwo(long a, long b, int count, long lo, long hi)
    {
        Assert.Equal(IntervalValue.Range(lo, hi), IntervalValue.Range(a, b).ShiftRight(count));
    }

    [Fact]
    public void ShiftLeft_OverflowSaturates()
    {
        Assert.True(IntervalValue.Constant(9_000_000_000_000_000_000L).ShiftLeft(2).UpperIsInfinite);
    }

    [Fact]
    public void Shift_NegativeOrTooLargeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntervalValue.Constant(1).ShiftLeft(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IntervalValue.Constant(1).ShiftRight(64));
    }

    // ── Equality / formatting ───────────────────────────────────────────────────────────

    [Fact]
    public void Equality_ComparesEndpointsAndEmptiness()
    {
        Assert.True(IntervalValue.Constant(5) == IntervalValue.Range(5, 5));
        Assert.True(IntervalValue.Empty == IntervalValue.Range(5, 3));
        Assert.True(IntervalValue.Range(0, 5) != IntervalValue.Range(0, 6));
        Assert.Equal(IntervalValue.Constant(5).GetHashCode(), IntervalValue.Range(5, 5).GetHashCode());
    }

    [Fact]
    public void ToString_RendersInfinitiesAndEmpty()
    {
        Assert.Equal("∅", IntervalValue.Empty.ToString());
        Assert.Equal("[-∞, +∞]", IntervalValue.Top.ToString());
        Assert.Equal("[0, 5]", IntervalValue.Range(0, 5).ToString());
        Assert.Equal("[-∞, 0]", IntervalValue.Range(long.MinValue, 0).ToString());
        Assert.Equal("[0, +∞]", IntervalValue.Range(0, long.MaxValue).ToString());
    }
}
