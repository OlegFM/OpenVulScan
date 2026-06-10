# SSA Precision Hardening — S-1/S-2/S-3 (Design Spec)

**Bead:** `ovs-tr6` (precision subset: S-1, S-2, S-3)
**Date:** 2026-06-10
**Status:** Approved

## 1. Problem

The final review of the SSA numbering work (`ovs-2qi.9`) identified three precision gaps
that directly limit the quality of upcoming NRE rules (`ovs-2qi.15`, six rules on
`NullStateLattice`). They are fixed now so the rule family is built on a sound foundation.

- **S-1 — φ results invisible to rule callbacks.** `DataFlowRuleDispatcher.Run` seeds the
  per-operation rule state from `solver.InStates[block]`, which does **not** contain
  `phi.Result` bindings — those are materialised only inside
  `ITransfer.Apply(state, block)`, which the dispatcher never calls. Rules therefore see a
  pre-φ state: V3022 misses `int x = 5; if (c) x = 5; if (x == 5) { }` (always-true across
  a join), and every future NullState rule would equally miss merged null-states.
- **S-2 — flow-capture values fall through to ⊤/Unknown.** Roslyn lowers `a ?? b`, `a?.b`,
  ternaries, etc. into `IFlowCaptureOperation` / `IFlowCaptureReferenceOperation`.
  `SsaBuilder` registers the capture def, but neither SSA transfer evaluates
  `capture.Value`, so the capture's lattice entry is never set; symmetrically, `Evaluate`
  has no case for `IFlowCaptureReferenceOperation`, so even a set entry would never be read.
  Null-state information dies at every lowered construct.
- **S-3 — pre-order traversal kills a just-written field def.** Pass 2 of `SsaBuilder`
  walks operations in pre-order (parent before children). For
  `this.field = this.GetX()`, the assignment def is registered first, then the child
  invocation triggers `IsThisAccessingInvocation` and kills **all** tracked fields —
  including the one just written. Downstream reads see the post-kill version and lose the
  assigned value. The same root cause produces two sibling bugs: a field read inside a
  method-call argument records the post-kill version (listed as a production-correctness
  item in `ovs-tr6`), and a self-referencing RHS (`x = x + 1`) reads the **new** version
  instead of the old one.

## 2. Scope

In scope: S-1, S-2, S-3 exactly as designed below, plus regression tests.
Out of scope (remain in `ovs-tr6`): out-var / `??=` handling, method-symbol lookup via
`cfg.OriginalOperation`, evaluator/dispatch-table refactors, `ConstantSsaEdgeRefiner`,
S-4 debug invariant, engine exception isolation (`ovs-o32`).

## 3. Design

### 3.1 S-1 — `ApplyPhis` in the transfer contract

**`src/OpenVulScan.Core/Lattice/ITransfer.cs`** — add a default interface method:

```csharp
/// <summary>
/// Materialises the φ-joins of <paramref name="block"/> into the incoming state.
/// Called on block entry, before any operation of the block is applied.
/// Non-SSA transfers need no φ handling; the default is the identity.
/// </summary>
T ApplyPhis(T state, BasicBlock block) => state;
```

A default implementation keeps the legacy flat `NullStateTransfer` (and any other existing
implementor) source-compatible: they inherit the identity no-op.

**`NullStateSsaTransfer` / `ConstantSsaTransfer`** — extract the existing φ-loop from
`Apply(state, block)` into an override of `ApplyPhis(state, block)`;
`Apply(state, block)` becomes `state = ApplyPhis(state, block);` followed by the
per-operation loop. Solver behaviour is unchanged (same computation, same order).

**`src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs`** — in `Run`, after
`var state = result.InStates[block];` insert `state = transfer.ApplyPhis(state, block);`
before the per-operation loop. Rules now observe post-φ state, exactly what
`Apply(state, block)` produces inside the solver.

### 3.2 S-2 — evaluate captures on both the def and the use side

In **both** `NullStateSsaTransfer` and `ConstantSsaTransfer`:

- `Apply(state, operation)` switch gains a case **before** the fallback:
  `IFlowCaptureOperation capture => Evaluate(capture.Value, state)` — the capture def gets
  the lattice value of the captured expression.
- `Evaluate(expr, state)` switch gains:
  `IFlowCaptureReferenceOperation cref => Lookup(cref, new TrackedKey.Capture(cref.Id), state)`
  — reads of the capture flow the stored value back out. (This use-side case goes beyond
  the literal `ovs-tr6` text but is required for the def-side fix to be observable; without
  it the stored value is never consulted.)

`SsaBuilder` already registers capture defs (`AllocateExplicit`, version 0) and capture
uses (`IFlowCaptureReferenceOperation` case), so no builder change is needed for S-2.

### 3.3 S-3 — post-order defs in `SsaBuilder` Pass 2

Replace the flat pre-order loop
(`foreach (var op in EnumerateBlockOps(block)) ProcessOperation(op, …)`) with a recursive
walk rooted at each top-level operation of the block (`block.Operations` +
`block.BranchValue`):

```
Walk(op):
  if op is a tracked def (declarator / simple assignment / compound assignment /
                          increment-decrement with tracked target):
      foreach child: Walk(child)        // RHS uses old versions; RHS kills happen first
      RegisterDef(op)                   // def AFTER children — assignment semantics
  else if op is IFlowCaptureOperation:
      foreach child: Walk(child)        // captured expression first
      register capture def (version 0)
  else if IsThisAccessingInvocation(op):
      foreach child: Walk(child)        // argument reads use pre-kill versions
      kill all tracked instance fields  // kill AFTER children
  else:
      record use if applicable (same cases and parent-guards as today)
      foreach child: Walk(child)
```

Effects:

1. `this.field = this.GetX()` — RHS kill happens first (fields → vK), then the def
   registers vK+1 with the assigned value. Downstream reads see vK+1. **Fixes S-3.**
2. `M(this.field); this.Mutate();`-style argument reads — a field read inside the argument
   list of a this-accessing invocation now binds the **pre-kill** version. **Fixes the
   `ovs-tr6` production item "field read inside method-call argument records post-kill
   version".**
3. `x = x + 1` — the RHS read of `x` binds the old version; the def creates the new one.
   **Fixes the self-reference read.** (Note: the existing increment test
   `IncrementOperation_AdvancesVersionAndDoesNotCreatePhantomUse` covers `x++`, which has
   no RHS read; this is the general-assignment analogue.)

Unchanged:

- Pass 1 (def-site counting) keeps the flat enumeration — it only counts, order is
  irrelevant.
- The parent-guards in the use cases (skip assignment/increment targets) remain; they are
  structural checks independent of traversal order.
- Phi placement, entry versions, Pass 3 binding — untouched.

Existing SSA tests that encode pre-order behaviour (if any assert the buggy ordering) are
updated, with the new expectation justified in the test name/comment.

## 4. Affected files

| File | Change |
|---|---|
| `src/OpenVulScan.Core/Lattice/ITransfer.cs` | + default `ApplyPhis` |
| `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs` | φ-loop → `ApplyPhis`; capture cases in `Apply`/`Evaluate` |
| `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs` | same as above |
| `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs` | call `ApplyPhis` before per-op loop |
| `src/OpenVulScan.Core/Ssa/SsaBuilder.cs` | Pass 2 recursive post-order walk |
| `tests/OpenVulScan.Core.Tests/Ssa/*` | new + updated tests |
| `tests/OpenVulScan.Rules.Tests/*` | V3022 cross-branch regression snapshot |

## 5. Testing

Core tests (xUnit, `CfgTestHarness`):

- **S-3 builder:** (a) `this.f = GetX()`-shape — def version is the latest, post-kill;
  downstream read binds it. (b) field read in a this-accessing invocation's argument binds
  the pre-kill version. (c) `x = x + 1` — RHS use binds the old version, def is new.
- **S-2 transfers:** capture def takes its `Value`'s lattice value (NullState + Constant);
  capture reference read returns the stored value (via a lowered `??`/ternary snippet).
- **S-1 transfers:** `ApplyPhis` materialises φ-joins identically to the former inline
  loop; `Apply(state, block)` result is unchanged (equivalence check on a join snippet).

Rule-level regression (Verify snapshot):

- **V3022:** `int x = 5; if (c) x = 5; if (x == 5) { }` flags always-true after S-1.

Acceptance: full suite green (446 baseline + new tests; any intentionally-changed snapshot
re-verified with justification), clean Release build (warnings-as-errors).

## 6. Risks

- **S-1 default interface method** requires C# 8+ — fine (`LangVersion=preview`).
- **S-3 changes def/use binding order**, which existing snapshots may encode. Every changed
  snapshot must be re-justified, not blindly accepted: the new binding must match C#
  evaluation semantics (RHS before assignment).
- **Dispatcher double-φ risk:** `ApplyPhis` must be idempotent in practice (`SetItem` of
  joined values — re-running over an already-joined state recomputes the same values), so
  the solver's internal `Apply(state, block)` and the dispatcher's explicit call cannot
  disagree.
