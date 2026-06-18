# Design: IntervalLattice for int/long

- **Date:** 2026-06-18
- **Beads issue:** ovs-2qi.4 (Phase 2 — intra-procedural DFA + path-sensitive)
- **Status:** approved-pending-review
- **Consumes:** `ILattice<T>`, `WorklistSolver` (future widening integration)

## 1. Goal

An integer **interval domain** `[lo, hi]` over `int`/`long` with a **widening** operator so
fixpoint iteration over loops terminates, plus abstract `+ − × ÷` and bitwise transfer
functions. Foundation for future range rules (array out-of-bounds, integer overflow,
shift-count, division-by-zero refinement).

## 2. Why this lattice is different — infinite height ⇒ widening

Every prior lattice (`NullStateLattice`, `DisposeLattice`, `InitializedLattice`,
`ResourceOwnershipLattice`) has height ≤ 3, so the worklist solver's ascending chains
stabilise trivially. Intervals do **not**: `[0,0] ⊏ [0,1] ⊏ [0,2] ⊏ …` is an infinite
strictly-ascending chain. A monotone solver using only `Join` would never converge on a
counting loop. Hence a **widening** operator `∇` is mandatory (Cousot & Cousot): on a
back-edge, any bound that moved outward is jumped straight to ±∞, guaranteeing each bound
changes at most once (finite → ∞). This trades precision for termination.

`Widen` lives on a new marker interface `IWideningLattice<T> : ILattice<T>` so a future
solver can detect "this domain needs widening at loop heads" without special-casing types.

## 3. Representation (KISS)

`IntervalValue` is a `readonly struct`:

- `IsEmpty` (∅ = ⊥) — no values; also "not reachable / not yet computed on this path".
- `Lower`, `Upper` as `long`, with **`long.MinValue` ≡ −∞** (lower) and
  **`long.MaxValue` ≡ +∞** (upper). Top (⊤) = `[−∞, +∞]`.
- `Range(lo, hi)` normalises `lo > hi` to ∅; `Constant(v)` = `[v, v]`.

**Decision — sentinel infinities (saturation):** the two `long` extremes double as ±∞.
A real value reaching `long.MaxValue`/`long.MinValue` is treated as unbounded — a *sound*
over-approximation. Consequence: the exact extreme longs lose precision (treated as ∞).
Acceptable for a SAST range domain; avoids carrying separate infinity flags. `long` is the
internal width, so `int` ranges embed exactly.

## 4. Lattice operations

- `Join([a,b],[c,d]) = [min(a,c), max(b,d)]` — convex hull (smallest interval containing
  both), with ∅ as identity. NOT set-union: the domain only holds convex intervals.
- `Meet([a,b],[c,d]) = [max(a,c), min(b,d)]` or ∅ if disjoint — intersection, the dual.
  Provided for future edge-refinement (`x < 10` ⇒ meet with `[−∞, 9]`).
- `LessOrEqual = ⊆` (subset), with ∅ ≤ everything. Consistent with `Join`:
  `le(x,y) ⇔ Join(x,y) == y`.
- `Widen(prev, next)`: `lo = next.lo < prev.lo ? −∞ : prev.lo`,
  `hi = next.hi > prev.hi ? +∞ : prev.hi` (∅-identity on `prev`).

## 5. Abstract arithmetic (on `IntervalValue`, saturating)

All ops are sound interval transfer functions; intermediate products of two `long`s fit in
`Int128` (max ≈ 8.5e37 < Int128.Max ≈ 1.7e38) and are clamped back to `long`, saturating
overflow to ±∞. Infinities are handled explicitly before the finite `Int128` path.

- `Add`, `Subtract`, `Negate` — endpoint arithmetic; `Subtract = Add(a, Negate(b))`.
- `Multiply` — min/max of the four corner products (sign-aware; `∞ × 0 = 0`).
- `Divide` — if the divisor interval contains 0 ⇒ **⊤** (KISS; division-by-zero possible,
  not split). Otherwise min/max of the four corner quotients (`finite/∞ = 0`,
  `∞/finite = ±∞`, `∞/∞ = ±∞` — sound, loose).

## 6. Abstract bitwise (coarse but sound)

Bit operations are deliberately imprecise; precision only when operands are provably
non-negative, otherwise ⊤:

- `BitwiseAnd`: both ≥ 0 ⇒ `[0, min(b,d)]`; one ≥ 0 ⇒ `[0, that upper]`; else ⊤
  (`x & y ≤ min(x,y)`).
- `BitwiseOr`: both ≥ 0 ⇒ `[max(a,c), b+d]`; else ⊤ (`max(x,y) ≤ x|y ≤ x+y`).
- `BitwiseXor`: both ≥ 0 ⇒ `[0, b+d]`; else ⊤ (`x ^ y ≤ x|y ≤ x+y`).
- `ShiftLeft(k)` / `ShiftRight(k)` by a constant `k ≥ 0` ⇒ multiply / divide by `2^k`
  (arithmetic shift on the interval), reusing `Multiply`/`Divide`.

## 7. Testing

`xUnit`, ≥ 15 cases (acceptance criterion). Two files mirroring the existing lattice test
style:

- `IntervalLatticeTests` — axioms (assoc/comm/idem/absorption + le≡join-identity) over a
  representative sample of intervals (∅, ⊤, singletons, half-bounded, overlapping,
  disjoint); `Meet`; and **widening reaches a fixpoint** on a simulated counting loop.
- `IntervalValueTests` — construction/normalisation, `Add/Subtract/Multiply/Divide/Negate`
  incl. saturation and division-by-zero-interval ⇒ ⊤, and the bitwise ops.

## 8. Follow-ups (file as beads issues)

- Integrate `IWideningLattice` into `WorklistSolver` (widen at loop-header back-edges,
  with a delay/threshold + optional narrowing pass to recover precision).
- First range rule consuming the domain (e.g. array index out-of-bounds, V3106-family).
- Refine `Divide` to exclude the exact 0 from a divisor interval rather than ⊤.
