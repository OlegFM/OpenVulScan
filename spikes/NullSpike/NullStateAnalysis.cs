using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace NullSpike;

internal sealed class NullStateAnalysis
{
    private readonly ControlFlowGraph _cfg;
    public NullStateAnalysis(ControlFlowGraph cfg)
    {
        _cfg = cfg;
    }

    public ImmutableDictionary<ILocalSymbol, NullState> Analyze()
    {
        var emptyState = ImmutableDictionary<ILocalSymbol, NullState>.Empty.WithComparers(SymbolEqualityComparer.Default);
        var blockStates = new Dictionary<BasicBlock, ImmutableDictionary<ILocalSymbol, NullState>>();
        var worklist = new Queue<BasicBlock>();

        // Collect all locals and initialize to Unknown at method entry
        var allLocals = CollectLocals(_cfg);
        var initialState = emptyState;
        foreach (var local in allLocals)
        {
            initialState = initialState.SetItem(local, NullState.Unknown);
        }

        var entryBlock = _cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Entry);

        // Initialize all blocks with empty state
        foreach (var block in _cfg.Blocks)
        {
            blockStates[block] = block == entryBlock ? initialState : emptyState;
        }

        // Enqueue all blocks initially
        foreach (var block in _cfg.Blocks)
        {
            worklist.Enqueue(block);
        }

        while (worklist.Count > 0)
        {
            var block = worklist.Dequeue();
            var incoming = MergeIncomingStates(block, blockStates);
            var outgoing = TransferBlock(block, incoming);

            if (!StatesEqual(outgoing, blockStates[block]))
            {
                blockStates[block] = outgoing;

                // Enqueue successors
                if (block.FallThroughSuccessor?.Destination is not null)
                    worklist.Enqueue(block.FallThroughSuccessor.Destination);
                if (block.ConditionalSuccessor?.Destination is not null)
                    worklist.Enqueue(block.ConditionalSuccessor.Destination);
            }
        }

        // Return the state at the exit block
        var exitBlock = _cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Exit);
        return exitBlock is not null ? blockStates[exitBlock] : emptyState;
    }

    private static ImmutableHashSet<ILocalSymbol> CollectLocals(ControlFlowGraph cfg)
    {
        var locals = ImmutableHashSet<ILocalSymbol>.Empty.WithComparer(SymbolEqualityComparer.Default);
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in block.Operations)
            {
                locals = CollectLocals(op, locals);
            }
            if (block.BranchValue is not null)
            {
                locals = CollectLocals(block.BranchValue, locals);
            }
        }
        return locals;
    }

    private static ImmutableHashSet<ILocalSymbol> CollectLocals(IOperation op, ImmutableHashSet<ILocalSymbol> locals)
    {
        switch (op)
        {
            case ILocalReferenceOperation localRef:
                locals = locals.Add(localRef.Local);
                break;
            case IVariableDeclaratorOperation varDecl when varDecl.Symbol is ILocalSymbol local:
                locals = locals.Add(local);
                break;
        }

        foreach (var child in op.ChildOperations)
        {
            locals = CollectLocals(child, locals);
        }

        return locals;
    }

    private static bool StatesEqual(ImmutableDictionary<ILocalSymbol, NullState> a, ImmutableDictionary<ILocalSymbol, NullState> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var bValue) || bValue != kvp.Value)
                return false;
        }
        return true;
    }

    private static ImmutableDictionary<ILocalSymbol, NullState> MergeIncomingStates(
        BasicBlock block,
        Dictionary<BasicBlock, ImmutableDictionary<ILocalSymbol, NullState>> states)
    {
        var result = ImmutableDictionary<ILocalSymbol, NullState>.Empty.WithComparers(SymbolEqualityComparer.Default);

        foreach (var predecessor in block.Predecessors)
        {
            if (predecessor.Source is not null && states.TryGetValue(predecessor.Source, out var predState))
            {
                result = MergeStates(result, predState);
            }
        }

        return result;
    }

    private static ImmutableDictionary<ILocalSymbol, NullState> MergeStates(
        ImmutableDictionary<ILocalSymbol, NullState> a,
        ImmutableDictionary<ILocalSymbol, NullState> b)
    {
        var result = a.ToBuilder();
        result.KeyComparer = SymbolEqualityComparer.Default;

        foreach (var kvp in b)
        {
            if (result.TryGetValue(kvp.Key, out var existing))
            {
                result[kvp.Key] = NullStateLattice.Join(existing, kvp.Value);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result.ToImmutable();
    }

    private static ImmutableDictionary<ILocalSymbol, NullState> TransferBlock(
        BasicBlock block,
        ImmutableDictionary<ILocalSymbol, NullState> state)
    {
        var result = state.ToBuilder();

        foreach (var op in block.Operations)
        {
            TransferOperation(op, result);
        }

        if (block.BranchValue is not null)
        {
            TransferOperation(block.BranchValue, result);
        }

        return result.ToImmutable();
    }

    private static void TransferOperation(IOperation op, ImmutableDictionary<ILocalSymbol, NullState>.Builder state)
    {
        switch (op)
        {
            case IAssignmentOperation assignment:
                if (assignment.Target is ILocalReferenceOperation localRef)
                {
                    var valueState = EvaluateExpression(assignment.Value, state);
                    state[localRef.Local] = valueState;
                }
                break;

            case IExpressionStatementOperation exprStmt:
                if (exprStmt.Operation is not null)
                {
                    TransferOperation(exprStmt.Operation, state);
                }
                break;

            case IVariableDeclarationOperation varDeclOp:
                foreach (var declarator in varDeclOp.Declarators)
                {
                    TransferOperation(declarator, state);
                }
                break;

            case IVariableDeclarationGroupOperation varDeclGroup:
                foreach (var declaration in varDeclGroup.Declarations)
                {
                    TransferOperation(declaration, state);
                }
                break;

            case IVariableDeclaratorOperation varDecl:
                if (varDecl.Initializer is not null && varDecl.Symbol is ILocalSymbol local)
                {
                    var initState = EvaluateExpression(varDecl.Initializer.Value, state);
                    state[local] = initState;
                }
                else if (varDecl.Symbol is ILocalSymbol localNoInit)
                {
                    state[localNoInit] = NullState.Unknown;
                }
                break;
        }
    }

    private static NullState EvaluateExpression(IOperation? op, ImmutableDictionary<ILocalSymbol, NullState>.Builder state)
    {
        if (op is null)
            return NullState.Unknown;

        return op switch
        {
            ILiteralOperation literal => literal.ConstantValue.Value is null
                ? NullState.DefinitelyNull
                : NullState.NotNull,

            ILocalReferenceOperation localRef => state.TryGetValue(localRef.Local, out var localState)
                ? localState
                : NullState.Unknown,

            IConditionalAccessOperation => NullState.MaybeNull,

            IConditionalAccessInstanceOperation => NullState.MaybeNull,

            IMemberReferenceOperation member => EvaluateExpression(member.Instance, state) switch
            {
                NullState.DefinitelyNull => NullState.DefinitelyNull,
                NullState.NotNull => NullState.NotNull,
                NullState.Unknown => NullState.Unknown,
                _ => NullState.MaybeNull,
            },

            IDefaultValueOperation defaultVal => defaultVal.Type?.IsReferenceType == true
                ? NullState.DefinitelyNull
                : NullState.MaybeNull,

            IInvocationOperation => NullState.MaybeNull,

            IObjectCreationOperation => NullState.NotNull,

            IArrayCreationOperation => NullState.NotNull,

            IBinaryOperation binary => NullState.MaybeNull,

            IUnaryOperation unary => EvaluateExpression(unary.Operand, state),

            IConversionOperation conv => EvaluateExpression(conv.Operand, state),

            IParenthesizedOperation paren => EvaluateExpression(paren.Operand, state),

            _ => NullState.Unknown,
        };
    }
}
