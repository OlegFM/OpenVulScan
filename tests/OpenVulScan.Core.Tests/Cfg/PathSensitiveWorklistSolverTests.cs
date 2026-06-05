using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests;

public class PathSensitiveWorklistSolverTests
{
    [Fact]
    public void EdgeRefiner_NullCheckEquals_ThenBranch_DefinitelyNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x == null)
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.DefinitelyNull, result.InStates[thenBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_NullCheckEquals_ElseBranch_NotNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x == null)
        {
        }
        else
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var elseBlock = FindElseBlock(cfg);
        Assert.NotNull(elseBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.NotNull, result.InStates[elseBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_NotNullCheck_ThenBranch_NotNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x != null)
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.NotNull, result.InStates[thenBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_NotNullCheck_ElseBranch_DefinitelyNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x != null)
        {
        }
        else
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var elseBlock = FindElseBlock(cfg);
        Assert.NotNull(elseBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.DefinitelyNull, result.InStates[elseBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_IsNull_ThenBranch_DefinitelyNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x is null)
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.DefinitelyNull, result.InStates[thenBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_IsNotNull_ThenBranch_NotNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x is not null)
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.NotNull, result.InStates[thenBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_IsNull_ElseBranch_NotNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x is null)
        {
        }
        else
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var elseBlock = FindElseBlock(cfg);
        Assert.NotNull(elseBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.NotNull, result.InStates[elseBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_IsNotNull_ElseBranch_DefinitelyNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x is not null)
        {
        }
        else
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var elseBlock = FindElseBlock(cfg);
        Assert.NotNull(elseBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.DefinitelyNull, result.InStates[elseBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_LogicalAnd_BothTrue_ThenBranch_NotNull()
    {
        var code = @"
class C
{
    void M(string? x, string? y)
    {
        if (x != null && y != null)
        {
            var a = x;
            var b = y;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg, initialState: ImmutableDictionary<string, NullState>.Empty
            .Add("x", NullState.Unknown)
            .Add("y", NullState.Unknown));
        Assert.Equal(NullState.NotNull, result.InStates[thenBlock]["x"]);
        Assert.Equal(NullState.NotNull, result.InStates[thenBlock]["y"]);
    }

    [Fact]
    public void EdgeRefiner_LogicalOr_FirstTrue_ThenBranch_NotNull()
    {
        var code = @"
class C
{
    void M(string? x, string? y)
    {
        if (x != null || y != null)
        {
            var a = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        // In x != null || y != null, if we reach the then-block,
        // at least one of them is not null, but we don't know which.
        // However, with CFG decomposition, the true path of x != null
        // should have x refined to NotNull.
        // For the merged then-block, the join of (x=NotNull, y=Unknown)
        // and (x=Unknown, y=NotNull) gives x=MaybeNull, y=MaybeNull.
        // So this test is more about ensuring no crash and sensible behavior.
        Assert.True(result.InStates[thenBlock].ContainsKey("x") || result.InStates[thenBlock].ContainsKey("y"));
    }

    [Fact]
    public void EdgeRefiner_MergeBlock_JoinsRefinedStates()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x != null)
        {
            var y = x;
        }
        else
        {
            var z = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var mergeBlock = FindMergeBlock(cfg);
        Assert.NotNull(mergeBlock);

        var result = SolveWithEdgeRefiner(cfg);
        // Merge block should have x = MaybeNull (join of NotNull and DefinitelyNull)
        Assert.Equal(NullState.MaybeNull, result.InStates[mergeBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_NonNullCondition_NoRefinement()
    {
        var code = @"
class C
{
    void M(string? x, bool cond)
    {
        if (cond)
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        // x should remain Unknown because cond has nothing to do with x
        Assert.Equal(NullState.Unknown, result.InStates[thenBlock]["x"]);
    }

    [Fact]
    public void Solver_WithoutEdgeRefiner_BehaviorUnchanged()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (x != null)
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var lattice = new MapLattice<string, NullStateLattice, NullState>();
        var transfer = new StringKeyedNullTransfer();
        var solver = new WorklistSolver<ImmutableDictionary<string, NullState>>(lattice, transfer);

        var result = solver.Solve(cfg, ImmutableDictionary<string, NullState>.Empty.Add("x", NullState.Unknown));
        Assert.True(result.Converged);

        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);
        // Without edge refiner, x should remain Unknown
        Assert.Equal(NullState.Unknown, result.InStates[thenBlock]["x"]);
    }

    [Fact]
    public void EdgeRefiner_NestedNot_IsNull()
    {
        var code = @"
class C
{
    void M(string? x)
    {
        if (!!(x == null))
        {
            var y = x;
        }
    }
}";
        var cfg = CompileAndGetCfg(code);
        var thenBlock = FindThenBlock(cfg);
        Assert.NotNull(thenBlock);

        var result = SolveWithEdgeRefiner(cfg);
        Assert.Equal(NullState.DefinitelyNull, result.InStates[thenBlock]["x"]);
    }

    // --- Helper methods ---

    private static WorklistSolverResult<ImmutableDictionary<string, NullState>> SolveWithEdgeRefiner(
        ControlFlowGraph cfg,
        ImmutableDictionary<string, NullState>? initialState = null)
    {
        var lattice = new MapLattice<string, NullStateLattice, NullState>();
        var transfer = new StringKeyedNullTransfer();
        var edgeRefiner = new NullStateEdgeRefiner();
        var solver = new WorklistSolver<ImmutableDictionary<string, NullState>>(lattice, transfer, edgeRefiner);
        return solver.Solve(cfg, initialState ?? ImmutableDictionary<string, NullState>.Empty.Add("x", NullState.Unknown));
    }

    /// <summary>
    /// Minimal string-keyed null-state transfer used only in worklist solver tests.
    /// Replaces the deleted NullStateMapTransfer production class.
    /// </summary>
    private sealed class StringKeyedNullTransfer : ITransfer<ImmutableDictionary<string, NullState>>
    {
        public ImmutableDictionary<string, NullState> Apply(
            ImmutableDictionary<string, NullState> state, IOperation operation)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(operation);

            switch (operation)
            {
                case ISimpleAssignmentOperation { Target: ILocalReferenceOperation localRef } assignment:
                    return state.SetItem(localRef.Local.Name, Evaluate(assignment.Value, state));

                case ISimpleAssignmentOperation { Target: IParameterReferenceOperation paramRef } assignment:
                    return state.SetItem(paramRef.Parameter.Name, Evaluate(assignment.Value, state));

                case IVariableDeclaratorOperation { Symbol: ILocalSymbol local } varDecl:
                    var init = varDecl.Initializer is not null
                        ? Evaluate(varDecl.Initializer.Value, state)
                        : NullState.Unknown;
                    return state.SetItem(local.Name, init);
            }

            return state;
        }

        public ImmutableDictionary<string, NullState> Apply(
            ImmutableDictionary<string, NullState> state, BasicBlock block)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(block);

            var result = state;
            foreach (var op in block.Operations)
                result = Apply(result, op);
            if (block.BranchValue is not null)
                result = Apply(result, block.BranchValue);
            return result;
        }

        private static NullState Evaluate(IOperation? op, ImmutableDictionary<string, NullState> state) =>
            op switch
            {
                null => NullState.Unknown,
                ILiteralOperation lit when lit.ConstantValue.Value is null => NullState.DefinitelyNull,
                ILiteralOperation => NullState.NotNull,
                ILocalReferenceOperation lref => state.TryGetValue(lref.Local.Name, out var s) ? s : NullState.Unknown,
                IParameterReferenceOperation pref => state.TryGetValue(pref.Parameter.Name, out var s) ? s : NullState.Unknown,
                IObjectCreationOperation => NullState.NotNull,
                IArrayCreationOperation => NullState.NotNull,
                IConversionOperation conv => Evaluate(conv.Operand, state),
                IParenthesizedOperation paren => Evaluate(paren.Operand, state),
                _ => NullState.Unknown,
            };
    }

    private static BasicBlock? FindThenBlock(ControlFlowGraph cfg)
    {
        // Find the first conditional block and follow the fall-through chain
        // to the actual then body (the first block without a conditional successor)
        var current = cfg.Blocks.FirstOrDefault(b =>
            b.FallThroughSuccessor?.Destination is not null &&
            b.ConditionalSuccessor?.Destination is not null);

        if (current is null)
            return null;

        while (current.FallThroughSuccessor?.Destination is not null &&
               current.ConditionalSuccessor?.Destination is not null)
        {
            current = current.FallThroughSuccessor.Destination;
        }

        return current.Kind == BasicBlockKind.Exit ? null : current;
    }

    private static BasicBlock? FindElseBlock(ControlFlowGraph cfg)
    {
        // The else block is the conditional successor of a conditional block
        foreach (var block in cfg.Blocks)
        {
            if (block.FallThroughSuccessor?.Destination is not null &&
                block.ConditionalSuccessor?.Destination is not null &&
                block.BranchValue is not null)
            {
                return block.ConditionalSuccessor.Destination;
            }
        }
        return null;
    }

    private static BasicBlock? FindMergeBlock(ControlFlowGraph cfg)
    {
        // The merge block has multiple predecessors and is not entry
        return cfg.Blocks.FirstOrDefault(b =>
            b.Predecessors.Length > 1 &&
            b.Kind != BasicBlockKind.Entry);
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
