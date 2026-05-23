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
    private readonly IEdgeRefiner<T>? _edgeRefiner;
    private readonly int _maxIterations;

    /// <summary>
    /// Creates a worklist solver for the given lattice and transfer function.
    /// </summary>
    /// <param name="lattice">The lattice that defines Bottom, Top, Join, and order.</param>
    /// <param name="transfer">The transfer function that maps IN state to OUT state per block.</param>
    /// <param name="edgeRefiner">
    /// Optional edge refiner for path-sensitive analysis. When provided, the solver
    /// refines predecessor out-states for each control-flow edge before joining.
    /// </param>
    /// <param name="maxIterations">
    /// Maximum number of individual block visits (worklist pops) before graceful exit.
    /// Default is 100_000. This counts individual block visits, not full rounds over the CFG.
    /// </param>
    public WorklistSolver(ILattice<T> lattice, ITransfer<T> transfer, IEdgeRefiner<T>? edgeRefiner = null, int maxIterations = 100_000)
    {
        _lattice = lattice ?? throw new ArgumentNullException(nameof(lattice));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _edgeRefiner = edgeRefiner;
        _maxIterations = maxIterations >= 0 ? maxIterations : throw new ArgumentOutOfRangeException(nameof(maxIterations));
    }

    public WorklistSolverResult<T> Solve(ControlFlowGraph cfg, CancellationToken ct = default)
        => Solve(cfg, _lattice.Bottom, ct);

    public WorklistSolverResult<T> Solve(ControlFlowGraph cfg, T initialEntryState, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var inStates = cfg.Blocks.ToDictionary(b => b, _ => _lattice.Bottom);
        var outStates = cfg.Blocks.ToDictionary(b => b, b => _transfer.Apply(_lattice.Bottom, b));

        var entryBlock = cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Entry);
        if (entryBlock is not null)
        {
            inStates[entryBlock] = initialEntryState;
            outStates[entryBlock] = _transfer.Apply(initialEntryState, entryBlock);
        }

        var successors = BuildSuccessorMap(cfg);
        var rpo = ComputeReversePostOrder(cfg, successors);
        var worklist = new Queue<BasicBlock>();
        foreach (var block in rpo)
        {
            worklist.Enqueue(block);
        }

        int iterations = 0;
        while (worklist.Count > 0 && iterations < _maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            var block = worklist.Dequeue();
            iterations++;

            var newIn = ComputeInState(block, outStates, entryBlock, initialEntryState);
            if (AreEqual(newIn, inStates[block]))
            {
                continue;
            }

            inStates[block] = newIn;
            var newOut = _transfer.Apply(newIn, block);

            if (AreEqual(newOut, outStates[block]))
            {
                continue;
            }

            outStates[block] = newOut;

            if (successors.TryGetValue(block, out var succs))
            {
                foreach (var succ in succs)
                {
                    worklist.Enqueue(succ);
                }
            }
        }

        return new WorklistSolverResult<T>(
            ImmutableDictionary.CreateRange(inStates),
            ImmutableDictionary.CreateRange(outStates),
            converged: worklist.Count == 0);
    }

    private static Dictionary<BasicBlock, List<BasicBlock>> BuildSuccessorMap(ControlFlowGraph cfg)
    {
        var successors = new Dictionary<BasicBlock, List<BasicBlock>>();
        foreach (var block in cfg.Blocks)
        {
            foreach (var pred in block.Predecessors)
            {
                if (pred.Source is null)
                {
                    continue;
                }

                if (!successors.TryGetValue(pred.Source, out var list))
                {
                    list = new List<BasicBlock>();
                    successors[pred.Source] = list;
                }

                list.Add(block);
            }
        }

        return successors;
    }

    private T ComputeInState(BasicBlock block, Dictionary<BasicBlock, T> outStates, BasicBlock? entryBlock, T initialEntryState)
    {
        if (block == entryBlock)
        {
            return initialEntryState;
        }

        var preds = block.Predecessors;
        if (preds.IsEmpty)
        {
            return _lattice.Bottom;
        }

        var state = RefineOutState(outStates[preds[0].Source], preds[0]);
        for (int i = 1; i < preds.Length; i++)
        {
            state = _lattice.Join(state, RefineOutState(outStates[preds[i].Source], preds[i]));
        }

        return state;
    }

    private T RefineOutState(T outState, ControlFlowBranch branch)
    {
        if (_edgeRefiner is null)
        {
            return outState;
        }

        return _edgeRefiner.Refine(outState, branch);
    }

    private bool AreEqual(T left, T right)
        => _lattice.LessOrEqual(left, right) && _lattice.LessOrEqual(right, left);

    private static ImmutableArray<BasicBlock> ComputeReversePostOrder(
        ControlFlowGraph cfg,
        Dictionary<BasicBlock, List<BasicBlock>> successors)
    {
        var visited = new HashSet<BasicBlock>();
        var postOrder = new List<BasicBlock>();

        void Dfs(BasicBlock block)
        {
            if (!visited.Add(block))
            {
                return;
            }

            if (successors.TryGetValue(block, out var succs))
            {
                foreach (var succ in succs)
                {
                    Dfs(succ);
                }
            }

            postOrder.Add(block);
        }

        Dfs(cfg.Blocks.First());

        foreach (var block in cfg.Blocks)
        {
            if (!visited.Contains(block))
            {
                postOrder.Add(block);
            }
        }

        postOrder.Reverse();
        return [.. postOrder];
    }
}
