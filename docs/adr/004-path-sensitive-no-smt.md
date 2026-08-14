# ADR-004: Path-Sensitivity via Edge-Condition Refinement, Not SMT

**Status:** Accepted

**Date:** 2026-08-14

**Deciders:** OpenVulScan Core Team

**References:**
- `src/OpenVulScan.Core/Cfg/WorklistSolver.cs` — fixpoint solver with edge refinement and widening
- `src/OpenVulScan.Core/Lattice/ConstantSsaEdgeRefiner.cs`, `IntervalSsaEdgeRefiner.cs`, `NullStateSsaEdgeRefiner.cs` — shipped refiners
- ADR-001 (architecture overview), design notes `docs/superpowers/specs/2026-08-14-interval-ssa-pipeline-v3106-design.md`

---

## Context

Rules like V3022 (always true/false), V3080 (null dereference), V3106 (index out of
bounds) need *path-sensitive* facts: the value of `x` inside `if (x != null)` differs from
its value in the `else` branch. Classic options:

1. **SMT-backed symbolic execution** (Z3 or similar): encode path conditions as logical
   formulas, ask a solver for satisfiability per path.
2. **Path enumeration / state splitting**: fork the abstract state at every branch and
   propagate per-path states (bounded by a split budget).
3. **Edge-condition refinement**: stay flow-sensitive (one state per CFG edge join), but
   *narrow* the predecessor's out-state along each conditional edge by the facts the branch
   condition implies.

## Decision

OpenVulScan implements path-sensitivity through **edge-condition refinement over SSA-keyed
lattice states** — option 3. No SMT solver, no path enumeration.

Mechanics, as built:

- `WorklistSolver<T>` accepts an optional `IEdgeRefiner<T>`. When computing a block's IN
  state it refines each predecessor's OUT state along the specific `ControlFlowBranch`
  *before* joining (`ComputeInState`).
- A refiner parses the branch condition (`ConditionKind` + `BranchValue`), recursing
  through `!`, `&&`, `||`, and applies the implied fact to the **SSA version** the condition
  actually tests: equality guards pin constants (`ConstantSsaEdgeRefiner`), relational
  guards intersect half-ranges (`IntervalSsaEdgeRefiner`), null checks flip null-states
  (`NullStateSsaEdgeRefiner`, `OwnershipNullGuardEdgeRefiner`).
- Refinement is a lattice **meet**: a fact that contradicts the current value collapses it
  to ⊥, so an infeasible (dead) edge cannot manufacture downstream false positives — this
  substitutes for SMT unsatisfiability on the patterns we care about.
- SSA versioning (semi-pruned, φ at joins) is what makes the narrowing *stick*: the
  refined fact attaches to the tested version and is never confused with a later
  reassignment.
- Two solver-level guards keep the composition sound and terminating:
  - infinite-height lattices (intervals) opt into `IWideningLattice<T>` and are widened at
    back-edge targets; refinement then re-narrows *below* the widened header state on the
    body edge (`for (i = 0; i < 10; …) a[i]` — header `[0, +∞]`, body `[0, 9]`);
  - joins skip predecessors whose out-state is not yet computed, because
    `refine(⊥)` must stay ⊥ and an SSA map cannot distinguish "unvisited" from "empty"
    (see the WorklistSolver comment; found the hard way via ovs-kmj).

## Consequences

### Positive
- **Linear complexity.** One state per block IN/OUT; refinement is O(condition size) per
  edge. No exponential path explosion, hence no need for a path budget or a fallback mode —
  the only global guard is the solver's `maxIterations` (default 100 000 block visits).
  A 70-branch method is ~2 visits per block, far below the ceiling (stress test:
  `SolverStressTests`).
- **Zero dependencies.** No Z3 native binaries, no marshalling layer, no solver timeouts;
  `dotnet build` stays self-contained (consistent with ADR-002's dependency philosophy).
- **Deterministic and debuggable.** A refinement is a pure function of one edge; state
  dumps localize precision bugs quickly.
- **Compositional.** Any `DataFlowRule<TLattice>` gains path-sensitivity by supplying a
  refiner; the dispatcher still runs one shared solve per lattice type.

### Negative
- **No cross-variable correlation.** `if (a == b)` teaches us nothing relational; only
  facts of the form ⟨tracked key⟩ ⟨op⟩ ⟨constant/null⟩ refine. Patterns SMT would prove
  (`if (a < b && b < a)` infeasible) are out of reach.
- **Convex domains only narrow convexly.** `x != 5` refines nothing on the true edge in the
  interval domain (holes are unrepresentable); equality-only facts go to the constant
  domain instead.
- **Merge loses path identity.** After the join, facts proven on one side are gone unless
  both sides agree — deliberate: correlation-after-merge is exactly where path explosion
  starts (PVS-Studio's own V3022 docs describe the same trade-off).

## Alternatives Considered

### Z3 / SMT-backed symbolic execution
Rejected: a native dependency with per-OS binaries, unpredictable solve times needing
timeout plumbing, and formula-encoding complexity dwarfing the analyses it would serve. The
diagnostic classes in scope (constant folds, null guards, ranges) are all expressible as
per-edge lattice meets; SMT buys generality the ~200-rule PVS-parity target does not need.

### Bounded path enumeration (split at branches, cap at N, merge on overflow)
Rejected: even bounded, it multiplies state storage and makes diagnostics
non-deterministic near the cap (which paths got merged depends on visit order). The cap
also creates a testing burden — behavior *at* the boundary is its own bug class. Edge
refinement recovers the majority of the same precision at strictly linear cost; the
remaining gap (cross-variable correlation) is not where our rule backlog's value lies.

### Roslyn's built-in nullable flow state
Insufficient: covers nullability only, tied to compiler diagnostics, and not extensible to
custom lattices (ownership, intervals, constants).

## Implementation Notes

- Refiners are stateless per method and constructed via
  `DataFlowRule<T>.CreateEdgeRefiner(SsaIndex)`; the dispatcher groups rules by
  transfer/refiner *type* and solves once per group.
- The `whenTrue` polarity convention:
  `whenTrue = isConditionalSuccessor == (ConditionKind == WhenTrue)` — every refiner uses
  the same formula; get it wrong and true/false branches swap silently, so new refiners
  should copy the existing pattern (and its tests) verbatim.
- ovs-2qi.11 ("bounded path exploration, limit 64 splits") is superseded by this decision:
  there is no splitting to bound. The solver's `maxIterations` plus the stress test stand
  in for the budget-and-fallback machinery that ADR would have required.
