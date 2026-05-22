using Xunit;

namespace OpenVulScan.Tests;

public class BoolFlatLatticeTests
{
    private static readonly BoolFlatLattice _lattice = new();

    [Fact]
    public void Join_WithBottomAndFalse_ReturnsFalse()
    {
        Assert.Equal(BoolLatticeValue.False, _lattice.Join(BoolLatticeValue.Bottom, BoolLatticeValue.False));
    }

    [Fact]
    public void Join_WithBottomAndTrue_ReturnsTrue()
    {
        Assert.Equal(BoolLatticeValue.True, _lattice.Join(BoolLatticeValue.Bottom, BoolLatticeValue.True));
    }

    [Fact]
    public void Join_WithFalseAndTrue_ReturnsTop()
    {
        Assert.Equal(BoolLatticeValue.Top, _lattice.Join(BoolLatticeValue.False, BoolLatticeValue.True));
    }

    [Fact]
    public void Join_WithFalseAndFalse_ReturnsFalse()
    {
        Assert.Equal(BoolLatticeValue.False, _lattice.Join(BoolLatticeValue.False, BoolLatticeValue.False));
    }

    [Fact]
    public void Join_WithTopAndAny_ReturnsTop()
    {
        Assert.Equal(BoolLatticeValue.Top, _lattice.Join(BoolLatticeValue.Top, BoolLatticeValue.Bottom));
        Assert.Equal(BoolLatticeValue.Top, _lattice.Join(BoolLatticeValue.Top, BoolLatticeValue.False));
        Assert.Equal(BoolLatticeValue.Top, _lattice.Join(BoolLatticeValue.Top, BoolLatticeValue.True));
        Assert.Equal(BoolLatticeValue.Top, _lattice.Join(BoolLatticeValue.Top, BoolLatticeValue.Top));
    }

    [Fact]
    public void LessOrEqual_BottomIsLessOrEqualToAll()
    {
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.Bottom, BoolLatticeValue.Bottom));
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.Bottom, BoolLatticeValue.False));
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.Bottom, BoolLatticeValue.True));
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.Bottom, BoolLatticeValue.Top));
    }

    [Fact]
    public void LessOrEqual_TopIsGreaterOrEqualToAll()
    {
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.Bottom, BoolLatticeValue.Top));
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.False, BoolLatticeValue.Top));
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.True, BoolLatticeValue.Top));
        Assert.True(_lattice.LessOrEqual(BoolLatticeValue.Top, BoolLatticeValue.Top));
    }

    [Fact]
    public void LessOrEqual_IncomparableValuesAreNotLessOrEqual()
    {
        Assert.False(_lattice.LessOrEqual(BoolLatticeValue.False, BoolLatticeValue.True));
        Assert.False(_lattice.LessOrEqual(BoolLatticeValue.True, BoolLatticeValue.False));
    }

    [Fact]
    public void Axiom_JoinIsAssociative()
    {
        var values = new[] { BoolLatticeValue.Bottom, BoolLatticeValue.False, BoolLatticeValue.True, BoolLatticeValue.Top };

        foreach (var a in values)
        {
            foreach (var b in values)
            {
                foreach (var c in values)
                {
                    var left = _lattice.Join(_lattice.Join(a, b), c);
                    var right = _lattice.Join(a, _lattice.Join(b, c));
                    Assert.Equal(left, right);
                }
            }
        }
    }

    [Fact]
    public void Axiom_JoinIsCommutative()
    {
        var values = new[] { BoolLatticeValue.Bottom, BoolLatticeValue.False, BoolLatticeValue.True, BoolLatticeValue.Top };

        foreach (var a in values)
        {
            foreach (var b in values)
            {
                Assert.Equal(_lattice.Join(a, b), _lattice.Join(b, a));
            }
        }
    }

    [Fact]
    public void Axiom_JoinIsIdempotent()
    {
        var values = new[] { BoolLatticeValue.Bottom, BoolLatticeValue.False, BoolLatticeValue.True, BoolLatticeValue.Top };

        foreach (var a in values)
        {
            Assert.Equal(a, _lattice.Join(a, a));
        }
    }

    [Fact]
    public void Axiom_Absorption()
    {
        var values = new[] { BoolLatticeValue.Bottom, BoolLatticeValue.False, BoolLatticeValue.True, BoolLatticeValue.Top };

        foreach (var a in values)
        {
            foreach (var b in values)
            {
                var join = _lattice.Join(a, b);
                Assert.True(_lattice.LessOrEqual(a, join));
                Assert.True(_lattice.LessOrEqual(b, join));
            }
        }
    }
}
