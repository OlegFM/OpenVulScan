using Xunit;

namespace OpenVulScan.Tests;

public class NullStateLatticeTests
{
    private static readonly NullStateLattice _lattice = new();

    [Fact]
    public void Bottom_IsUnknown()
    {
        Assert.Equal(NullState.Unknown, _lattice.Bottom);
    }

    [Fact]
    public void Top_IsMaybeNull()
    {
        Assert.Equal(NullState.MaybeNull, _lattice.Top);
    }

    [Theory]
    [InlineData(NullState.Unknown, NullState.Unknown, NullState.Unknown)]
    [InlineData(NullState.Unknown, NullState.DefinitelyNull, NullState.DefinitelyNull)]
    [InlineData(NullState.Unknown, NullState.NotNull, NullState.NotNull)]
    [InlineData(NullState.Unknown, NullState.MaybeNull, NullState.MaybeNull)]
    public void Join_WithUnknown_ReturnsOther(NullState left, NullState right, NullState expected)
    {
        Assert.Equal(expected, _lattice.Join(left, right));
    }

    [Fact]
    public void Join_DefinitelyNullAndNotNull_ReturnsMaybeNull()
    {
        Assert.Equal(NullState.MaybeNull, _lattice.Join(NullState.DefinitelyNull, NullState.NotNull));
        Assert.Equal(NullState.MaybeNull, _lattice.Join(NullState.NotNull, NullState.DefinitelyNull));
    }

    [Fact]
    public void Join_AnyWithMaybeNull_ReturnsMaybeNull()
    {
        foreach (var value in new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull })
        {
            Assert.Equal(NullState.MaybeNull, _lattice.Join(value, NullState.MaybeNull));
        }
    }

    [Fact]
    public void Join_SameValue_ReturnsSameValue()
    {
        foreach (var value in new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull })
        {
            Assert.Equal(value, _lattice.Join(value, value));
        }
    }

    [Fact]
    public void LessOrEqual_UnknownIsLessOrEqualToAll()
    {
        foreach (var value in new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull })
        {
            Assert.True(_lattice.LessOrEqual(NullState.Unknown, value));
        }
    }

    [Fact]
    public void LessOrEqual_MaybeNullIsGreaterOrEqualToAll()
    {
        foreach (var value in new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull })
        {
            Assert.True(_lattice.LessOrEqual(value, NullState.MaybeNull));
        }
    }

    [Fact]
    public void LessOrEqual_DefinitelyNullAndNotNull_AreIncomparable()
    {
        Assert.False(_lattice.LessOrEqual(NullState.DefinitelyNull, NullState.NotNull));
        Assert.False(_lattice.LessOrEqual(NullState.NotNull, NullState.DefinitelyNull));
    }

    [Fact]
    public void LessOrEqual_DefinitelyNullIsLessOrEqualToMaybeNull()
    {
        Assert.True(_lattice.LessOrEqual(NullState.DefinitelyNull, NullState.MaybeNull));
    }

    [Fact]
    public void LessOrEqual_NotNullIsLessOrEqualToMaybeNull()
    {
        Assert.True(_lattice.LessOrEqual(NullState.NotNull, NullState.MaybeNull));
    }

    [Fact]
    public void LessOrEqual_MaybeNullIsNotLessOrEqualToConcreteStates()
    {
        Assert.False(_lattice.LessOrEqual(NullState.MaybeNull, NullState.DefinitelyNull));
        Assert.False(_lattice.LessOrEqual(NullState.MaybeNull, NullState.NotNull));
        Assert.False(_lattice.LessOrEqual(NullState.MaybeNull, NullState.Unknown));
    }

    [Fact]
    public void Axiom_JoinIsAssociative()
    {
        var values = new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull };

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
        var values = new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull };

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
        var values = new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull };

        foreach (var a in values)
        {
            Assert.Equal(a, _lattice.Join(a, a));
        }
    }

    [Fact]
    public void Axiom_Absorption()
    {
        var values = new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull };

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

    [Fact]
    public void Axiom_LessOrEqualEquivalentToJoinIdentity()
    {
        var values = new[] { NullState.Unknown, NullState.DefinitelyNull, NullState.NotNull, NullState.MaybeNull };

        foreach (var a in values)
        {
            foreach (var b in values)
            {
                var le = _lattice.LessOrEqual(a, b);
                var joinIsB = _lattice.Join(a, b) == b;
                Assert.Equal(le, joinIsB);
            }
        }
    }
}
