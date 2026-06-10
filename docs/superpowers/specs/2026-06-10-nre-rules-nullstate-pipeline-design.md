# NRE Rule Family on a Shared NullState Pipeline — V3080 / V3105 / V3153 / V3168 (Design Spec)

**Bead:** `ovs-2qi.15` (scope narrowed: V3142 and V3151 split into separate beads — they use
different mechanisms: CFG reachability and constant/zero analysis respectively)
**Date:** 2026-06-10
**Status:** Approved

## 1. Problem

Phase-2 requires the core NRE detection family. Four rules share one analysis domain
(`NullState` over SSA versions) and one fault model — dereferencing a value that may be
null — differing only in *provenance* of the nullness:

| Code | CWE | Level | Fires on |
|---|---|---|---|
| V3080 | CWE-476 | Level1 | Possible null dereference (null literal / default on some path) |
| V3105 | CWE-690 | Level1 | Variable assigned via `?.` then dereferenced (`var x = a?.B; x.C;`) |
| V3153 | CWE-476 | Level1 | Direct dereference of a `?.` result (`(a?.B).C`) |
| V3168 | CWE-476 | Level1 | `await` of a potentially-null expression (`await a?.M();`) |

Two infrastructure gaps block the work:

1. **DataFlow rules never run in production.** `RuleScheduler.AnalyzeAsync` only handles
   `AstRule` / `SymbolRule`. `DataFlowRuleDispatcher<TLattice>` exists but is instantiated
   only in tests (V3022/V3063). The acceptance criterion "0 FP on synthetic.cs" requires
   the full scheduler path to exercise these rules.
2. **No SSA-aware edge refiner for `NullState`.** The existing `NullStateEdgeRefiner` keys
   on variable-name strings and is incompatible with `ImmutableDictionary<SsaId, NullState>`
   state. Without branch refinement, `if (x != null) x.M();` is a false positive in
   virtually every real file.

The SSA precision hardening (S-1/S-2/S-3, completed 2026-06-10) is the prerequisite that
makes this family expressible: flow captures from `?.` lowering are versioned defs with φ,
their null-state is evaluated by `NullStateSsaTransfer`, and φ-joins are visible to rules.

## 2. Scope

In scope:

- Scheduler wiring for DataFlow rules (production + snapshot-test harness).
- Shared-solve grouping in `DataFlowRuleDispatcher<TLattice>`.
- `NullStateSsaEdgeRefiner` in Core + `CreateEdgeRefiner(SsaIndex)` in the rule contract.
- `SsaIndex.DefSiteOf(SsaId)` reverse lookup (additive).
- Four rules + shared `NullDerefClassifier` in `OpenVulScan.Rules.DataFlow`.
- `synthetic.cs` FP corpus + zero-FP test; ≥30 snapshot cases total.

Out of scope (filed separately):

- V3142 unreachable code, V3151 division-by-zero — new beads split from `ovs-2qi.15`.
- Dispatcher coverage beyond `MethodDeclarationSyntax` (constructors, properties, local
  functions, lambdas) — follow-up bead.
- `foreach (var x in a?.Items)` as a V3153 case — stretch; if the foreach CFG lowering does
  not classify cleanly, file a follow-up rather than force it.
- Inter-procedural variants of V3105/V3168 (bead title says "intra").

## 3. Design

### 3.1 Scheduler wiring

**`OpenVulScan.RuleEngine`** — add a non-generic marker interface:

```csharp
public interface IDataFlowRule { }
```

`DataFlowRule<TLattice>` implements it. `RuleScheduler.AnalyzeAsync` collects instantiated
`IDataFlowRule`s, groups them by the closed generic state type (reflection over the
`DataFlowRule<TLattice>` base argument), constructs
`typeof(DataFlowRuleDispatcher<>).MakeGenericType(stateType)` per group, and appends the
dispatcher's `Run` results. Reflection happens once per analysis — the same trade-off
already accepted in `AstRule`'s handler discovery.

CLI pickup is automatic: `AnalysisRunner` already scans `OpenVulScan.Rules.*.dll`, and
`Rules.DataFlow.dll` already lands in the CLI output (V3022/V3063 live there).

**`SnapshotTestHarness`** additionally scans the `Rules.DataFlow` assembly so the quartet's
snapshot tests run the full scheduler → dispatcher path. Existing direct-dispatcher tests
for V3022/V3063 stay as-is (they test the dispatcher itself).

### 3.2 Shared solve in the dispatcher

Inside the per-method loop of `DataFlowRuleDispatcher<TLattice>.Run`: create each rule's
transfer and edge refiner, then group rules by
`(Lattice.GetType(), transfer.GetType(), refiner?.GetType())`. Solve the worklist **once
per group** and replay the block/operation walk invoking `OnState` of every rule in the
group. Type-equality grouping is sound because transfers and refiners are stateless apart
from the `SsaIndex` they were constructed with (same per method). V3022 + V3063 share
`ConstantSsaTransfer` and group automatically — same states as today, fewer solves.

### 3.3 `NullStateSsaEdgeRefiner` (new, Core)

SSA analogue of the flat `NullStateEdgeRefiner`, implementing
`IEdgeRefiner<ImmutableDictionary<SsaId, NullState>>`:

- Branch conditions recognised: `x == null`, `x != null`, `null == x`, `null != x`,
  `x is null`, `x is not null`; recursion through `!`, `&&` (true-branch), `||`
  (false-branch) — same condition algebra as the flat refiner.
- Operand resolution: `ILocalReferenceOperation` / `IParameterReferenceOperation` →
  `TrackedKey.Symbol`; `IFieldReferenceOperation` with instance receiver →
  `TrackedKey.InstanceField`; **`IFlowCaptureReferenceOperation` → `TrackedKey.Capture`**.
  The capture case is what suppresses FPs on `a?.b` itself: Roslyn lowers it to a
  `capture == null` branch, and the not-null edge must refine the capture's state.
- Key resolution: `SsaIndex.UseAt(conditionOperand, key)` gives the `SsaId` to refine;
  `state.SetItem(ssaId, refined)`. Refinement narrows only
  (`MaybeNull → NotNull/DefinitelyNull`, `Unknown → NotNull/DefinitelyNull`); it never
  widens a `NotNull`/`DefinitelyNull` fact (mirror `RefineForNullCheck` /
  `RefineForNotNullCheck` semantics from the flat transfer).

**Contract change in `DataFlowRule<TLattice>`** — mirror the `CreateTransfer` pattern:

```csharp
public virtual IEdgeRefiner<TLattice>? CreateEdgeRefiner(SsaIndex ssaIndex) => EdgeRefiner;
```

The dispatcher calls `CreateEdgeRefiner(ssaIndex)` instead of the `EdgeRefiner` property.
Default preserves existing behaviour for current rules.

### 3.4 `SsaIndex.DefSiteOf(SsaId)`

Additive reverse lookup built in the `SsaIndex` constructor by inverting `_definitions`
(each `SsaId` has at most one def operation; φ-results and entry versions have none):

```csharp
public IOperation? DefSiteOf(SsaId id);   // null for φ-results and entry versions
```

### 3.5 Shared deref detection + provenance classifier

Static helper `NullDerefClassifier` in `OpenVulScan.Rules.DataFlow`. A *deref site* is an
operation that throws NRE when its receiver/operand is null:

- `IMemberReferenceOperation` with `Instance` reference (field/property/event/method ref);
- `IInvocationOperation` with `Instance` reference;
- `IArrayElementReferenceOperation` (array reference operand);
- `IAwaitOperation` (operand — `await null` throws).

The classifier runs only when `operation` **is** the deref node (no subtree scanning — the
dispatcher replay already visits every descendant). The dereferenced value's state is read
from the solver state: resolve the receiver to a `TrackedKey`
(Symbol / InstanceField / Capture via `UseAt`), look up the `SsaId` entry. A rule fires
when the state is `DefinitelyNull` or `MaybeNull`. **`Unknown` and `NotNull` are silent.**
`Unknown` silence is the central noise control: unchecked parameters and results of unknown
calls have no state entry, so nothing fires without explicit evidence (a null literal on
some path, or `?.` provenance).

Provenance classification — exactly one code per deref site (cross-rule dedup by
construction):

1. Deref node is `IAwaitOperation` → **V3168**.
2. Receiver syntax (unwrapping parentheses) is `ConditionalAccessExpressionSyntax` →
   **V3153** (covers `(a?.B).C`, `(a?.B)[0]`, `(a?.M()).N()`).
3. Receiver is a local/parameter reference whose reaching SSA def
   (`UseAt` → `DefSiteOf`) is a variable declarator or simple assignment whose RHS
   (unwrapping conversions/parentheses) is a `ConditionalAccessExpressionSyntax` →
   **V3105**. If the reaching def is a φ-result or anything else, provenance is ambiguous →
   falls through to V3080.
4. Otherwise → **V3080**.

Each rule file (`V3080PossibleNullDereference.cs`, `V3105UseAfterNullConditional.cs`,
`V3153DereferenceOfConditionalAccessResult.cs`, `V3168AwaitPossiblyNull.cs`) is a thin
subscriber: a `DataFlowRule<ImmutableDictionary<SsaId, NullState>>` (shared abstract base
`NullStateRuleBase` seals `Lattice` = `MapLattice<SsaId, NullStateLattice, NullState>`,
`CreateTransfer` = `NullStateSsaTransfer`, `CreateEdgeRefiner` = `NullStateSsaEdgeRefiner`)
whose `OnState` calls the classifier and reports only its own code. The classifier is a
pure function over (operation, state, ssaIndex); running it in four subscribers per op is
negligible next to the (now shared) solver cost.

Diagnostic messages follow PVS wording:

- V3080: `Possible null dereference of '{0}'`
- V3105: `The '{0}' variable was used after it was assigned through null-conditional operator. NullReferenceException is possible`
- V3153: `Dereferencing the result of null-conditional access operator can lead to NullReferenceException`
- V3168: `Awaiting on expression with potential null value can lead to throwing of 'NullReferenceException'`

All four: `[Rule("Vxxxx", RuleSeverity.Level1, "CWE-xxx", RuleCategory.GeneralAnalysis,
AnalysisCapability.DataFlow)]` with CWE per the table in §1.

### 3.6 Known imprecision (accepted)

`if (a != null) { var x = a?.B; x.C; }` fires V3105: the `?.` null-arm is structurally
present in the CFG and the refiner does not prune infeasible paths. PVS-Studio behaves the
same way (using `?.` signals doubt; dereferencing its result unchecked is the pattern the
rule exists for). `synthetic.cs` must not contain this ambiguous pattern.

## 4. Affected files

| File | Change |
|---|---|
| `src/OpenVulScan.RuleEngine/IDataFlowRule.cs` | new marker interface |
| `src/OpenVulScan.RuleEngine/DataFlowRule.cs` | implement marker; + `CreateEdgeRefiner` |
| `src/OpenVulScan.RuleEngine/RuleScheduler.cs` | collect + dispatch DataFlow rules |
| `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs` | group rules, shared solve, use `CreateEdgeRefiner` |
| `src/OpenVulScan.Core/Lattice/NullStateSsaEdgeRefiner.cs` | new SSA-aware refiner |
| `src/OpenVulScan.Core/Ssa/SsaIndex.cs` | + `DefSiteOf` |
| `src/OpenVulScan.Rules.DataFlow/NullStateRuleBase.cs` | new shared base |
| `src/OpenVulScan.Rules.DataFlow/NullDerefClassifier.cs` | new shared detector/classifier |
| `src/OpenVulScan.Rules.DataFlow/V3080PossibleNullDereference.cs` | new rule |
| `src/OpenVulScan.Rules.DataFlow/V3105UseAfterNullConditional.cs` | new rule |
| `src/OpenVulScan.Rules.DataFlow/V3153DereferenceOfConditionalAccessResult.cs` | new rule |
| `src/OpenVulScan.Rules.DataFlow/V3168AwaitPossiblyNull.cs` | new rule |
| `tests/OpenVulScan.Rules.Tests/SnapshotTestHarness.cs` | scan Rules.DataFlow assembly |
| `tests/OpenVulScan.Rules.Tests/V3080…V3168 test files` | snapshot suites |
| `tests/OpenVulScan.Rules.Tests/TestData/Synthetic.cs` | FP corpus |
| `tests/OpenVulScan.Core.Tests/…` | refiner + DefSiteOf unit tests |

## 5. Testing

Core unit tests (`CfgTestHarness`):

- `NullStateSsaEdgeRefiner`: each condition form refines the right `SsaId`; capture-ref
  conditions refine `TrackedKey.Capture`; `&&`/`||`/`!` recursion; no widening of facts.
- `SsaIndex.DefSiteOf`: assignment def round-trips; φ-result returns null.
- Dispatcher grouping: two rules sharing a transfer type produce identical diagnostics to
  separate solves (equivalence test) and the states observed by both subscribers match.

Snapshot tests (Verify, ≥30 cases total, ~8 per rule), positives include:

- V3080: `string s = null; s.Length`, null on one branch then deref after join,
  field set to null then deref.
- V3105: `var x = a?.B; x.C;` (declarator and assignment forms), deref via invocation.
- V3153: `(a?.B).C`, `(a?.M()).N()`, element access on parenthesised `?.` result.
- V3168: `await a?.M()`, `var t = a?.M(); await t;` — note the second classifies as
  V3168 (await node wins over V3105 by rule 1 of the classifier).

Negatives (each rule): guarded deref (`if (x != null)`), `is not null` guard, `??`
fallback (`(a?.B ?? fallback).C` silent), reassignment to non-null before deref,
unchecked parameter deref (Unknown → silent), `?.`-chained access without parens
(`a?.B.C` — safe by language semantics, silent).

FP corpus: `TestData/Synthetic.cs` (~150 lines of realistic safe null-handling: guard
clauses, ternaries, `??`/`??=`, early returns, loops with reassignment, safe `?.` chains)
+ a test running the full scheduler asserting **zero** diagnostics from the four codes.

Acceptance: full suite green (456 baseline + new), Release build clean
(warnings-as-errors), ≥30 quartet snapshot cases, synthetic.cs zero-FP test passing.

## 6. Risks

- **Roslyn `?.` lowering shapes vary** (nested chains produce nested captures). The
  classifier is syntax-driven for provenance (robust), state-driven for firing — the S-2
  capture evaluation already handles the state side; tests must cover a 2-level chain.
- **`await` in test snippets** requires `Task` metadata — `System.Private.CoreLib` (already
  referenced via `typeof(object).Assembly`) defines `Task`; if the harness compilation
  reports missing types, add the `System.Runtime` facade reference in the harness (test
  plumbing, not production).
- **Grouping regression risk for V3022/V3063**: shared solve must not change their
  diagnostics — covered by the existing 232-test suite plus the equivalence test in §5.
- **Refiner soundness**: refining the wrong `SsaId` (e.g., stale version) would silently
  eat true positives. Mitigated by resolving versions exclusively through `UseAt` at the
  condition operand and by negative tests asserting positives still fire when the guard
  protects a *different* variable.

## 7. Implementation notes / deviations from the approved design

Recorded post-implementation (final review 2026-06-10). All four were individually
reviewed and verified sound; they extend rather than contradict the design above.

1. **Phase-1 AST V3105 retired** (commit `5ed53ec`). A pre-existing syntactic stopgap
   `V3105NreAfterConditionalAccess` (Rules.Ast) owned the V3105 code; the registry rejects
   duplicates, so the rule, its tests and 10 snapshots were deleted. Its `(a?.B).C` half is
   now correctly attributed to V3153; its per-`a?.B.C` heuristic was intentionally dropped
   (safe by language semantics, no null evidence).
2. **`NullStateSsaTransfer`: `IDefaultValueOperation` → `DefinitelyNull`** for reference
   types (commit `5ed53ec`). The `?.` null arm emits `IDefaultValueOperation`, not a null
   literal; without this arm the capture lost its null evidence. Value types and
   unconstrained generics stay `Unknown` (`IsReferenceType` guard). Direct unit coverage in
   `NullStateSsaTransferDefaultValueTests` (commits `f187361`, `eeb6854`).
3. **`SsaBuilder`: l-value capture aliasing** (commit `5ed53ec`). Roslyn lowers assignment
   *targets* into flow captures (`string x; x = a?.F;` → target is a capture-ref wrapping
   `LocalReference(x)`). `BuildCaptureToLocalMap` re-keys such defs to
   `TrackedKey.Symbol(x)` uniformly across def counting, versioning/φ and use binding.
   Direct unit coverage in `SsaBuilderCaptureTests` (commit `f187361`).
4. **`ConstantSsaEvaluator`: null-literal crash guard** (commits `d3a8fc6`, `f6f670f`).
   Latent `ArgumentNullException` (`Const(null)` throws) exposed when DataFlow rules first
   ran through the production scheduler; null literals now evaluate to `Top`, matching
   `ConstantSsaTransfer` in Core.

Files touched beyond the §4 table: `src/OpenVulScan.Rules.Ast/V3105NreAfterConditionalAccess.cs`
(deleted), `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`, `src/OpenVulScan.Rules.DataFlow/ConstantSsaEvaluator.cs`.

## 8. Tracker housekeeping

- `ovs-2qi.15`: note scope narrowing (quartet only); close when this spec ships.
- New beads: V3142 (unreachable code, CFG reachability mechanism) and V3151
  (division-by-zero before zero-check, constant/zero domain) — split from `ovs-2qi.15`,
  same parent epic `ovs-2qi`.
- Follow-up bead: dispatcher coverage beyond `MethodDeclarationSyntax`.
- Stretch follow-up (only if dropped here): `foreach` over `?.` result as V3153.
