using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests;

public class WorklistSolverResultTests
{
    [Fact]
    public void Result_HoldsConvergedFlag()
    {
        var inDict = ImmutableDictionary<BasicBlock, bool>.Empty;
        var outDict = ImmutableDictionary<BasicBlock, bool>.Empty;
        var result = new WorklistSolverResult<bool>(inDict, outDict, converged: true);
        Assert.True(result.Converged);
        Assert.Same(inDict, result.InStates);
        Assert.Same(outDict, result.OutStates);
    }
}

public class WorklistSolverTests
{
    [Fact]
    public void Solver_CanBeConstructed()
    {
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);
        Assert.NotNull(solver);
    }

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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        var result = solver.Solve(cfg);

        Assert.True(result.Converged);
        // Every reachable block should have all preceding block ordinals in its IN state
        var blocks = cfg.Blocks.Where(b => b.Kind != BasicBlockKind.Entry).ToList();
        Assert.True(blocks.Count >= 2, "Expected at least 2 non-entry blocks");
        
        // The first non-entry block should have the entry block in its predecessors
        var firstBlock = blocks.First();
        Assert.Contains(cfg.Blocks.First(b => b.Kind == BasicBlockKind.Entry).Ordinal, result.InStates[firstBlock]);
    }

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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);

        // Find merge block (block with multiple predecessors that isn't entry)
        var mergeBlock = cfg.Blocks.FirstOrDefault(b => b.Predecessors.Length > 1 && b.Kind != BasicBlockKind.Entry);
        Assert.NotNull(mergeBlock);

        // Merge block's IN state should contain ordinals from both branches
        var predOrdinals = mergeBlock.Predecessors.Select(p => p.Source.Ordinal).ToList();
        Assert.True(predOrdinals.Count >= 2, "Expected at least 2 predecessors");
        
        foreach (var predOrdinal in predOrdinals)
        {
            Assert.Contains(predOrdinal, result.InStates[mergeBlock]);
        }
    }

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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);

        // Find loop header (block with back edge predecessor)
        var loopHeader = cfg.Blocks.FirstOrDefault(b => b.Predecessors.Any(p => p.Source.Ordinal >= b.Ordinal));
        Assert.NotNull(loopHeader);

        // Loop header should contain ordinals from its back-edge predecessor(s)
        // This proves convergence required multiple iterations
        var backEdgePredOrdinals = loopHeader.Predecessors
            .Where(p => p.Source.Ordinal >= loopHeader.Ordinal)
            .Select(p => p.Source.Ordinal)
            .ToList();
        
        Assert.NotEmpty(backEdgePredOrdinals);
        foreach (var predOrdinal in backEdgePredOrdinals)
        {
            Assert.Contains(predOrdinal, result.InStates[loopHeader]);
        }
    }

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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);

        // Find merge block after switch
        var mergeBlock = cfg.Blocks.FirstOrDefault(b => b.Predecessors.Length > 1 && b.Kind != BasicBlockKind.Entry);
        Assert.NotNull(mergeBlock);

        // Merge block should contain ordinals from all case branches
        var predOrdinals = mergeBlock.Predecessors.Select(p => p.Source.Ordinal).ToList();
        Assert.True(predOrdinals.Count >= 2, "Expected at least 2 predecessors for switch merge");
        
        foreach (var predOrdinal in predOrdinals)
        {
            Assert.Contains(predOrdinal, result.InStates[mergeBlock]);
        }
    }

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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        var result = solver.Solve(cfg);
        Assert.True(result.Converged);

        // In Roslyn CFG, catch blocks have NO explicit predecessors (exception edge is implicit).
        // However, the exit block merges both try and catch paths.
        // Verify the exit block's IN state contains ordinals from both try and catch blocks.
        var exitBlock = cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Exit);
        Assert.NotNull(exitBlock);
        Assert.True(exitBlock.Predecessors.Length >= 2, "Exit block should have at least 2 predecessors (try + catch)");

        var predOrdinals = exitBlock.Predecessors.Select(p => p.Source.Ordinal).ToList();
        foreach (var predOrdinal in predOrdinals)
        {
            Assert.Contains(predOrdinal, result.InStates[exitBlock]);
        }

        // Also verify the catch block itself was processed (it has an IN state even if Bottom)
        var catchBlock = cfg.Blocks.FirstOrDefault(b => b.EnclosingRegion?.Kind == ControlFlowRegionKind.Catch);
        Assert.NotNull(catchBlock);
        Assert.True(result.InStates.ContainsKey(catchBlock), "Catch block should have an IN state");
    }

    [Fact]
    public void MaxIterationsZero_ReturnsBottom()
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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer, maxIterations: 0);

        var result = solver.Solve(cfg);
        Assert.False(result.Converged);
    }

    [Fact]
    public void Solver_IsIdempotent()
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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        var result1 = solver.Solve(cfg);
        var result2 = solver.Solve(cfg);

        Assert.True(result1.Converged);
        Assert.True(result2.Converged);
        Assert.Equal(result1.InStates.Count, result2.InStates.Count);
        
        foreach (var block in cfg.Blocks)
        {
            Assert.Equal(result1.InStates[block], result2.InStates[block]);
        }
    }

    [Fact]
    public void CancellationToken_ThrowsWhenCancelled()
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
        var lattice = new BlockReachabilityLattice();
        var transfer = new BlockReachabilityTransfer();
        var solver = new WorklistSolver<ImmutableHashSet<int>>(lattice, transfer);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => solver.Solve(cfg, cts.Token));
    }

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
        Assert.NotEmpty(result.InStates);

        var entryBlock = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Entry);
        Assert.Equal(NullState.Unknown, result.InStates[entryBlock]);

        // All blocks should have Unknown state since NullStateTransfer
        // doesn't track parameter nullability without symbol table integration
        foreach (var block in cfg.Blocks)
        {
            Assert.Equal(NullState.Unknown, result.InStates[block]);
        }
    }

    // --- Test lattice and transfer ---

    private sealed class BlockReachabilityLattice : ILattice<ImmutableHashSet<int>>
    {
        public ImmutableHashSet<int> Bottom => ImmutableHashSet<int>.Empty;

        public ImmutableHashSet<int> Top => throw new InvalidOperationException("BlockReachabilityLattice has no finite Top element.");

        public ImmutableHashSet<int> Join(ImmutableHashSet<int> left, ImmutableHashSet<int> right)
            => left.Union(right);

        public bool LessOrEqual(ImmutableHashSet<int> left, ImmutableHashSet<int> right)
            => left.IsSubsetOf(right);
    }

    private sealed class BlockReachabilityTransfer : ITransfer<ImmutableHashSet<int>>
    {
        public ImmutableHashSet<int> Apply(ImmutableHashSet<int> state, IOperation operation)
            => state;

        public ImmutableHashSet<int> Apply(ImmutableHashSet<int> state, BasicBlock block)
            => state.Add(block.Ordinal);
    }

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
        var operation = model.GetOperation(method) ?? throw new InvalidOperationException("Failed to get operation for method");

        return operation switch
        {
            Microsoft.CodeAnalysis.Operations.IMethodBodyOperation methodBodyOp => ControlFlowGraph.Create(methodBodyOp),
            Microsoft.CodeAnalysis.Operations.IBlockOperation blockOp => ControlFlowGraph.Create(blockOp),
            _ => throw new InvalidOperationException($"Unsupported operation type: {operation.Kind}")
        };
    }
}
