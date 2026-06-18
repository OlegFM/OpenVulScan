using Xunit;

namespace OpenVulScan.Tests;

public class IntervalLatticeTests
{
    private static readonly IntervalLattice _lattice = new();

    // A representative finite sample exercising ∅, ⊤, singletons, half-bounded, overlapping,
    // disjoint and negative ranges — enough to check the lattice axioms hold structurally.
    private static readonly IntervalValue[] _sample =
    {
        IntervalValue.Empty,
        IntervalValue.Top,
        IntervalValue.Constant(0),
        IntervalValue.Constant(5),
        IntervalValue.Constant(-3),
        IntervalValue.Range(0, 10),
        IntervalValue.Range(-5, 5),
        IntervalValue.Range(3, 8),
        IntervalValue.Range(long.MinValue, 0),
        IntervalValue.Range(0, long.MaxValue),
        IntervalValue.Range(100, 200),
    };

    // ── Bottom / Top ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bottom_IsEmpty()
    {
        Assert.Equal(IntervalValue.Empty, _lattice.Bottom);
    }

    [Fact]
    public void Top_IsUnbounded()
    {
        Assert.Equal(IntervalValue.Top, _lattice.Top);
    }

    // ── Join (convex hull) ──────────────────────────────────────────────────────────────

    [Fact]
    public void Join_IsConvexHull()
    {
        // Disjoint operands join to the smallest enclosing interval (the gap is filled).
        Assert.Equal(IntervalValue.Range(0, 15), _lattice.Join(IntervalValue.Range(0, 5), IntervalValue.Range(10, 15)));
    }

    [Fact]
    public void Join_WithEmpty_IsIdentity()
    {
        foreach (var v in _sample)
        {
            Assert.Equal(v, _lattice.Join(IntervalValue.Empty, v));
            Assert.Equal(v, _lattice.Join(v, IntervalValue.Empty));
        }
    }

    [Fact]
    public void Join_WithTop_IsTop()
    {
        foreach (var v in _sample)
        {
            Assert.Equal(IntervalValue.Top, _lattice.Join(v, IntervalValue.Top));
        }
    }

    // ── Meet / intersection (IntervalValue.Intersect) ───────────────────────────────────

    [Fact]
    public void Intersect_IsIntersection()
    {
        Assert.Equal(IntervalValue.Range(5, 10), IntervalValue.Range(0, 10).Intersect(IntervalValue.Range(5, 15)));
    }

    [Fact]
    public void Intersect_OfDisjointIntervals_IsEmpty()
    {
        Assert.Equal(IntervalValue.Empty, IntervalValue.Range(0, 5).Intersect(IntervalValue.Range(10, 15)));
    }

    [Fact]
    public void Intersect_WithTop_IsIdentity()
    {
        foreach (var v in _sample)
        {
            Assert.Equal(v, v.Intersect(IntervalValue.Top));
        }
    }

    // ── LessOrEqual (inclusion) ─────────────────────────────────────────────────────────

    [Fact]
    public void LessOrEqual_IsInclusion()
    {
        Assert.True(_lattice.LessOrEqual(IntervalValue.Range(2, 3), IntervalValue.Range(0, 10)));
        Assert.False(_lattice.LessOrEqual(IntervalValue.Range(0, 10), IntervalValue.Range(2, 3)));
    }

    [Fact]
    public void LessOrEqual_EmptyIsLeastElement()
    {
        foreach (var v in _sample)
        {
            Assert.True(_lattice.LessOrEqual(IntervalValue.Empty, v));
        }
    }

    [Fact]
    public void LessOrEqual_TopIsGreatestElement()
    {
        foreach (var v in _sample)
        {
            Assert.True(_lattice.LessOrEqual(v, IntervalValue.Top));
        }
    }

    [Fact]
    public void LessOrEqual_NonEmptyIsNeverBelowEmpty()
    {
        Assert.False(_lattice.LessOrEqual(IntervalValue.Range(0, 1), IntervalValue.Empty));
    }

    // ── Lattice axioms over the sample ──────────────────────────────────────────────────

    [Fact]
    public void Axiom_JoinIsAssociative()
    {
        foreach (var a in _sample)
        {
            foreach (var b in _sample)
            {
                foreach (var c in _sample)
                {
                    Assert.Equal(
                        _lattice.Join(_lattice.Join(a, b), c),
                        _lattice.Join(a, _lattice.Join(b, c)));
                }
            }
        }
    }

    [Fact]
    public void Axiom_JoinIsCommutative()
    {
        foreach (var a in _sample)
        {
            foreach (var b in _sample)
            {
                Assert.Equal(_lattice.Join(a, b), _lattice.Join(b, a));
            }
        }
    }

    [Fact]
    public void Axiom_JoinIsIdempotent()
    {
        foreach (var a in _sample)
        {
            Assert.Equal(a, _lattice.Join(a, a));
        }
    }

    [Fact]
    public void Axiom_MeetIsCommutativeAndIdempotent()
    {
        foreach (var a in _sample)
        {
            Assert.Equal(a, a.Intersect(a));
            foreach (var b in _sample)
            {
                Assert.Equal(a.Intersect(b), b.Intersect(a));
            }
        }
    }

    [Fact]
    public void Axiom_Absorption()
    {
        foreach (var a in _sample)
        {
            foreach (var b in _sample)
            {
                var join = _lattice.Join(a, b);
                Assert.True(_lattice.LessOrEqual(a, join));
                Assert.True(_lattice.LessOrEqual(b, join));

                var meet = a.Intersect(b);
                Assert.True(_lattice.LessOrEqual(meet, a));
                Assert.True(_lattice.LessOrEqual(meet, b));
            }
        }
    }

    [Fact]
    public void Axiom_LessOrEqualEquivalentToJoinIdentity()
    {
        foreach (var a in _sample)
        {
            foreach (var b in _sample)
            {
                Assert.Equal(_lattice.LessOrEqual(a, b), _lattice.Join(a, b) == b);
            }
        }
    }

    [Fact]
    public void Axiom_LessOrEqualEquivalentToMeetIdentity()
    {
        foreach (var a in _sample)
        {
            foreach (var b in _sample)
            {
                Assert.Equal(_lattice.LessOrEqual(a, b), a.Intersect(b) == a);
            }
        }
    }

    // ── Widening ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Widen_FromEmpty_ReturnsNext()
    {
        Assert.Equal(IntervalValue.Range(0, 5), _lattice.Widen(IntervalValue.Empty, IntervalValue.Range(0, 5)));
    }

    [Fact]
    public void Widen_GrowingUpperBound_JumpsToPositiveInfinity()
    {
        var widened = _lattice.Widen(IntervalValue.Constant(0), IntervalValue.Range(0, 1));
        Assert.Equal(IntervalValue.Range(0, IntervalValue.PositiveInfinity), widened);
        Assert.True(widened.UpperIsInfinite);
    }

    [Fact]
    public void Widen_ShrinkingLowerBound_JumpsToNegativeInfinity()
    {
        var widened = _lattice.Widen(IntervalValue.Range(0, 5), IntervalValue.Range(-2, 5));
        Assert.Equal(IntervalValue.Range(IntervalValue.NegativeInfinity, 5), widened);
        Assert.True(widened.LowerIsInfinite);
    }

    [Fact]
    public void Widen_StableBounds_AreUnchanged()
    {
        Assert.Equal(IntervalValue.Range(0, 10), _lattice.Widen(IntervalValue.Range(0, 10), IntervalValue.Range(0, 10)));
        // A narrower next never moves a bound outward, so the accumulated value is kept.
        Assert.Equal(IntervalValue.Range(0, 10), _lattice.Widen(IntervalValue.Range(0, 10), IntervalValue.Range(2, 8)));
    }

    [Fact]
    public void Widen_ReachesFixpointOnCountingLoop()
    {
        // Models `for (long i = 0; ; i++)` — the joined value strictly ascends ([0,0], [0,1],
        // [0,2], …) and would never converge under Join alone. Widening at the back-edge must
        // drive it to a fixpoint ([0, +∞]) in a bounded number of iterations.
        var accumulated = IntervalValue.Constant(0);

        const int safetyLimit = 16;
        int iterations = 0;
        while (true)
        {
            Assert.True(++iterations <= safetyLimit, "widening failed to converge");

            var incremented = accumulated.Add(IntervalValue.Constant(1));
            var joined = _lattice.Join(accumulated, incremented);
            var widened = _lattice.Widen(accumulated, joined);

            if (widened == accumulated)
                break;

            accumulated = widened;
        }

        Assert.Equal(IntervalValue.Range(0, IntervalValue.PositiveInfinity), accumulated);
        Assert.True(iterations <= 4);
    }
}
