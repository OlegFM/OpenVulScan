using System.Globalization;

namespace OpenVulScan;

/// <summary>
/// An element of the integer interval domain: either the empty interval ∅ (⊥) or a closed
/// range <c>[Lower, Upper]</c> over <see cref="long"/>, where the extremes double as
/// infinities (<see cref="NegativeInfinity"/> ≡ −∞ as a lower bound,
/// <see cref="PositiveInfinity"/> ≡ +∞ as an upper bound).
/// </summary>
/// <remarks>
/// <para>
/// Bounds are stored as <see cref="long"/>; <c>int</c> ranges embed exactly. Because the
/// domain is inherently bounded by the <see cref="long"/> range, the ∞ sentinels need no
/// special arithmetic: γ(lower = <see cref="long.MinValue"/>) already means "everything down
/// to <see cref="long.MinValue"/>", which is the same set whether the bound arose as a
/// sentinel or as the exact extreme. Endpoint arithmetic therefore computes the exact value
/// in <see cref="Int128"/> and clamps back into the <see cref="long"/> range — a sound
/// over-approximation under <em>saturating</em> semantics. Two's-complement wrap-around is
/// deliberately not modeled: a potential runtime overflow saturates to the corresponding
/// extreme instead of wrapping.
/// </para>
/// <para>
/// <c>default(IntervalValue)</c> is <see cref="Empty"/> (⊥).
/// </para>
/// <para>
/// This is the value carried by <see cref="IntervalLattice"/>. Order-theoretic operations
/// (join / meet / ≤ / widen) live on the lattice; the abstract transfer functions
/// (<see cref="Add"/>, <see cref="Multiply"/>, …) live here as they form the interval algebra.
/// </para>
/// </remarks>
public readonly struct IntervalValue : IEquatable<IntervalValue>
{
    /// <summary>The −∞ sentinel for a lower bound.</summary>
    public const long NegativeInfinity = long.MinValue;

    /// <summary>The +∞ sentinel for an upper bound.</summary>
    public const long PositiveInfinity = long.MaxValue;

    // Inverted flag so that default(IntervalValue) is Empty (⊥), not Constant(0) — a
    // defaulted struct silently meaning "exactly zero" is a trap for map lookups.
    private readonly bool _hasValue;
    private readonly long _lower;
    private readonly long _upper;

    private IntervalValue(bool hasValue, long lower, long upper)
    {
        _hasValue = hasValue;
        _lower = lower;
        _upper = upper;
    }

    /// <summary>The empty interval ∅ (⊥) — no values / not reachable on this path.</summary>
    public static IntervalValue Empty { get; } = new(hasValue: false, 0, 0);

    /// <summary>The greatest element ⊤ = <c>[−∞, +∞]</c> — any value.</summary>
    public static IntervalValue Top { get; } = new(hasValue: true, NegativeInfinity, PositiveInfinity);

    /// <summary>
    /// The closed range <c>[lower, upper]</c>, normalised to <see cref="Empty"/> when
    /// <paramref name="lower"/> &gt; <paramref name="upper"/>.
    /// </summary>
    public static IntervalValue Range(long lower, long upper)
        => lower > upper ? Empty : new IntervalValue(hasValue: true, lower, upper);

    /// <summary>The singleton interval <c>[value, value]</c>.</summary>
    public static IntervalValue Constant(long value) => new(hasValue: true, value, value);

    /// <summary>Gets a value indicating whether this is the empty interval ∅.</summary>
    public bool IsEmpty => !_hasValue;

    /// <summary>Gets a value indicating whether this is ⊤ = <c>[−∞, +∞]</c>.</summary>
    public bool IsTop => _hasValue && _lower == NegativeInfinity && _upper == PositiveInfinity;

    /// <summary>Gets the lower bound (valid only when <see cref="IsEmpty"/> is false).</summary>
    public long Lower => _lower;

    /// <summary>Gets the upper bound (valid only when <see cref="IsEmpty"/> is false).</summary>
    public long Upper => _upper;

    /// <summary>Gets a value indicating whether the lower bound is −∞.</summary>
    public bool LowerIsInfinite => _hasValue && _lower == NegativeInfinity;

    /// <summary>Gets a value indicating whether the upper bound is +∞.</summary>
    public bool UpperIsInfinite => _hasValue && _upper == PositiveInfinity;

    /// <summary>Determines whether <paramref name="value"/> lies within this interval.</summary>
    public bool Contains(long value) => _hasValue && _lower <= value && value <= _upper;

    // ── Abstract arithmetic (saturating, sound over-approximations) ─────────────────────

    /// <summary>Interval addition: <c>[a,b] + [c,d] = [a+c, b+d]</c>.</summary>
    public IntervalValue Add(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        return Range(SatAdd(_lower, other._lower), SatAdd(_upper, other._upper));
    }

    /// <summary>Interval negation: <c>-[a,b] = [-b, -a]</c>.</summary>
    public IntervalValue Negate()
    {
        if (IsEmpty)
            return Empty;

        return Range(NegateBound(_upper), NegateBound(_lower));
    }

    /// <summary>Interval subtraction: <c>[a,b] - [c,d] = [a-d, b-c]</c>.</summary>
    public IntervalValue Subtract(IntervalValue other) => Add(other.Negate());

    /// <summary>
    /// Interval multiplication: the convex hull of the four corner products,
    /// each computed exactly in <see cref="Int128"/> and clamped.
    /// </summary>
    public IntervalValue Multiply(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        long ac = SatMul(_lower, other._lower);
        long ad = SatMul(_lower, other._upper);
        long bc = SatMul(_upper, other._lower);
        long bd = SatMul(_upper, other._upper);

        long lo = Min4(ac, ad, bc, bd);
        long hi = Max4(ac, ad, bc, bd);
        return Range(lo, hi);
    }

    /// <summary>
    /// Interval division. If the divisor interval contains 0 the result is ⊤ (a division by
    /// zero is possible); otherwise the convex hull of the four corner quotients.
    /// </summary>
    public IntervalValue Divide(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        // Divisor straddles 0 ⇒ unknown / possible division by zero.
        if (other._lower <= 0 && other._upper >= 0)
            return Top;

        long ac = SatDiv(_lower, other._lower);
        long ad = SatDiv(_lower, other._upper);
        long bc = SatDiv(_upper, other._lower);
        long bd = SatDiv(_upper, other._upper);

        return Range(Min4(ac, ad, bc, bd), Max4(ac, ad, bc, bd));
    }

    /// <summary>
    /// The intersection (greatest lower bound / lattice <em>meet</em>) of two intervals:
    /// <c>[max(a,c), min(b,d)]</c>, or <see cref="Empty"/> when they are disjoint. The dual of
    /// the lattice join, used by branch refinement (e.g. on <c>x &lt; 10</c>, meet with
    /// <c>[−∞, 9]</c>).
    /// </summary>
    public IntervalValue Intersect(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        return Range(Math.Max(_lower, other._lower), Math.Min(_upper, other._upper));
    }

    // ── Abstract bitwise (coarse but sound) ─────────────────────────────────────────────

    /// <summary>
    /// Bitwise AND. Precise only when operands are provably non-negative
    /// (<c>x &amp; y ≤ min(x,y)</c>); otherwise ⊤.
    /// </summary>
    public IntervalValue BitwiseAnd(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        bool leftNonNeg = _lower >= 0;
        bool rightNonNeg = other._lower >= 0;

        if (leftNonNeg && rightNonNeg)
            return Range(0, Math.Min(_upper, other._upper));
        if (leftNonNeg)
            return Range(0, _upper);
        if (rightNonNeg)
            return Range(0, other._upper);

        return Top;
    }

    /// <summary>
    /// Bitwise OR. Precise only when operands are provably non-negative
    /// (<c>max(x,y) ≤ x | y ≤ x + y</c>); otherwise ⊤.
    /// </summary>
    public IntervalValue BitwiseOr(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        if (_lower >= 0 && other._lower >= 0)
            return Range(Math.Max(_lower, other._lower), SatAdd(_upper, other._upper));

        return Top;
    }

    /// <summary>
    /// Bitwise XOR. Precise only when operands are provably non-negative
    /// (<c>0 ≤ x ^ y ≤ x + y</c>); otherwise ⊤.
    /// </summary>
    public IntervalValue BitwiseXor(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return Empty;

        if (_lower >= 0 && other._lower >= 0)
            return Range(0, SatAdd(_upper, other._upper));

        return Top;
    }

    /// <summary>
    /// Arithmetic left shift by a constant <paramref name="count"/> (≡ multiply by
    /// <c>2^count</c>), saturating on overflow.
    /// </summary>
    public IntervalValue ShiftLeft(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 63);
        if (IsEmpty)
            return Empty;

        // << is monotone in the operand, so shifting both endpoints preserves the order.
        return Range(ShlBound(_lower, count), ShlBound(_upper, count));
    }

    /// <summary>
    /// Arithmetic right shift by a constant <paramref name="count"/>. <c>&gt;&gt;</c> is
    /// monotone, so the endpoints shift directly.
    /// </summary>
    public IntervalValue ShiftRight(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 63);
        if (IsEmpty)
            return Empty;

        long lo = IsNegInf(_lower) ? NegativeInfinity : _lower >> count;
        long hi = IsPosInf(_upper) ? PositiveInfinity : _upper >> count;
        return Range(lo, hi);
    }

    // ── Endpoint helpers (±∞-aware, saturating) ─────────────────────────────────────────

    private static bool IsNegInf(long x) => x == NegativeInfinity;

    private static bool IsPosInf(long x) => x == PositiveInfinity;

    private static long Clamp(Int128 value)
    {
        if (value <= NegativeInfinity)
            return NegativeInfinity;
        if (value >= PositiveInfinity)
            return PositiveInfinity;
        return (long)value;
    }

    // The domain is bounded by the long range, so a sentinel bound and the exact extreme
    // denote the same concrete set; endpoint arithmetic is exact in Int128, then clamped.
    // Infinity special cases would UNDER-approximate when a sentinel value sits on the
    // inner side of a bound (e.g. Constant(long.MinValue) + 10 must be MinValue + 10,
    // not [-∞, MinValue]).
    private static long SatAdd(long x, long y) => Clamp((Int128)x + y);

    private static long NegateBound(long x)
    {
        if (IsNegInf(x))
            return PositiveInfinity;
        if (IsPosInf(x))
            return NegativeInfinity;
        return -x;
    }

    private static long SatMul(long x, long y) => Clamp((Int128)x * y);

    // Caller guarantees the divisor interval excludes 0, so y != 0 here. Int128 division
    // truncates toward zero, matching C# long division; MinValue / -1 clamps to +∞.
    private static long SatDiv(long x, long y) => Clamp((Int128)x / y);

    private static long ShlBound(long x, int count)
    {
        if (IsNegInf(x))
            return NegativeInfinity;
        if (IsPosInf(x))
            return PositiveInfinity;
        return Clamp((Int128)x << count);
    }

    private static long Min4(long a, long b, long c, long d) => Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static long Max4(long a, long b, long c, long d) => Math.Max(Math.Max(a, b), Math.Max(c, d));

    // ── Equality / formatting ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool Equals(IntervalValue other)
    {
        if (IsEmpty || other.IsEmpty)
            return IsEmpty == other.IsEmpty;

        return _lower == other._lower && _upper == other._upper;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IntervalValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(_lower, _upper);

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsEmpty)
            return "∅";

        string lo = IsNegInf(_lower) ? "-∞" : _lower.ToString(CultureInfo.InvariantCulture);
        string hi = IsPosInf(_upper) ? "+∞" : _upper.ToString(CultureInfo.InvariantCulture);
        return $"[{lo}, {hi}]";
    }

    /// <summary>Determines whether two <see cref="IntervalValue"/> instances are equal.</summary>
    public static bool operator ==(IntervalValue left, IntervalValue right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="IntervalValue"/> instances are not equal.</summary>
    public static bool operator !=(IntervalValue left, IntervalValue right) => !left.Equals(right);
}
