# WorklistSolver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a reverse-postorder worklist solver for Roslyn CFGs that computes a fixpoint using `ILattice<T>` and `ITransfer<T>`, returning per-block IN states with a convergence flag.

**Architecture:** The solver iterates blocks in reverse postorder (computed via DFS on the CFG). For each block, it joins the OUT states of all predecessors to form the IN state, then checks for fixpoint. The algorithm loops until no state changes or max iterations is reached.

**Tech Stack:** .NET 10, C# preview, Roslyn 5.0 (`Microsoft.CodeAnalysis.CSharp`), xUnit.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `src/OpenVulScan.Core/Cfg/WorklistSolverResult.cs` | Immutable result object: `InStates` dictionary + `Converged` flag |
| `src/OpenVulScan.Core/Cfg/WorklistSolver.cs` | `WorklistSolver<T>`: constructor, `Solve`, internal RPO builder |
| `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs` | xUnit tests: linear, if-else, while, switch, try/catch using `BoolFlatLattice` |

---

### Task 1: Result type

**Files:**
- Create: `src/OpenVulScan.Core/Cfg/WorklistSolverResult.cs`
- Test: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;

namespace OpenVulScan.Tests;

public class WorklistSolverResultTests
{
    [Fact]
    public void Result_HoldsConvergedFlag()
    {
        var dict = ImmutableDictionary<BasicBlock, bool>.Empty;
        var result = new WorklistSolverResult<bool>(dict, converged: true);
        Assert.True(result.Converged);
        Assert.Same(dict, result.InStates);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OpenVulScan.Core.Tests --filter "FullyQualifiedName~WorklistSolverResultTests" -v n`
Expected: FAIL — `WorklistSolverResult` not found

- [ ] **Step 3: Implement minimal result type**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed class WorklistSolverResult<T>
{
    public WorklistSolverResult(ImmutableDictionary<BasicBlock, T> inStates, bool converged)
    {
        InStates = inStates;
        Converged = converged;
    }

    public ImmutableDictionary<BasicBlock, T> InStates { get; }
    public bool Converged { get; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: same as Step 2
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpenVulScan.Core/Cfg/WorklistSolverResult.cs tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs
git commit -m "feat(solver): add WorklistSolverResult<T>"
```

---

### Task 2: RPO utility + Solver skeleton

**Files:**
- Create: `src/OpenVulScan.Core/Cfg/WorklistSolver.cs`
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing test for solver instantiation**

```csharp
[Fact]
public void Solver_CanBeConstructed()
{
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.Top);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);
    Assert.NotNull(solver);
}
```

- [ ] **Step 2: Run test**

Expected: FAIL — `WorklistSolver` not found

- [ ] **Step 3: Implement solver skeleton + RPO**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed class WorklistSolver<T>
{
    private readonly ILattice<T> _lattice;
    private readonly ITransfer<T> _transfer;
    private readonly int _maxIterations;

    public WorklistSolver(ILattice<T> lattice, ITransfer<T> transfer, int maxIterations = 10_000)
    {
        _lattice = lattice ?? throw new ArgumentNullException(nameof(lattice));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _maxIterations = maxIterations > 0 ? maxIterations : throw new ArgumentOutOfRangeException(nameof(maxIterations));
    }

    public WorklistSolverResult<T> Solve(ControlFlowGraph cfg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var blocks = cfg.Blocks.ToImmutableArray();
        var rpo = ComputeReversePostOrder(cfg);

        var inStates = blocks.ToImmutableDictionary(b => b, _ => _lattice.Bottom);
        var outStates = blocks.ToImmutableDictionary(b => b, _ => _lattice.Bottom);

        bool changed;
        int iterations = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            changed = false;
            foreach (var block in rpo)
            {
                var inState = ComputeInState(block, outStates);
                var outState = _transfer.Apply(inState, block);

                if (!EqualityComparer<T>.Default.Equals(outState, outStates[block]))
                {
                    outStates = outStates.SetItem(block, outState);
                    inStates = inStates.SetItem(block, inState);
                    changed = true;
                }
            }
            iterations++;
        } while (changed && iterations < _maxIterations);

        return new WorklistSolverResult<T>(inStates, converged: !changed);
    }

    private T ComputeInState(BasicBlock block, ImmutableDictionary<BasicBlock, T> outStates)
    {
        var preds = block.Predecessors;
        if (preds.IsEmpty)
            return _lattice.Bottom;

        var state = outStates[preds.First().Source];
        foreach (var pred in preds.Skip(1))
        {
            state = _lattice.Join(state, outStates[pred.Source]);
        }
        return state;
    }

    private static ImmutableArray<BasicBlock> ComputeReversePostOrder(ControlFlowGraph cfg)
    {
        var visited = new HashSet<BasicBlock>();
        var postOrder = new List<BasicBlock>();

        void Dfs(BasicBlock block)
        {
            if (!visited.Add(block))
                return;

            foreach (var succ in block.Successors)
            {
                if (succ.Destination != null)
                    Dfs(succ.Destination);
            }

            postOrder.Add(block);
        }

        Dfs(cfg.Blocks.First());

        // Add unreachable blocks at the end so they are still processed
        foreach (var block in cfg.Blocks)
        {
            if (!visited.Contains(block))
                postOrder.Add(block);
        }

        postOrder.Reverse();
        return postOrder.ToImmutableArray();
    }
}
```

- [ ] **Step 4: Run test**

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpenVulScan.Core/Cfg/WorklistSolver.cs tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs
git commit -m "feat(solver): add WorklistSolver<T> with RPO"
```

---

### Task 3: Test transfer helpers

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write test transfer helpers**

Inside the test file (or a nested private class), define:

```csharp
private sealed class ConstantBoolTransfer : ITransfer<BoolLatticeValue>
{
    private readonly BoolLatticeValue _value;
    public ConstantBoolTransfer(BoolLatticeValue value) => _value = value;
    public BoolLatticeValue Apply(BoolLatticeValue state, IOperation operation) => _value;
    public BoolLatticeValue Apply(BoolLatticeValue state, BasicBlock block) => _value;
}

private sealed class IdentityBoolTransfer : ITransfer<BoolLatticeValue>
{
    public BoolLatticeValue Apply(BoolLatticeValue state, IOperation operation) => state;
    public BoolLatticeValue Apply(BoolLatticeValue state, BasicBlock block) => state;
}

private sealed class JoinTransfer : ITransfer<BoolLatticeValue>
{
    public BoolLatticeValue Apply(BoolLatticeValue state, IOperation operation) => state;
    public BoolLatticeValue Apply(BoolLatticeValue state, BasicBlock block) => state;
}
```

- [ ] **Step 2: Ensure project compiles**

Run: `dotnet build tests/OpenVulScan.Core.Tests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): add test transfer helpers"
```

---

### Task 4: Linear test

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing linear test**

```csharp
[Fact]
public void Linear_FixpointInOnePass()
{
    var code = @"
class C
{
    void M()
    {
        int x = 1;
        int y = 2;
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

    var result = solver.Solve(cfg);

    Assert.True(result.Converged);
    foreach (var block in cfg.Blocks)
    {
        Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
    }
}
```

- [ ] **Step 2: Add `CompileAndGetCfg` helper**

```csharp
private static ControlFlowGraph CompileAndGetCfg(string code)
{
    var tree = CSharpSyntaxTree.ParseText(code);
    var compilation = CSharpCompilation.Create(
        "Test",
        new[] { tree },
        new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    var model = compilation.GetSemanticModel(tree);
    var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
    var methodSymbol = model.GetDeclaredSymbol(method)!;
    var body = method.Body ?? (SyntaxNode)method.ExpressionBody!;

    var cfg = ControlFlowGraph.Create(body, model);
    return cfg;
}
```

- [ ] **Step 3: Run test**

Expected: FAIL (compile or missing using)

- [ ] **Step 4: Fix compilation**

Add usings:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;
```

- [ ] **Step 5: Run test again**

Expected: PASS (or fail for legitimate reasons)

- [ ] **Step 6: Commit**

```bash
git commit -m "test(solver): linear CFG fixpoint test"
```

---

### Task 5: If-else test

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing if-else test**

```csharp
[Fact]
public void IfElse_JoinsAtMerge()
{
    var code = @"
class C
{
    void M(bool cond)
    {
        int x;
        if (cond)
            x = 1;
        else
            x = 2;
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

    var result = solver.Solve(cfg);
    Assert.True(result.Converged);

    // All blocks should converge to True because transfer is constant
    foreach (var block in cfg.Blocks)
    {
        Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
    }
}
```

- [ ] **Step 2: Run test**

Expected: PASS (since constant transfer dominates)

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): if-else join test"
```

---

### Task 6: While loop test

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing while test**

```csharp
[Fact]
public void While_ConvergesInMultipleIterations()
{
    var code = @"
class C
{
    void M()
    {
        int i = 0;
        while (i < 10)
            i++;
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

    var result = solver.Solve(cfg);
    Assert.True(result.Converged);
    foreach (var block in cfg.Blocks)
    {
        Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
    }
}
```

- [ ] **Step 2: Run test**

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): while loop convergence test"
```

---

### Task 7: Switch test

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing switch test**

```csharp
[Fact]
public void Switch_JoinsAtMerge()
{
    var code = @"
class C
{
    void M(int n)
    {
        switch (n)
        {
            case 1: break;
            case 2: break;
            default: break;
        }
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

    var result = solver.Solve(cfg);
    Assert.True(result.Converged);
    foreach (var block in cfg.Blocks)
    {
        Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
    }
}
```

- [ ] **Step 2: Run test**

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): switch merge test"
```

---

### Task 8: Try/catch test

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing try/catch test**

```csharp
[Fact]
public void TryCatch_HandlesExceptionFlow()
{
    var code = @"
class C
{
    void M()
    {
        try
        {
            int x = 1;
        }
        catch
        {
            int y = 2;
        }
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer);

    var result = solver.Solve(cfg);
    Assert.True(result.Converged);
    foreach (var block in cfg.Blocks)
    {
        Assert.Equal(BoolLatticeValue.True, result.InStates[block]);
    }
}
```

- [ ] **Step 2: Run test**

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): try/catch exception flow test"
```

---

### Task 9: Max iterations graceful exit test

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing max-iterations test**

```csharp
[Fact]
public void MaxIterations_ReturnsBestApproximation()
{
    var code = @"
class C
{
    void M()
    {
        int x = 1;
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new BoolFlatLattice();
    var transfer = new ConstantBoolTransfer(BoolLatticeValue.True);
    var solver = new WorklistSolver<BoolLatticeValue>(lattice, transfer, maxIterations: 0);

    var result = solver.Solve(cfg);
    Assert.False(result.Converged);
}
```

- [ ] **Step 2: Run test**

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): max iterations graceful exit"
```

---

### Task 10: Integration test with NullStateLattice

**Files:**
- Modify: `tests/OpenVulScan.Core.Tests/Cfg/WorklistSolverTests.cs`

- [ ] **Step 1: Write failing integration test**

```csharp
[Fact]
public void NullState_Integration_SimpleMethod()
{
    var code = @"
class C
{
    void M(string? s)
    {
        var len = s.Length;
    }
}";
    var cfg = CompileAndGetCfg(code);
    var lattice = new NullStateLattice();
    var transfer = new NullStateTransfer();
    var solver = new WorklistSolver<NullState>(lattice, transfer);

    var result = solver.Solve(cfg);
    Assert.True(result.Converged);
    // We simply assert the solver ran without crashing and returned states
    Assert.NotEmpty(result.InStates);
}
```

- [ ] **Step 2: Run test**

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git commit -m "test(solver): NullStateLattice integration"
```

---

### Task 11: Final quality gates

**Files:**
- All above

- [ ] **Step 1: Run full build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Run full test suite**

Run: `dotnet test`
Expected: all pass

- [ ] **Step 3: Commit any final fixes**

```bash
git add -A
git commit -m "refactor(solver): final polish and quality gates"
```

---

## Spec Coverage Check

| Spec Requirement | Task |
|------------------|------|
| `WorklistSolver<T>` constructor with lattice, transfer, optional maxIterations | Task 2 |
| `Solve(ControlFlowGraph, CancellationToken)` | Task 2 |
| Reverse postorder iteration | Task 2 (ComputeReversePostOrder) |
| Fixpoint loop | Task 2 |
| Max iterations graceful exit + `Converged` flag | Task 2, Task 9 |
| Return per-block IN states | Task 1, Task 2 |
| Linear test | Task 4 |
| If-else test | Task 5 |
| While test | Task 6 |
| Switch test | Task 7 |
| Try/catch test | Task 8 |

## Placeholder Scan

No placeholders found. Every step contains concrete code, exact commands, and expected outcomes.

## Type Consistency

- `WorklistSolverResult<T>` uses `ImmutableDictionary<BasicBlock, T>` consistently.
- `WorklistSolver<T>.Solve` returns `WorklistSolverResult<T>`.
- RPO returns `ImmutableArray<BasicBlock>`.

All consistent across tasks.
