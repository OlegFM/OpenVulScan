using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OpenVulScan;

/// <summary>
/// A product (map) lattice: each key maps to an element of a sub-lattice.
/// Join, partial order, and widening are applied point-wise.
/// </summary>
/// <typeparam name="TKey">The type of the map keys.</typeparam>
/// <typeparam name="TLattice">The type of the sub-lattice implementation.</typeparam>
/// <typeparam name="TValue">The element type of the sub-lattice.</typeparam>
/// <remarks>
/// The map lattice always implements <see cref="IWideningLattice{T}"/>: when the sub-lattice
/// itself widens, <see cref="Widen"/> delegates point-wise; otherwise it degrades to the
/// point-wise <see cref="Join"/>, which is a valid widening for finite-height sub-lattices.
/// The key set is bounded by the variables of the analysed method, so only the per-key value
/// chains can be infinite.
/// </remarks>
public sealed class MapLattice<TKey, TLattice, TValue> : IWideningLattice<ImmutableDictionary<TKey, TValue>>
    where TKey : notnull
    where TLattice : ILattice<TValue>, new()
{
    private readonly TLattice _subLattice = new();

    /// <inheritdoc />
    public ImmutableDictionary<TKey, TValue> Bottom => ImmutableDictionary<TKey, TValue>.Empty;

    /// <inheritdoc />
    public ImmutableDictionary<TKey, TValue> Top
    {
        get
        {
            throw new InvalidOperationException(
                "MapLattice does not have a finite Top element because the key set is unbounded.");
        }
    }

    /// <summary>
    /// Computes the point-wise join of two maps.
    /// </summary>
    /// <param name="left">The first map.</param>
    /// <param name="right">The second map.</param>
    /// <returns>A new map containing the join of each key present in either map.</returns>
    public ImmutableDictionary<TKey, TValue> Join(
        ImmutableDictionary<TKey, TValue> left,
        ImmutableDictionary<TKey, TValue> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var builder = left.ToBuilder();

        foreach (var kvp in right)
        {
            if (builder.TryGetValue(kvp.Key, out var existing))
            {
                builder[kvp.Key] = _subLattice.Join(existing, kvp.Value);
            }
            else
            {
                builder[kvp.Key] = kvp.Value;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Determines whether <paramref name="left"/> is point-wise less than or
    /// equal to <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The left-hand map.</param>
    /// <param name="right">The right-hand map.</param>
    /// <returns>
    /// <see langword="true"/> if every key in <paramref name="left"/> is also
    /// in <paramref name="right"/> and satisfies the sub-lattice order;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public bool LessOrEqual(
        ImmutableDictionary<TKey, TValue> left,
        ImmutableDictionary<TKey, TValue> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        foreach (var kvp in left)
        {
            if (!right.TryGetValue(kvp.Key, out var rightValue))
                return false;

            if (!_subLattice.LessOrEqual(kvp.Value, rightValue))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Widens <paramref name="previous"/> towards <paramref name="incoming"/> point-wise.
    /// Keys present only in one argument keep their value (widening against the sub-lattice
    /// ⊥ is the value itself).
    /// </summary>
    /// <param name="previous">The previous iterate (the accumulated map).</param>
    /// <param name="incoming">The newly produced map.</param>
    /// <returns>The widened map, an upper bound of both arguments.</returns>
    public ImmutableDictionary<TKey, TValue> Widen(
        ImmutableDictionary<TKey, TValue> previous,
        ImmutableDictionary<TKey, TValue> incoming)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(incoming);

        if (_subLattice is not IWideningLattice<TValue> wideningSubLattice)
            return Join(previous, incoming);

        var builder = previous.ToBuilder();

        foreach (var kvp in incoming)
        {
            builder[kvp.Key] = builder.TryGetValue(kvp.Key, out var existing)
                ? wideningSubLattice.Widen(existing, kvp.Value)
                : kvp.Value;
        }

        return builder.ToImmutable();
    }
}
