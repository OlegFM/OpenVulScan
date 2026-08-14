# Interval SSA pipeline + V3106 (intra, definite OOB) — design

**Bead:** ovs-kmj. **Date:** 2026-08-14. **Status:** Accepted.

## 1. Goal

Give the merged `IntervalValue`/`IntervalLattice` (ovs-2qi.4) its first consumer: an
SSA-keyed interval dataflow pipeline mirroring the Constant pipeline, and the intra-method
rule **V3106** flagging array element accesses whose index is *definitely* out of bounds.

## 2. Architecture (mirrors the Constant pipeline 1:1)

| Constant pipeline (precedent)  | Interval pipeline (this design)  |
|---|---|
| `ConstantSsaTransfer`          | `IntervalSsaTransfer`            |
| `ConstantSsaEdgeRefiner`       | `IntervalSsaEdgeRefiner`         |
| `ConstantSsaEvaluator`         | `IntervalSsaEvaluator`           |
| `V3022` (`DataFlowRule<ImmutableDictionary<SsaId, ConstantLatticeValue>>`) | `V3106IndexOutOfBounds` (`DataFlowRule<ImmutableDictionary<SsaId, IntervalValue>>`) |

State: `ImmutableDictionary<SsaId, IntervalValue>` under
`MapLattice<SsaId, IntervalLattice, IntervalValue>`. The map lattice widens point-wise
(ovs-i1l), so the solver terminates on counting loops — the pipeline depends on that.

## 3. Key decisions

1. **Array variables track their LENGTH.** `Evaluate(IArrayCreationOperation)` returns the
   interval of `DimensionSizes[0]`. For an array-typed def the stored interval *is* the
   length; array aliases (`var b = a;`) propagate it via the normal SSA lookup. Documented
   on the evaluator. Rank > 1 or unknown size ⇒ ⊤.
2. **Definite-only V3106 (anti-FP v1):** report only when every possible index is outside
   every possible bound: `index.Lower ≥ length.Upper` (both finite) or `index.Upper < 0`.
   MAY semantics ("possibly out of bound", PVS wording) is a follow-up once corpus FP-rate
   is measurable.
3. **Refiner handles relational guards** `x < c`, `x <= c`, `x > c`, `x >= c`, `x == c`
   (literal on either side, operands swapped ⇒ mirrored operator) by `Intersect` with the
   half-range; `!`, `&&`, `||` recurse as in `ConstantSsaEdgeRefiner`; `x != c` refines only
   its false-edge (equality). Meet semantics: contradiction ⇒ ∅ (dead edge stays inert).
   Endpoint `c±1` is computed in `Int128` and clamped (no wrap at `long` extremes).
4. **Numeric literal widening to `long`:** sbyte/byte/short/ushort/int/uint/long/char embed
   exactly; `ulong` values above `long.MaxValue` ⇒ ⊤.
5. Binary `+ - * /` and unary `-` evaluate through the `IntervalValue` algebra; everything
   else ⇒ ⊤. `%`, shifts, bitwise are follow-ups (the algebra exists, the evaluator wiring
   is deliberately minimal).

## 4. Why the loop case is safe (pipeline sanity check)

`var a = new int[10]; for (int i = 0; i < 10; i++) a[i] = 0;`
Widening drives `i`'s loop-header interval to `[0, +∞]`; the `i < 10` true-edge refinement
intersects to `[0, 9]` before the body — so `a[i]` sees an in-bounds index and V3106 stays
silent. Flagging requires a guard proving the index out (early-return style) or a constant.

## 5. Out of scope (follow-ups)

- `a.Length` / `Array.Length` property tracking; `Span<T>`/indexers/`^`-from-end.
- MAY-semantics V3106; inter-procedural V3106 (ovs-xwx.13).
- `%`, shifts, bitwise in the evaluator; `checked` semantics (overflow saturates, no wrap).
