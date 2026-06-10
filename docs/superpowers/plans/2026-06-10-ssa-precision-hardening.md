# SSA Precision Hardening (S-1/S-2/S-3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three SSA precision gaps from `ovs-tr6` (S-1 φ-visibility in rule callbacks, S-2 flow-capture value evaluation, S-3 post-order defs in SsaBuilder) so the upcoming NRE rule family is built on a sound foundation.

**Architecture:** S-3 rewrites `SsaBuilder` Pass 2 from a flat pre-order enumeration into a recursive post-order walk (children before defs/kills). S-2 adds `IFlowCaptureOperation`/`IFlowCaptureReferenceOperation` cases to both SSA transfers. S-1 adds a default `ApplyPhis` method to `ITransfer<T>`, extracts the φ-loops into it, and calls it from `DataFlowRuleDispatcher` before the per-operation loop.

**Tech Stack:** C# / .NET 10, Roslyn `ControlFlowGraph`/`IOperation`, xUnit (Core.Tests), direct-dispatcher tests (Rules.Tests).

**Spec:** `docs/superpowers/specs/2026-06-10-ssa-precision-hardening-design.md`

---

## Background the engineer needs

- **Build:** `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, `LangVersion=preview`, single namespace `OpenVulScan`.
- **Naming in tests:** `tests/OpenVulScan.Core.Tests` allows underscores in test method names (existing convention, e.g. `Assignment_OfNullLiteral_TracksDefinitelyNull`). `tests/OpenVulScan.Rules.Tests` does NOT (CA1707 is an error there) — use PascalCase without underscores.
- **Usings:** Core.Tests has no global usings — every test file lists its usings explicitly. Rules.Tests has implicit/global usings for `Xunit`, `System.Threading`, etc. (see `V3022Tests.cs`).
- **Test harnesses:** Core SSA tests use `CfgTestHarness.Compile(snippet)` → `(ControlFlowGraph Cfg, SemanticModel Model, IMethodBodyOperation Body)`; the snippet's FIRST method declaration is used, so put `M()` before helper methods. V3022 tests construct `DataFlowRuleDispatcher` directly (no Verify snapshots).
- **Key APIs:**
  - `SsaIndex.DefinitionAt(IOperation) : SsaId?`, `UseAt(IOperation, TrackedKey) : SsaId?`, `PhisAt(BasicBlock) : IReadOnlyList<Phi>`, `AllVersions(TrackedKey) : IReadOnlyList<SsaId>`.
  - `SsaId(TrackedKey Key, int Version)` readonly record struct. `Phi(SsaId Result, ImmutableArray<PhiOperand> Operands)`; `PhiOperand(BasicBlock PredecessorBlock, SsaId Version)`.
  - `WorklistSolver<T>(ILattice<T>, ITransfer<T>, IEdgeRefiner<T>? = null, …)`; `Solve(cfg, ct)` returns a result with `InStates[block]` / `OutStates[block]`.
  - `MapLattice<TKey, TLat, TVal>` — e.g. `MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>`, `MapLattice<SsaId, NullStateLattice, NullState>`.
- **Why task order S-3 → S-2 → S-1:** S-3 changes the builder whose output the transfers consume; landing it first surfaces any existing-test fallout before the transfer/dispatcher work stacks on top.

## File Structure

- Modify: `src/OpenVulScan.Core/Ssa/SsaBuilder.cs` — Pass 2 recursive post-order walk (Task 1).
- Modify: `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs` — capture cases (Task 2), `ApplyPhis` extraction (Task 3).
- Modify: `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs` — same (Tasks 2, 3).
- Modify: `src/OpenVulScan.Core/Lattice/ITransfer.cs` — default `ApplyPhis` (Task 3).
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs` — call `ApplyPhis` (Task 3).
- Create: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderPostOrderTests.cs` (Task 1).
- Create: `tests/OpenVulScan.Core.Tests/Ssa/SsaTransferCaptureTests.cs` (Task 2).
- Create: `tests/OpenVulScan.Core.Tests/Ssa/SsaTransferApplyPhisTests.cs` (Task 3).
- Modify: `tests/OpenVulScan.Rules.Tests/V3022Tests.cs` — cross-branch regression (Task 3).

---

## Task 1: S-3 — post-order defs/kills in SsaBuilder Pass 2

**Files:**
- Modify: `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`
- Create: `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderPostOrderTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderPostOrderTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderPostOrderTests
{
    private static IEnumerable<IOperation> AllOps(ControlFlowGraph cfg)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in block.Operations)
                foreach (var d in Descend(op))
                    yield return d;
            if (block.BranchValue is not null)
                foreach (var d in Descend(block.BranchValue))
                    yield return d;
        }

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    [Fact]
    public void FieldAssignFromThisCall_DefSurvivesRhsKill()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        this.f = GetX();
        var y = this.f;
    }
    int GetX() => 0;
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var key = new TrackedKey.InstanceField(field);

        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .Single(a => a.Target is IFieldReferenceOperation);
        var read = AllOps(cfg).OfType<IFieldReferenceOperation>()
            .Single(f => f.Parent is not ISimpleAssignmentOperation parent
                         || !ReferenceEquals(parent.Target, f));

        // The RHS call kills fields BEFORE the def registers (post-order),
        // so the downstream read must bind exactly the assignment's version.
        Assert.Equal(index.DefinitionAt(assign), index.UseAt(read, key));
    }

    [Fact]
    public void ArgumentFieldRead_BindsPreKillVersion()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        this.f = 1;
        Use(this.f);
    }
    void Use(int v) { }
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var key = new TrackedKey.InstanceField(field);

        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .Single(a => a.Target is IFieldReferenceOperation);
        var argRead = AllOps(cfg).OfType<IFieldReferenceOperation>()
            .Single(f => f.Parent is IArgumentOperation);

        // The argument is evaluated before the call executes: the read binds
        // the pre-kill version (the assignment's def), and the kill still
        // produces a later version afterwards.
        Assert.Equal(index.DefinitionAt(assign), index.UseAt(argRead, key));
        Assert.Equal(2, index.AllVersions(key).Count);
    }

    [Fact]
    public void SelfReferencingAssignment_RhsReadsOldVersion()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x = x + 1;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var read = AllOps(cfg).OfType<ILocalReferenceOperation>()
            .Single(l => l.Parent is IBinaryOperation);
        var key = new TrackedKey.Symbol(read.Local);

        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .Single(a => a.Target is ILocalReferenceOperation);

        // RHS reads the OLD version (0); the assignment defines version 1.
        Assert.Equal(0, index.UseAt(read, key)!.Value.Version);
        Assert.Equal(1, index.DefinitionAt(assign)!.Value.Version);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderPostOrderTests"`
Expected: 3 FAIL — the current pre-order walk binds the post-kill / post-def versions.
(If `FieldAssignFromThisCall` fails for a different reason — e.g. `Single()` throws — inspect the op shapes before proceeding; the assertions, not the plumbing, must be what fails.)

- [ ] **Step 3: Rewrite Pass 2 as a recursive post-order walk**

In `src/OpenVulScan.Core/Ssa/SsaBuilder.cs`:

(a) Replace the Pass 2 inner loop (currently `foreach (var op in EnumerateBlockOps(block)) { ProcessOperation(op, current, NewVersion, AllocateExplicit, definitions, uses); }`) with:

```csharp
            foreach (var op in TopLevelBlockOps(block))
            {
                Walk(op, current, NewVersion, AllocateExplicit, definitions, uses);
            }
```

(b) Delete the `ProcessOperation` method entirely. Add in its place:

```csharp
    private static IEnumerable<IOperation> TopLevelBlockOps(BasicBlock block)
    {
        foreach (var op in block.Operations)
            yield return op;
        if (block.BranchValue is not null)
            yield return block.BranchValue;
    }

    private static void Walk(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        Func<TrackedKey, SsaId> newVersion,
        Func<TrackedKey, int, SsaId> allocateExplicit,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses)
    {
        // Tracked defs: walk children first (RHS reads bind pre-def versions and
        // RHS kills happen before the def), then register the def. This matches
        // C# evaluation order: the right-hand side completes before the write.
        var defKey = TryGetDefinitionKey(op);
        if (defKey is not null)
        {
            WalkChildren(op, current, newVersion, allocateExplicit, definitions, uses);
            RegisterDef(op, defKey, current, newVersion, definitions);
            return;
        }

        // Flow captures: the captured expression is evaluated first, then the
        // capture binds. Always version 0 (single-assignment by Roslyn's guarantee).
        if (op is IFlowCaptureOperation flow)
        {
            WalkChildren(op, current, newVersion, allocateExplicit, definitions, uses);
            var captureKey = new TrackedKey.Capture(flow.Id);
            var captureId = allocateExplicit(captureKey, 0);
            current[captureKey] = captureId;
            definitions[op] = captureId;
            return;
        }

        // this-accessing invocations: argument reads bind pre-kill versions,
        // then all tracked instance fields are killed (callee may mutate them).
        if (IsThisAccessingInvocation(op))
        {
            WalkChildren(op, current, newVersion, allocateExplicit, definitions, uses);
            var fieldKeysSnapshot = current.Keys.OfType<TrackedKey.InstanceField>().ToList();
            foreach (var key in fieldKeysSnapshot)
            {
                var id = newVersion(key);
                current[key] = id;
            }
            return;
        }

        RecordUse(op, current, uses);
        WalkChildren(op, current, newVersion, allocateExplicit, definitions, uses);
    }

    private static void WalkChildren(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        Func<TrackedKey, SsaId> newVersion,
        Func<TrackedKey, int, SsaId> allocateExplicit,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses)
    {
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            Walk(child, current, newVersion, allocateExplicit, definitions, uses);
        }
    }

    private static void RecordUse(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses)
    {
        switch (op)
        {
            case ILocalReferenceOperation lref:
            {
                // Skip: this lref is the assignment target, not a read.
                if (lref.Parent is ISimpleAssignmentOperation { Target: var t1 } && ReferenceEquals(t1, lref)) break;
                if (lref.Parent is ICompoundAssignmentOperation { Target: var t2 } && ReferenceEquals(t2, lref)) break;
                if (lref.Parent is IIncrementOrDecrementOperation { Target: var t3 } && ReferenceEquals(t3, lref)) break;
                var key = new TrackedKey.Symbol(lref.Local);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IParameterReferenceOperation pref:
            {
                // Skip: this pref is the assignment target, not a read.
                if (pref.Parent is ISimpleAssignmentOperation { Target: var t1 } && ReferenceEquals(t1, pref)) break;
                if (pref.Parent is ICompoundAssignmentOperation { Target: var t2 } && ReferenceEquals(t2, pref)) break;
                if (pref.Parent is IIncrementOrDecrementOperation { Target: var t3 } && ReferenceEquals(t3, pref)) break;
                var key = new TrackedKey.Symbol(pref.Parameter);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field } fref:
            {
                // Skip: this fref is the assignment target, not a read.
                if (fref.Parent is ISimpleAssignmentOperation { Target: var t1 } && ReferenceEquals(t1, fref)) break;
                if (fref.Parent is ICompoundAssignmentOperation { Target: var t2 } && ReferenceEquals(t2, fref)) break;
                var key = new TrackedKey.InstanceField(field);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IFlowCaptureReferenceOperation flowRef:
            {
                // No parent guard needed — captures are never assignment targets.
                var key = new TrackedKey.Capture(flowRef.Id);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
        }
    }
```

Keep `EnumerateBlockOps`/`EnumerateAllOps` — Pass 1 still uses them (it only counts def-sites; order is irrelevant there). Keep `RegisterDef`, `TryGetDefinitionKey`, `IsThisAccessingInvocation` unchanged.

- [ ] **Step 4: Build and run the new tests**

Run: `dotnet build src/OpenVulScan.Core/OpenVulScan.Core.csproj --configuration Release`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaBuilderPostOrderTests"`
Expected: 3 PASS.

- [ ] **Step 5: Run the whole Core test suite — audit any fallout**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj`
Expected: all pass. If an existing SSA test fails, inspect it: if it ASSERTS the old pre-order binding (a use bound to a post-def/post-kill version), update the expectation with a comment justifying the new (evaluation-order-correct) binding. If the failure is anything else, STOP and report BLOCKED with the test name and output.

- [ ] **Step 6: Run the full solution test suite**

Run: `dotnet test --configuration Release`
Expected: all pass (baseline 446 passed / 1 skipped, plus 3 new). V3022/V3063 rule tests must not change in this task (transfers/dispatcher untouched). If any Rules test fails, STOP and report BLOCKED.

- [ ] **Step 7: Commit**

```bash
git add src/OpenVulScan.Core/Ssa/SsaBuilder.cs tests/OpenVulScan.Core.Tests/Ssa/SsaBuilderPostOrderTests.cs
git commit -m "fix(core): SSA Pass 2 post-order walk — defs/kills after children (ovs-tr6 S-3)"
```

---

## Task 2: S-2 — flow-capture evaluation in both SSA transfers

**Files:**
- Modify: `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs`
- Modify: `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs`
- Create: `tests/OpenVulScan.Core.Tests/Ssa/SsaTransferCaptureTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/OpenVulScan.Core.Tests/Ssa/SsaTransferCaptureTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaTransferCaptureTests
{
    [Fact]
    public void CaptureDef_TakesNullStateOfCapturedExpression()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string M()
    {
        string a = ""x"";
        var t = a ?? ""y"";
        return t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, NullState>.Empty;

        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);

        // The capture of `a` (the ?? receiver) must carry a's value: NotNull.
        var captureOfA = AllOps(cfg).OfType<IFlowCaptureOperation>()
            .Single(c => c.Value is ILocalReferenceOperation);
        var captureId = index.DefinitionAt(captureOfA);
        Assert.NotNull(captureId);
        Assert.Equal(NullState.NotNull, state[captureId.Value]);
    }

    [Fact]
    public void CaptureReference_FlowsNullStateToConsumer()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string M()
    {
        string a = ""x"";
        var t = a ?? ""y"";
        return t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);
        var lattice = new MapLattice<SsaId, NullStateLattice, NullState>();
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, NullState>>(lattice, transfer);
        var result = solver.Solve(cfg, CancellationToken.None);

        // Both ?? arms are NotNull ("x" via the capture chain, "y" literal),
        // so t must be NotNull. Without capture evaluation it degrades to Unknown.
        var tSym = FindLocal(cfg, "t");
        var tVersion = index.AllVersions(new TrackedKey.Symbol(tSym)).Single();

        var exit = cfg.Blocks.Single(b => b.Kind == BasicBlockKind.Exit);
        Assert.Equal(NullState.NotNull, result.InStates[exit][tVersion]);
    }

    [Fact]
    public void CaptureReference_FlowsConstantToConsumer()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int M(bool c)
    {
        var t = c ? 5 : 5;
        return t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);
        var lattice = new MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>();
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, ConstantLatticeValue>>(lattice, transfer);
        var result = solver.Solve(cfg, CancellationToken.None);

        // Both ternary arms write Const(5) into the same capture; the join is
        // Const(5) and t reads it through the capture reference.
        var tSym = FindLocal(cfg, "t");
        var tVersion = index.AllVersions(new TrackedKey.Symbol(tSym)).Single();

        var exit = cfg.Blocks.Single(b => b.Kind == BasicBlockKind.Exit);
        Assert.Equal(ConstantLatticeValue.Const(5), result.InStates[exit][tVersion]);
    }

    private static ILocalSymbol FindLocal(ControlFlowGraph cfg, string name)
        => AllOps(cfg).OfType<ILocalReferenceOperation>().Select(l => l.Local)
            .Concat(AllOps(cfg).OfType<IVariableDeclaratorOperation>().Select(d => d.Symbol))
            .First(s => s.Name == name);

    private static System.Collections.Generic.IEnumerable<IOperation> AllOps(ControlFlowGraph cfg)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in block.Operations)
                foreach (var d in Descend(op))
                    yield return d;
            if (block.BranchValue is not null)
                foreach (var d in Descend(block.BranchValue))
                    yield return d;
        }

        static System.Collections.Generic.IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaTransferCaptureTests"`
Expected: 3 FAIL — capture defs fall through to `Unknown`/`Top` and capture references are not evaluated.
(If a test fails on plumbing — `Single()`/missing key — rather than on the value assertion, inspect the CFG shape first; the value assertions must be what fails.)

- [ ] **Step 3: Add the capture cases to both transfers**

In `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs`:

(a) In `Apply(state, operation)`, insert a case before the `_ =>` fallback:

```csharp
            ICompoundAssignmentOperation => NullState.Unknown,
            IFlowCaptureOperation capture => Evaluate(capture.Value, state),
            _ => NullState.Unknown,
```

(b) In `Evaluate`, insert a case before the `_ =>` fallback:

```csharp
            IFlowCaptureReferenceOperation cref =>
                Lookup(cref, new TrackedKey.Capture(cref.Id), state),
            _ => NullState.Unknown,
```

In `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs`:

(a) In `Apply(state, operation)`, insert before the `_ =>` fallback:

```csharp
            IFlowCaptureOperation capture => Evaluate(capture.Value, state),
            _ => ConstantLatticeValue.Top,
```

(b) In `Evaluate`, insert before the `_ =>` fallback:

```csharp
            IFlowCaptureReferenceOperation cref =>
                Lookup(cref, new TrackedKey.Capture(cref.Id), state),
            _ => ConstantLatticeValue.Top,
```

- [ ] **Step 4: Build and run the new tests**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 warnings.

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaTransferCaptureTests"`
Expected: 3 PASS.

- [ ] **Step 5: Run the full solution test suite**

Run: `dotnet test --configuration Release`
Expected: all pass. Capture evaluation may in principle sharpen V3022/V3063 results; if a Verify snapshot in Rules.Tests mismatches, inspect the received file — accept (rename `.received.txt` → `.verified.txt`) ONLY if the new diagnostic is genuinely correct (an always-true/false condition the analysis previously missed), and note it in the commit message. Otherwise STOP and report BLOCKED.

- [ ] **Step 6: Commit**

```bash
git add src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs tests/OpenVulScan.Core.Tests/Ssa/SsaTransferCaptureTests.cs
git commit -m "fix(core): evaluate flow captures in SSA transfers (ovs-tr6 S-2)"
```

---

## Task 3: S-1 — `ApplyPhis` in the transfer contract + dispatcher

**Files:**
- Modify: `src/OpenVulScan.Core/Lattice/ITransfer.cs`
- Modify: `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs`
- Modify: `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs`
- Modify: `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs`
- Create: `tests/OpenVulScan.Core.Tests/Ssa/SsaTransferApplyPhisTests.cs`
- Modify: `tests/OpenVulScan.Rules.Tests/V3022Tests.cs`

- [ ] **Step 1: Write the failing rule-level regression test**

Append to the `V3022Tests` class in `tests/OpenVulScan.Rules.Tests/V3022Tests.cs`:

```csharp
    [Fact]
    public void CrossBranchInvariantDetected()
    {
        var source = @"
class C
{
    void M(bool c)
    {
        int x = 5;
        if (c)
        {
            x = 5;
        }
        if (x == 5) { }
    }
}";
        var compilation = CreateTestCompilation(source);
        var rule = new V3022AlwaysTrueFalse();
        var dispatcher = new DataFlowRuleDispatcher<ImmutableDictionary<SsaId, ConstantLatticeValue>>(new[] { rule }, compilation);

        var diagnostics = dispatcher.Run(CancellationToken.None);

        // x is 5 on both paths into the join; the φ-join is Const(5), so
        // `x == 5` is always true. Requires φ-results to be visible to rules.
        Assert.Contains(diagnostics, d =>
            d.Id == "V3022" &&
            d.GetMessage(CultureInfo.InvariantCulture).Contains("always true", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Write the failing Core unit tests**

Create `tests/OpenVulScan.Core.Tests/Ssa/SsaTransferApplyPhisTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaTransferApplyPhisTests
{
    private const string JoinSnippet = @"
class C
{
    int M(bool c)
    {
        int x = 1;
        if (c)
        {
            x = 1;
        }
        return x;
    }
}";

    [Fact]
    public void ApplyPhis_Constant_JoinsPredecessorVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(JoinSnippet);
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);

        var joinBlock = cfg.Blocks.First(b => index.PhisAt(b).Count > 0);
        var phi = index.PhisAt(joinBlock).Single();

        var state = ImmutableDictionary<SsaId, ConstantLatticeValue>.Empty;
        foreach (var operand in phi.Operands)
            state = state.SetItem(operand.Version, ConstantLatticeValue.Const(1));

        var after = transfer.ApplyPhis(state, joinBlock);

        Assert.Equal(ConstantLatticeValue.Const(1), after[phi.Result]);
    }

    [Fact]
    public void ApplyPhis_NullState_JoinsPredecessorVersions()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(JoinSnippet);
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new NullStateSsaTransfer(index);

        var joinBlock = cfg.Blocks.First(b => index.PhisAt(b).Count > 0);
        var phi = index.PhisAt(joinBlock).Single();

        var state = ImmutableDictionary<SsaId, NullState>.Empty;
        foreach (var operand in phi.Operands)
            state = state.SetItem(operand.Version, NullState.NotNull);

        var after = transfer.ApplyPhis(state, joinBlock);

        Assert.Equal(NullState.NotNull, after[phi.Result]);
    }
}
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaTransferApplyPhisTests"`
Expected: COMPILE FAIL — `ApplyPhis` does not exist on the transfers yet.

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3022Tests.CrossBranchInvariantDetected"`
Expected: FAIL — no diagnostic produced (φ-results invisible to the rule).

- [ ] **Step 4: Add `ApplyPhis` to the contract and extract the φ-loops**

(a) In `src/OpenVulScan.Core/Lattice/ITransfer.cs`, add to the interface after the block-level `Apply`:

```csharp
    /// <summary>
    /// Materialises the φ-joins of <paramref name="block"/> into the incoming state.
    /// Called on block entry, before any operation of the block is applied.
    /// Non-SSA transfers need no φ handling; the default is the identity.
    /// </summary>
    /// <param name="state">The lattice state at block entry (pre-φ).</param>
    /// <param name="block">The control-flow basic block whose φ-functions to apply.</param>
    /// <returns>The lattice state with φ-results bound.</returns>
    T ApplyPhis(T state, BasicBlock block) => state;
```

(b) In `src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs`, replace the φ-loop inside `Apply(state, block)` (the `foreach (var phi in _ssa.PhisAt(block)) { … }` section including its comment) with a call, and add the extracted method:

```csharp
    /// <inheritdoc />
    public ImmutableDictionary<SsaId, NullState> Apply(
        ImmutableDictionary<SsaId, NullState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        state = ApplyPhis(state, block);

        foreach (var op in block.Operations.SelectMany(EnumerateOps))
            state = Apply(state, op);

        if (block.BranchValue is not null)
            foreach (var op in EnumerateOps(block.BranchValue))
                state = Apply(state, op);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, NullState> ApplyPhis(
        ImmutableDictionary<SsaId, NullState> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Join predecessor states into each φ-result on block entry.
        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = NullState.Unknown;
            var any = false;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                {
                    joined = any ? _lattice.Join(joined, s) : s;
                    any = true;
                }
            }
            state = state.SetItem(phi.Result, any ? joined : NullState.Unknown);
        }

        return state;
    }
```

(c) In `src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs`, the same extraction:

```csharp
    /// <inheritdoc />
    public ImmutableDictionary<SsaId, ConstantLatticeValue> Apply(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        state = ApplyPhis(state, block);

        foreach (var op in block.Operations.SelectMany(EnumerateOps))
            state = Apply(state, op);

        if (block.BranchValue is not null)
            foreach (var op in EnumerateOps(block.BranchValue))
                state = Apply(state, op);

        return state;
    }

    /// <inheritdoc />
    public ImmutableDictionary<SsaId, ConstantLatticeValue> ApplyPhis(
        ImmutableDictionary<SsaId, ConstantLatticeValue> state, BasicBlock block)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(block);

        // Join predecessor states into each φ-result on block entry.
        foreach (var phi in _ssa.PhisAt(block))
        {
            var joined = ConstantLatticeValue.Bottom;
            foreach (var operand in phi.Operands)
            {
                if (state.TryGetValue(operand.Version, out var s))
                    joined = _lattice.Join(joined, s);
            }
            state = state.SetItem(phi.Result, joined);
        }

        return state;
    }
```

(d) In `src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs`, in `Run`, change the block-state seeding:

```csharp
                        var state = result.InStates[block];
                        state = transfer.ApplyPhis(state, block);
```

- [ ] **Step 5: Build and run all the new tests**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 warnings (legacy `NullStateTransfer` compiles unchanged via the default interface method).

Run: `dotnet test tests/OpenVulScan.Core.Tests/OpenVulScan.Core.Tests.csproj --filter "FullyQualifiedName~SsaTransferApplyPhisTests"`
Expected: 2 PASS.

Run: `dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj --filter "FullyQualifiedName~V3022Tests"`
Expected: ALL V3022 tests PASS, including `CrossBranchInvariantDetected`.

- [ ] **Step 6: Run the full solution test suite — audit snapshot fallout**

Run: `dotnet test --configuration Release`
Expected: all pass. φ-visibility may sharpen existing V3022/V3063 Verify snapshots (new, genuinely-correct diagnostics appearing). For any mismatch: inspect the received file; accept ONLY if the new diagnostic is a real always-true/false the analysis previously missed (rename `.received.txt` → `.verified.txt`, mention in commit message); otherwise STOP and report BLOCKED.

- [ ] **Step 7: Commit**

```bash
git add src/OpenVulScan.Core/Lattice/ITransfer.cs src/OpenVulScan.Core/Lattice/NullStateSsaTransfer.cs src/OpenVulScan.Core/Lattice/ConstantSsaTransfer.cs src/OpenVulScan.RuleEngine/DataFlowRuleDispatcher.cs tests/OpenVulScan.Core.Tests/Ssa/SsaTransferApplyPhisTests.cs tests/OpenVulScan.Rules.Tests/V3022Tests.cs
git commit -m "feat(core): ApplyPhis in transfer contract; dispatcher exposes phi results to rules (ovs-tr6 S-1)"
```

---

## Task 4: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full Release test suite**

Run: `dotnet test --configuration Release`
Expected: 0 failures. Baseline was 446 passed / 1 skipped; Tasks 1–3 add 9 tests (3+3+3) → expect ~455 passed / 1 skipped (plus any snapshots intentionally re-verified in Tasks 2–3). Report exact numbers.

- [ ] **Step 2: Clean Release build**

Run: `dotnet build --configuration Release --no-restore`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit only if anything changed**

If steps produced no file changes, nothing to commit. Otherwise:

```bash
git add -A
git commit -m "chore(core): SSA precision hardening verification"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- §3.1 S-1 (`ApplyPhis` default in `ITransfer`, extraction in both transfers, dispatcher call) → Task 3 steps 4a–4d; V3022 regression (spec §5) → Task 3 step 1; φ-join equivalence units → Task 3 step 2.
- §3.2 S-2 (def-side `IFlowCaptureOperation` + use-side `IFlowCaptureReferenceOperation` in both transfers) → Task 2 step 3; tests (spec §5) → Task 2 step 1 (def-side NullState, use-side NullState via `??`, use+def Constant via ternary).
- §3.3 S-3 (recursive post-order walk; defs/captures/kills after children; Pass 1 and parent-guards unchanged) → Task 1 step 3; the three behavioural fixes → Task 1 step 1 tests (field-def-survives-kill, arg-read-pre-kill, self-ref-old-version).
- §5 acceptance (full suite green, justified snapshot updates, clean build) → Tasks 1/2/3 final steps + Task 4.
- §6 risks: default interface method (Task 3 step 5 verifies legacy transfer compiles); changed-snapshot justification (Tasks 2/3 step 6); φ idempotence (dispatcher reuses the same `SetItem` computation the solver ran — Task 3 step 4d).

**Placeholder scan:** none — all code steps contain complete code; commands have expected outcomes.

**Type consistency:** `Walk`/`WalkChildren`/`RecordUse`/`TopLevelBlockOps` defined once (Task 1) and not referenced elsewhere; `ApplyPhis` signature identical across `ITransfer`, both transfers, and the dispatcher call; test helpers (`AllOps`, `FindLocal`) are file-local; `MapLattice<SsaId, NullStateLattice, NullState>` / `MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>` match the 3-parameter signature used by V3022.
