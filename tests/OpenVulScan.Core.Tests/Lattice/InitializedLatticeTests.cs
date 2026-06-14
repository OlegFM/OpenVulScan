using Xunit;

namespace OpenVulScan.Tests;

public class InitializedLatticeTests
{
    private static readonly InitializedLattice _lattice = new();

    private static readonly InitializationState[] _all =
    {
        InitializationState.Bottom,
        InitializationState.Uninit,
        InitializationState.Init,
        InitializationState.MaybeInit,
    };

    [Fact]
    public void Bottom_IsBottom()
    {
        Assert.Equal(InitializationState.Bottom, _lattice.Bottom);
    }

    [Fact]
    public void Top_IsMaybeInit()
    {
        Assert.Equal(InitializationState.MaybeInit, _lattice.Top);
    }

    [Theory]
    [InlineData(InitializationState.Bottom, InitializationState.Bottom, InitializationState.Bottom)]
    [InlineData(InitializationState.Bottom, InitializationState.Uninit, InitializationState.Uninit)]
    [InlineData(InitializationState.Bottom, InitializationState.Init, InitializationState.Init)]
    [InlineData(InitializationState.Bottom, InitializationState.MaybeInit, InitializationState.MaybeInit)]
    public void Join_WithBottom_ReturnsOther(InitializationState left, InitializationState right, InitializationState expected)
    {
        Assert.Equal(expected, _lattice.Join(left, right));
    }

    [Fact]
    public void Join_UninitAndInit_ReturnsMaybeInit()
    {
        Assert.Equal(InitializationState.MaybeInit, _lattice.Join(InitializationState.Uninit, InitializationState.Init));
        Assert.Equal(InitializationState.MaybeInit, _lattice.Join(InitializationState.Init, InitializationState.Uninit));
    }

    [Fact]
    public void Join_AnyWithMaybeInit_ReturnsMaybeInit()
    {
        foreach (var value in _all)
        {
            Assert.Equal(InitializationState.MaybeInit, _lattice.Join(value, InitializationState.MaybeInit));
        }
    }

    [Fact]
    public void Join_SameValue_ReturnsSameValue()
    {
        foreach (var value in _all)
        {
            Assert.Equal(value, _lattice.Join(value, value));
        }
    }

    [Fact]
    public void LessOrEqual_BottomIsLessOrEqualToAll()
    {
        foreach (var value in _all)
        {
            Assert.True(_lattice.LessOrEqual(InitializationState.Bottom, value));
        }
    }

    [Fact]
    public void LessOrEqual_MaybeInitIsGreaterOrEqualToAll()
    {
        foreach (var value in _all)
        {
            Assert.True(_lattice.LessOrEqual(value, InitializationState.MaybeInit));
        }
    }

    [Fact]
    public void LessOrEqual_UninitAndInit_AreIncomparable()
    {
        Assert.False(_lattice.LessOrEqual(InitializationState.Uninit, InitializationState.Init));
        Assert.False(_lattice.LessOrEqual(InitializationState.Init, InitializationState.Uninit));
    }

    [Fact]
    public void LessOrEqual_MaybeInitIsNotLessOrEqualToConcreteStates()
    {
        Assert.False(_lattice.LessOrEqual(InitializationState.MaybeInit, InitializationState.Uninit));
        Assert.False(_lattice.LessOrEqual(InitializationState.MaybeInit, InitializationState.Init));
        Assert.False(_lattice.LessOrEqual(InitializationState.MaybeInit, InitializationState.Bottom));
    }

    [Fact]
    public void Axiom_JoinIsAssociative()
    {
        foreach (var a in _all)
        {
            foreach (var b in _all)
            {
                foreach (var c in _all)
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
        foreach (var a in _all)
        {
            foreach (var b in _all)
            {
                Assert.Equal(_lattice.Join(a, b), _lattice.Join(b, a));
            }
        }
    }

    [Fact]
    public void Axiom_JoinIsIdempotent()
    {
        foreach (var a in _all)
        {
            Assert.Equal(a, _lattice.Join(a, a));
        }
    }

    [Fact]
    public void Axiom_Absorption()
    {
        foreach (var a in _all)
        {
            foreach (var b in _all)
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
        foreach (var a in _all)
        {
            foreach (var b in _all)
            {
                var le = _lattice.LessOrEqual(a, b);
                var joinIsB = _lattice.Join(a, b) == b;
                Assert.Equal(le, joinIsB);
            }
        }
    }
}
