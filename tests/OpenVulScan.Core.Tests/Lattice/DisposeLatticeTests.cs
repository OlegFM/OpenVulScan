using Xunit;

namespace OpenVulScan.Tests;

public class DisposeLatticeTests
{
    private static readonly DisposeLattice _lattice = new();

    private static readonly DisposeState[] _all =
    {
        DisposeState.Live,
        DisposeState.Disposed,
        DisposeState.DoubleDisposed,
    };

    [Fact]
    public void Bottom_IsLive()
    {
        Assert.Equal(DisposeState.Live, _lattice.Bottom);
    }

    [Fact]
    public void Top_IsDoubleDisposed()
    {
        Assert.Equal(DisposeState.DoubleDisposed, _lattice.Top);
    }

    [Theory]
    [InlineData(DisposeState.Live, DisposeState.Live, DisposeState.Live)]
    [InlineData(DisposeState.Live, DisposeState.Disposed, DisposeState.Disposed)]
    [InlineData(DisposeState.Live, DisposeState.DoubleDisposed, DisposeState.DoubleDisposed)]
    public void Join_WithLive_ReturnsOther(DisposeState left, DisposeState right, DisposeState expected)
    {
        Assert.Equal(expected, _lattice.Join(left, right));
    }

    [Fact]
    public void Join_DisposedAndDoubleDisposed_ReturnsDoubleDisposed()
    {
        Assert.Equal(DisposeState.DoubleDisposed, _lattice.Join(DisposeState.Disposed, DisposeState.DoubleDisposed));
        Assert.Equal(DisposeState.DoubleDisposed, _lattice.Join(DisposeState.DoubleDisposed, DisposeState.Disposed));
    }

    [Fact]
    public void Join_AnyWithDoubleDisposed_ReturnsDoubleDisposed()
    {
        foreach (var value in _all)
        {
            Assert.Equal(DisposeState.DoubleDisposed, _lattice.Join(value, DisposeState.DoubleDisposed));
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
    public void LessOrEqual_LiveIsLessOrEqualToAll()
    {
        foreach (var value in _all)
        {
            Assert.True(_lattice.LessOrEqual(DisposeState.Live, value));
        }
    }

    [Fact]
    public void LessOrEqual_DoubleDisposedIsGreaterOrEqualToAll()
    {
        foreach (var value in _all)
        {
            Assert.True(_lattice.LessOrEqual(value, DisposeState.DoubleDisposed));
        }
    }

    [Fact]
    public void LessOrEqual_FormsAscendingChain()
    {
        // Unlike the diamond-shaped InitializedLattice, the dispose states are totally
        // ordered: Live ⊑ Disposed ⊑ DoubleDisposed. The middle relation is what makes
        // this a chain rather than a flat lattice.
        Assert.True(_lattice.LessOrEqual(DisposeState.Live, DisposeState.Disposed));
        Assert.True(_lattice.LessOrEqual(DisposeState.Disposed, DisposeState.DoubleDisposed));
        Assert.True(_lattice.LessOrEqual(DisposeState.Live, DisposeState.DoubleDisposed));
    }

    [Fact]
    public void LessOrEqual_HigherStatesAreNotLessOrEqualToLower()
    {
        Assert.False(_lattice.LessOrEqual(DisposeState.Disposed, DisposeState.Live));
        Assert.False(_lattice.LessOrEqual(DisposeState.DoubleDisposed, DisposeState.Disposed));
        Assert.False(_lattice.LessOrEqual(DisposeState.DoubleDisposed, DisposeState.Live));
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
