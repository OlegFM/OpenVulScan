using Xunit;

namespace OpenVulScan.Tests;

public class ResourceOwnershipLatticeTests
{
    private static readonly ResourceOwnershipLattice _lattice = new();

    private static readonly OwnershipState[] _all =
    {
        OwnershipState.Untracked,
        OwnershipState.Disposed,
        OwnershipState.Open,
    };

    [Fact]
    public void Bottom_IsUntracked() => Assert.Equal(OwnershipState.Untracked, _lattice.Bottom);

    [Fact]
    public void Top_IsOpen() => Assert.Equal(OwnershipState.Open, _lattice.Top);

    [Theory]
    [InlineData(OwnershipState.Untracked, OwnershipState.Disposed, OwnershipState.Disposed)]
    [InlineData(OwnershipState.Untracked, OwnershipState.Open, OwnershipState.Open)]
    // Partial dispose: one path disposes, the other leaks ⇒ the merge must stay Open (⊤).
    [InlineData(OwnershipState.Disposed, OwnershipState.Open, OwnershipState.Open)]
    [InlineData(OwnershipState.Disposed, OwnershipState.Disposed, OwnershipState.Disposed)]
    public void Join_FollowsChainMax(OwnershipState a, OwnershipState b, OwnershipState expected)
    {
        Assert.Equal(expected, _lattice.Join(a, b));
        Assert.Equal(expected, _lattice.Join(b, a));
    }

    [Fact]
    public void Join_WithUntracked_ReturnsOther()
    {
        foreach (var v in _all)
            Assert.Equal(v, _lattice.Join(OwnershipState.Untracked, v));
    }

    [Fact]
    public void LessOrEqual_FormsAscendingChain()
    {
        Assert.True(_lattice.LessOrEqual(OwnershipState.Untracked, OwnershipState.Disposed));
        Assert.True(_lattice.LessOrEqual(OwnershipState.Disposed, OwnershipState.Open));
        Assert.True(_lattice.LessOrEqual(OwnershipState.Untracked, OwnershipState.Open));
    }

    [Fact]
    public void LessOrEqual_HigherStatesNotLessOrEqualToLower()
    {
        Assert.False(_lattice.LessOrEqual(OwnershipState.Disposed, OwnershipState.Untracked));
        Assert.False(_lattice.LessOrEqual(OwnershipState.Open, OwnershipState.Disposed));
        Assert.False(_lattice.LessOrEqual(OwnershipState.Open, OwnershipState.Untracked));
    }

    [Fact]
    public void Axiom_JoinIsAssociative()
    {
        foreach (var a in _all)
            foreach (var b in _all)
                foreach (var c in _all)
                    Assert.Equal(
                        _lattice.Join(_lattice.Join(a, b), c),
                        _lattice.Join(a, _lattice.Join(b, c)));
    }

    [Fact]
    public void Axiom_JoinIsCommutative()
    {
        foreach (var a in _all)
            foreach (var b in _all)
                Assert.Equal(_lattice.Join(a, b), _lattice.Join(b, a));
    }

    [Fact]
    public void Axiom_JoinIsIdempotent()
    {
        foreach (var a in _all)
            Assert.Equal(a, _lattice.Join(a, a));
    }

    [Fact]
    public void Axiom_Absorption()
    {
        foreach (var a in _all)
            foreach (var b in _all)
            {
                var join = _lattice.Join(a, b);
                Assert.True(_lattice.LessOrEqual(a, join));
                Assert.True(_lattice.LessOrEqual(b, join));
            }
    }

    [Fact]
    public void Axiom_LessOrEqualEquivalentToJoinIdentity()
    {
        foreach (var a in _all)
            foreach (var b in _all)
                Assert.Equal(_lattice.LessOrEqual(a, b), _lattice.Join(a, b) == b);
    }
}
