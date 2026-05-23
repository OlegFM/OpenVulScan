using Xunit;

namespace OpenVulScan.Tests;

public class ConstantLatticeTests
{
    private static readonly ConstantLattice _lattice = new();

    [Fact]
    public void Join_BottomWithConst_ReturnsConst()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.Equal(c, _lattice.Join(ConstantLatticeValue.Bottom, c));
        Assert.Equal(c, _lattice.Join(c, ConstantLatticeValue.Bottom));
    }

    [Fact]
    public void Join_BottomWithTop_ReturnsTop()
    {
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(ConstantLatticeValue.Bottom, ConstantLatticeValue.Top));
    }

    [Fact]
    public void Join_SameConst_ReturnsSameConst()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.Equal(c, _lattice.Join(c, c));
    }

    [Fact]
    public void Join_DifferentConsts_ReturnsTop()
    {
        var a = ConstantLatticeValue.Const(42);
        var b = ConstantLatticeValue.Const(100);
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(a, b));
    }

    [Fact]
    public void Join_ConstWithTop_ReturnsTop()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(c, ConstantLatticeValue.Top));
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(ConstantLatticeValue.Top, c));
    }

    [Fact]
    public void Join_TopWithTop_ReturnsTop()
    {
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(ConstantLatticeValue.Top, ConstantLatticeValue.Top));
    }

    [Fact]
    public void LessOrEqual_BottomIsLessOrEqualToAll()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.True(_lattice.LessOrEqual(ConstantLatticeValue.Bottom, ConstantLatticeValue.Bottom));
        Assert.True(_lattice.LessOrEqual(ConstantLatticeValue.Bottom, c));
        Assert.True(_lattice.LessOrEqual(ConstantLatticeValue.Bottom, ConstantLatticeValue.Top));
    }

    [Fact]
    public void LessOrEqual_TopIsGreaterOrEqualToAll()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.True(_lattice.LessOrEqual(ConstantLatticeValue.Bottom, ConstantLatticeValue.Top));
        Assert.True(_lattice.LessOrEqual(c, ConstantLatticeValue.Top));
        Assert.True(_lattice.LessOrEqual(ConstantLatticeValue.Top, ConstantLatticeValue.Top));
    }

    [Fact]
    public void LessOrEqual_ConstIsNotLessOrEqualToDifferentConst()
    {
        var a = ConstantLatticeValue.Const(42);
        var b = ConstantLatticeValue.Const(100);
        Assert.False(_lattice.LessOrEqual(a, b));
        Assert.False(_lattice.LessOrEqual(b, a));
    }

    [Fact]
    public void LessOrEqual_ConstIsLessOrEqualToItself()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.True(_lattice.LessOrEqual(c, c));
    }

    [Fact]
    public void LessOrEqual_TopIsNotLessOrEqualToConst()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.False(_lattice.LessOrEqual(ConstantLatticeValue.Top, c));
    }

    [Fact]
    public void LessOrEqual_ConstIsNotLessOrEqualToBottom()
    {
        var c = ConstantLatticeValue.Const(42);
        Assert.False(_lattice.LessOrEqual(c, ConstantLatticeValue.Bottom));
    }

    [Fact]
    public void Join_StringConsts()
    {
        var a = ConstantLatticeValue.Const("hello");
        var b = ConstantLatticeValue.Const("world");
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(a, b));
    }

    [Fact]
    public void Join_BoolConsts()
    {
        var t = ConstantLatticeValue.Const(true);
        var f = ConstantLatticeValue.Const(false);
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(t, f));
    }

    [Fact]
    public void Join_EnumConsts()
    {
        var a = ConstantLatticeValue.Const(StringComparison.Ordinal);
        var b = ConstantLatticeValue.Const(StringComparison.InvariantCulture);
        Assert.Equal(ConstantLatticeValue.Top, _lattice.Join(a, b));
    }

    [Fact]
    public void Axiom_JoinIsAssociative()
    {
        var values = new[]
        {
            ConstantLatticeValue.Bottom,
            ConstantLatticeValue.Const(1),
            ConstantLatticeValue.Const(2),
            ConstantLatticeValue.Top,
        };

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
        var values = new[]
        {
            ConstantLatticeValue.Bottom,
            ConstantLatticeValue.Const(1),
            ConstantLatticeValue.Const(2),
            ConstantLatticeValue.Top,
        };

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
        var values = new[]
        {
            ConstantLatticeValue.Bottom,
            ConstantLatticeValue.Const(1),
            ConstantLatticeValue.Top,
        };

        foreach (var a in values)
        {
            Assert.Equal(a, _lattice.Join(a, a));
        }
    }

    [Fact]
    public void Axiom_Absorption()
    {
        var values = new[]
        {
            ConstantLatticeValue.Bottom,
            ConstantLatticeValue.Const(1),
            ConstantLatticeValue.Const(2),
            ConstantLatticeValue.Top,
        };

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
