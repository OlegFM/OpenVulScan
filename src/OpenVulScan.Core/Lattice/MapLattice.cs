using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OpenVulScan;

/// <summary>
/// A product (map) lattice: each key maps to an element of a sub-lattice.
/// Join and partial order are applied point-wise.
/// </summary>
/// <typeparam name="TKey">The type of the map keys.</typeparam>
/// <typeparam name="TLattice">The type of the sub-lattice implementation.</typeparam>
/// <typeparam name="TValue">The element type of the sub-lattice.</typeparam>
public sealed class MapLattice<TKey, TLattice, TValue> : ILattice<ImmutableDictionary<TKey, TValue>>
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
}
