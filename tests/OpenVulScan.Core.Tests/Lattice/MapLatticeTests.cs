using System.Collections.Immutable;
using Xunit;

namespace OpenVulScan.Tests;

public class MapLatticeTests
{
    private static readonly MapLattice<string, BoolFlatLattice, BoolLatticeValue> _lattice = new();

    [Fact]
    public void Join_WithDisjointKeys_MergesBoth()
    {
        var left = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("a", BoolLatticeValue.False);
        var right = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("b", BoolLatticeValue.True);

        var result = _lattice.Join(left, right);

        Assert.Equal(BoolLatticeValue.False, result["a"]);
        Assert.Equal(BoolLatticeValue.True, result["b"]);
    }

    [Fact]
    public void Join_WithOverlappingKeys_AppliesSubLatticeJoin()
    {
        var left = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.False);
        var right = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.True);

        var result = _lattice.Join(left, right);

        Assert.Equal(BoolLatticeValue.Top, result["x"]);
    }

    [Fact]
    public void Join_WithEmptyLeft_ReturnsRight()
    {
        var left = ImmutableDictionary<string, BoolLatticeValue>.Empty;
        var right = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("k", BoolLatticeValue.True);

        var result = _lattice.Join(left, right);

        Assert.Single(result);
        Assert.Equal(BoolLatticeValue.True, result["k"]);
    }

    [Fact]
    public void LessOrEqual_WhenAllKeysLessOrEqual_ReturnsTrue()
    {
        var left = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.Bottom)
            .Add("y", BoolLatticeValue.False);
        var right = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.False)
            .Add("y", BoolLatticeValue.Top);

        Assert.True(_lattice.LessOrEqual(left, right));
    }

    [Fact]
    public void LessOrEqual_WhenAnyKeyNotLessOrEqual_ReturnsFalse()
    {
        var left = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.False);
        var right = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.True);

        Assert.False(_lattice.LessOrEqual(left, right));
    }

    [Fact]
    public void Axiom_MapJoinIsAssociative()
    {
        var a = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.Bottom)
            .Add("y", BoolLatticeValue.False);
        var b = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.True)
            .Add("z", BoolLatticeValue.Bottom);
        var c = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("y", BoolLatticeValue.True)
            .Add("z", BoolLatticeValue.False);

        var left = _lattice.Join(_lattice.Join(a, b), c);
        var right = _lattice.Join(a, _lattice.Join(b, c));

        Assert.Equal(left, right);
    }

    [Fact]
    public void Axiom_MapJoinIsCommutative()
    {
        var a = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.False)
            .Add("y", BoolLatticeValue.Top);
        var b = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.True)
            .Add("z", BoolLatticeValue.Bottom);

        var left = _lattice.Join(a, b);
        var right = _lattice.Join(b, a);

        Assert.Equal(left, right);
    }

    [Fact]
    public void Axiom_MapJoinIsIdempotent()
    {
        var a = ImmutableDictionary<string, BoolLatticeValue>.Empty
            .Add("x", BoolLatticeValue.False)
            .Add("y", BoolLatticeValue.True);

        var result = _lattice.Join(a, a);

        Assert.Equal(a, result);
    }
}
