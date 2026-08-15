# Inter-procedural Nullability for V3080 Implementation Plan (ovs-xwx.12)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Null-info crosses method boundaries: a call site's result carries the callee's summarized return nullability, so the existing NRE family (V3080/V3105/V3153/V3168) fires on `var s = F(); s.Length;` when `F` returns null.

**Architecture:** Summary-based, context-insensitive (k=0) — NOT full IFDS (ovs-xwx.4 stays open). A compilation-level pre-pass builds CHA→RTA call graph, walks SCCs bottom-up (`BottomUpSummaryScheduler`), and for each source method solves the existing NullState SSA pipeline to extract the join of return-site null-states. The per-method transfer consults the resulting lookup at `IInvocationOperation` sites; unknown/metadata callees stay `Unknown` (classifier is silent on Unknown → FP-safe). Wiring: a per-Run `AnalysisSession` in the dispatcher carries lazily-computed compilation artifacts; a new `CreateTransfer(SsaIndex, AnalysisSession)` virtual (default delegates to the legacy overload) lets `NullStateRuleBase` opt in.

**Tech Stack:** existing Core pieces only — `ChaBuilder`, `RtaRefiner`, `BottomUpSummaryScheduler`, `WorklistSolver`, `NullStateSsaTransfer/EdgeRefiner`, `SsaBuilder`.

**Spec (inline, from bead ovs-xwx.12):** propagate null-info from call sites through summaries; acceptance = 10+ inter-procedural snapshot cases, FP-rate < 30% on synthetic examples.

## Global Constraints

- `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`; flat namespace `OpenVulScan`.
- Dispatcher statelessness contract: transfers in one group must be behaviourally identical — the summary lookup is a per-session singleton, so capturing it preserves the contract (amend the contract comment).
- Return-site extraction: in a lowered CFG `return e;` is a block whose `FallThroughSuccessor.Semantics == ControlFlowBranchSemantics.Return` with `BranchValue = e`.

### Task 1: summary-aware transfer

**Files:**
- Create: `src/OpenVulScan.Core/Summaries/INullabilitySummaryLookup.cs`
- Modify: `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs` (optional ctor param; `IInvocationOperation` case in `Evaluate`; expose `internal NullState EvaluateValue(IOperation, state)` for Task 2)
- Test: `tests/OpenVulScan.Core.Tests/Ssa/NullStateSummaryTransferTests.cs`

**Interfaces:**
- Produces: `INullabilitySummaryLookup { NullState ReturnStateOf(IMethodSymbol method); }`; `NullStateSsaTransfer(SsaIndex ssa, INullabilitySummaryLookup? summaries = null)`.

- [ ] **Step 1:** Failing tests: stub lookup returning DefinitelyNull → `var s = F();` def state DefinitelyNull; NotNull → NotNull; no lookup → Unknown.
- [ ] **Step 2:** RED → implement → GREEN.

### Task 2: compilation-level provider

**Files:**
- Create: `src/OpenVulScan.Core/Summaries/NullabilitySummaryProvider.cs`
- Test: `tests/OpenVulScan.Core.Tests/Summaries/NullabilitySummaryProviderTests.cs`

**Interfaces:**
- Produces: `static INullabilitySummaryLookup NullabilitySummaryProvider.Compute(Compilation compilation, CancellationToken ct)`.
- Consumes: `ChaBuilder.Build`, `RtaRefiner.Refine`, `BottomUpSummaryScheduler.Run`, `NullStateSsaTransfer.EvaluateValue`.

Per-method extraction: skip void/abstract/no-body (Unknown); non-Nullable value-type returns → NotNull; else CFG+SSA solve with a lookup adapter over the scheduler's current summaries (recursion converges via SCC fixed point), march block states, join `BranchValue` states at Return-semantics blocks with `NullStateLattice.Join`. MethodSummary.MethodId = `GetDocumentationCommentId() ?? ToDisplayString()`; other fields default.

- [ ] **Step 1:** Failing tests: `string F() => null;` → DefinitelyNull; branch null/new → MaybeNull; `=> new object()` → NotNull; chain g→f null → DefinitelyNull; self-recursive `F(n) => n>0 ? F(n-1) : null` stabilizes to DefinitelyNull; `int F()` → NotNull.
- [ ] **Step 2:** RED → implement → GREEN.

### Task 3: session wiring + V3080 snapshots

**Files:**
- Create: `src/OpenVulScan.RuleEngine/AnalysisSession.cs`
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRule.cs` (add `virtual CreateTransfer(SsaIndex, AnalysisSession)` defaulting to legacy), `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs` (build session once per Run, call new overload), `src/OpenVulScan.Rules.DataFlow/NullStateRuleBase.cs` (override with summary lookup)
- Test: `tests/OpenVulScan.Rules.Tests/V3080InterProcTests.cs` (snapshot, 10+ cases)

**Interfaces:**
- Produces: `AnalysisSession(Compilation, CancellationToken)` with `INullabilitySummaryLookup GetNullabilitySummaries()` (memoized, thread-safe).

Snapshot cases: (1) direct call returns null → deref flagged; (2) maybe-null branch → flagged; (3) NotNull factory → silent; (4) null-check guard after call → silent; (5) two-method chain → flagged; (6) recursion → flagged; (7) metadata callee → silent; (8) expression-bodied callee → flagged; (9) callee returning parameter (Unknown) → silent; (10) `?.` on maybe-null call result → silent (guarded); (11) call result passed through local reassignment → flagged; (12) value-type return deref (e.g. `.ToString()`) → silent.

- [ ] **Step 1:** Snapshot tests RED (all silent today except intra cases) → wire session → GREEN with verified snapshots.
- [ ] **Step 2:** Full suite; commit; close ovs-xwx.12; note on ovs-xwx.4 that rule needs are covered by summaries, IFDS remains for path-sensitivity.
