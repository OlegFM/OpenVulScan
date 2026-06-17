# Design: Dispose rule family — V3114, V3073, V3178

- **Date:** 2026-06-15
- **Beads issue:** ovs-2qi.17 (Phase 2 — intra-procedural DFA + path-sensitive)
- **Status:** approved-pending-review
- **Consumes:** `DisposeLattice` (ovs-2qi.6), `WorklistSolver`, `MapLattice`, `SsaBuilder`

## 1. Context

`ovs-2qi.17` was filed as "V3074 + V3097 + V3122 (Dispose: missing / double / wrong-branch)".
While exploring, `RulesMap.xml` (the project's authoritative PVS-Studio catalogue) revealed those
three codes mean something entirely unrelated:

| Code  | Real PVS meaning                                                                 |
|-------|----------------------------------------------------------------------------------|
| V3074 | Class contains a `Dispose` method — consider implementing `IDisposable`.          |
| V3097 | `[Serializable]` type has non-serializable members not marked `[NonSerialized]`.  |
| V3122 | Upper/lower-case string compared with a different-case string.                    |

Using those numbers for dispose semantics would violate the project rule "rule codes mirror
PVS-Studio numbering" and risk future collisions. The dispose-related PVS codes are:

| Code  | Real PVS meaning                                                       | CWE     | Level |
|-------|-----------------------------------------------------------------------|---------|-------|
| V3114 | IDisposable object is not disposed before method returns.             | CWE-404 | 2     |
| V3073 | Not all IDisposable members are properly disposed in `Dispose`.       | —       | 0     |
| V3178 | Calling method / accessing property of a potentially disposed object. | CWE-672 | 1     |

**Decision (user-approved):** ship under the correct codes **V3114 + V3073 + V3178** (keeps the
three-rule "7+7+7" acceptance criterion intact). Update the beads issue accordingly.

## 2. Decisions log (all user-approved)

1. **Codes:** V3114 (leak) + V3073 (members) + V3178 (use-after-dispose). Re-mapped from the
   issue's incorrect V3074/V3097/V3122.
2. **Leak FP stance:** *moderate* — detect partial dispose (disposed on some but not all paths),
   not only definite leaks.
3. **V3178 stance:** *MAY* — flag when the object is possibly disposed on at least one path
   (PVS's "potentially disposed" wording). Consumes `DisposeLattice`.
4. **Engine:** `AstRule` building its own CFG+SSA and running `WorklistSolver` (precedent:
   V3151, V3142), not `DataFlowRule` via the dispatcher.

## 3. Key finding — moderate leak needs `Open = ⊤`

`DisposeLattice` is `Live(⊥) ⊏ Disposed ⊏ DoubleDisposed(⊤)`, `Join = max`. Its leak-dangerous
state `Live` is `⊥`, so `Join(Live, Disposed) = Disposed`: merging a disposing path with a
non-disposing path yields `Disposed`, hiding partial dispose. That only supports *conservative*
leak detection (Live on **all** paths).

The user chose *moderate*, so the dangerous "open" state must be `⊤` (absorbing) to survive joins.
Hence V3114/V3073 need a dedicated lattice; `DisposeLattice` is structurally unsuitable for them
and instead serves V3178.

## 4. Lattices

### 4.1 `ResourceOwnershipLattice` (new) — for V3114, V3073

A three-element chain over a new `OwnershipState` enum:

```
Untracked (⊥)  ⊏  Disposed  ⊏  Open (⊤)
Join = chain max          (MAY analysis of the predicate "created and not yet disposed")
Bottom = Untracked        (identity; also models "resource not created on this path")
Top    = Open
```

- `Join(Untracked, x) = x` — a path where the resource does not exist contributes nothing.
- `Join(Open, Disposed) = Open` — leaked on at least one path ⇒ flagged (partial dispose).
- `Join(Disposed, Disposed) = Disposed` — disposed on all paths ⇒ clean.

Same structural shape as `DisposeLattice` (chain, max-join, `⊥` identity), so it reuses the
`BoolFlatLattice` pattern and the same lattice-axiom test battery.

`MapLattice<ISymbol, ResourceOwnershipLattice, OwnershipState>` lifts it per resource. Absent key
= `Untracked` is exactly correct for "not created on this path" (handles a resource declared
inside a branch with no false positive).

### 4.2 `DisposeLattice` (existing) — for V3178

`Live(⊥) ⊏ Disposed ⊏ DoubleDisposed(⊤)`, MAY. Transfer raises a symbol `Live → Disposed` on a
dispose and `Disposed → DoubleDisposed` on a second dispose. V3178 reports when an operation
uses/re-disposes a symbol whose state is `⊒ Disposed` on some path.

## 5. Engine & module layout

All three rules are `AstRule`s that build their own CFG+SSA and run `WorklistSolver`, inspecting
block in-states directly.

Rejected — `DataFlowRule` via `DataFlowRuleDispatcher`:
- the dispatcher calls `solver.Solve(cfg)` with **no entry seed**, but V3073 must seed fields as
  `Open` at `Dispose()` entry;
- the dispatcher only visits `MethodDeclarationSyntax` (bug ovs-p3o), losing constructors and
  accessors where dispose matters;
- `OnState` has no clean "method exit" hook for the leak check.

Shared helper **`DisposeFlow`** lives in `OpenVulScan.Rules.DataFlow` (alongside
`NullDerefClassifier`) and does the heavy lifting; the three rule classes are thin consumers:

- `DisposeFlow.CollectOwnedResources(...)` — IDisposable locals created via `new`/factory, minus
  escaping ones.
- `DisposeFlow.IsDispose(IOperation)` / `IsResourceUse(IOperation, symbol)` — operation classifiers.
- `DisposeFlow.Solve*(...)` — runs the appropriate lattice solve and exposes per-point state.

Each rule = one file named after its code (`V3114NotDisposedBeforeReturn.cs`, etc.).

## 6. Per-rule semantics

### V3114 — IDisposable not disposed before return (CWE-404, Level 2)
- **Resources:** locals whose type implements `IDisposable`/`IAsyncDisposable`, created via `new`
  or a factory in the method body.
- **Escape pre-filter (KISS, anti-FP):** drop a symbol from tracking if it ever escapes — returned,
  assigned to a field/property, passed as an argument, captured by a lambda, or added to a
  collection. This removes the conditional-ownership false positives.
- **Transfer:** creation ⇒ `Open`; `Dispose()`/`using`-exit ⇒ `Disposed`.
- **Report:** at the Exit block in-state, any tracked resource `== Open` ⇒ V3114 (covers full
  leak and partial dispose).

### V3073 — not all IDisposable members disposed (Level 0)
- **Scope:** the `Dispose()` / `Dispose(bool)` method(s) of a class implementing `IDisposable`.
- **Resources:** instance fields whose type implements `IDisposable`, seeded `Open` at method entry
  (via `Solve(cfg, seed)`).
- **Report:** field `== Open` at exit ⇒ V3073.
- **v1 limitations (documented):** the virtual `Dispose(bool disposing)` + `base.Dispose()` pattern
  is handled shallowly; a field counts as disposed if `field.Dispose()` appears in the body.

### V3178 — use/re-dispose of disposed object (CWE-672, Level 1), MAY
- **Lattice:** `DisposeLattice`.
- **Report:** an operation that invokes a member of, accesses a property of, or calls `Dispose()`
  again on a symbol whose state is `⊒ Disposed` on some reaching path.
- **Accepted trade-off:** MAY semantics can false-positive on branchy/looping flow
  (the V3151 lesson); accepted per the PVS-faithful choice.

## 7. using / try-finally / async

`using`, `using`-declarations, `try/finally`, and `await using` are lowered by Roslyn's
`ControlFlowGraph` into finally regions containing explicit `Dispose` invocations, which the
transfer treats as ordinary disposes. Because `finally` executes on every path, dispose-in-finally
correctly yields `Disposed` on all paths.

**This lowering assumption was validated empirically by a CFG probe (2026-06-17).** The CFG does
lower `using`/`using`-declarations into a `finally` region with an explicit `Dispose` invocation —
but with two refinements the probe revealed:

1. The lowered invocation's `Instance` is an `IConversionOperation` (`((IDisposable)s).Dispose()`),
   not a bare `ILocalReferenceOperation` — dispose classification **must unwrap conversions**.
2. The lowered `finally` carries a null-guard (`if (s != null) ((IDisposable)s).Dispose()`). On the
   `s == null` skip path the resource stays `Open`, so `Join(Disposed, Open) = Open` at Exit would
   **false-positive V3114 on a correct `using`**.

**Resolution (KISS, matches PVS V3114):** for the leak rules, **exclude `using`-variables from
tracking entirely** (a `using` disposes by construction) and count only **explicit `Dispose()`
calls written by the developer**.

**Correction (Task 4 TDD, 2026-06-17) — the original "dispose-in-finally yields Disposed on all
paths" claim above is WRONG for the `WorklistSolver`.** The solver traverses only *normal*
`block.Predecessors`; Roslyn connects a `finally` region by *exception* edges
(`FallThroughSuccessor.Semantics == StructuredExceptionHandling`), so the `finally` block is not a
normal predecessor of Exit and the solver never applies its `Dispose()`. Likewise `return r;` is
lowered as `block.BranchValue` (an `ILocalReferenceOperation`, after `Unwrap`) with
`FallThroughSuccessor.Semantics == Return`, with **no** `IReturnOperation` wrapper, so a
parent-based escape check misses it. `DisposeFlow.CollectOwnedLocals` therefore adds two
pre-filters: a branch-value-return escape, and a finally-dispose pre-filter that drops any resource
disposed in a `StructuredExceptionHandling` block (finally always runs). **Known v1 FN:**
`finally { if (c) r.Dispose(); }` is over-suppressed. A structural block-only CFG probe would not
have surfaced this — it required running the solver. (Persisted to beads memory.)

## 8. Testing

- Snapshot tests via `SnapshotTestHarness.RunRuleSnapshotAsync`, 7 + 7 + 7 cases.
- Unit tests for `ResourceOwnershipLattice`: 4 axioms (assoc / comm / idem / absorption) +
  le-equiv-join-identity, mirroring `DisposeLatticeTests`.
- Case coverage: direct leak; dispose-in-finally/using (no flag); partial dispose (flag); escape
  via return/field/argument (no flag); resource declared inside a branch; async/await; straight-line
  double dispose (V3178 flag); branchy possibly-disposed (V3178 MAY flag).

## 9. Follow-ups (file as beads issues)

- ovs-p3o interplay: when the dispatcher is extended beyond `MethodDeclarationSyntax`, revisit
  whether these rules can migrate to `DataFlowRule`.
- Flow-sensitive escape (treat escape as dispose-equivalent per path) to replace the whole-method
  escape pre-filter, recovering precision on conditional-ownership methods.
- V3073 deep `Dispose(bool)` / `base.Dispose()` pattern.
