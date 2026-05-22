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
        _maxIterations = maxIterations >= 0 ? maxIterations : throw new ArgumentOutOfRangeException(nameof(maxIterations));
    }

    public WorklistSolverResult<T> Solve(ControlFlowGraph cfg, CancellationToken ct = default)
        => Solve(cfg, _lattice.Bottom, ct);

    public WorklistSolverResult<T> Solve(ControlFlowGraph cfg, T initialEntryState, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var inStates = cfg.Blocks.ToImmutableDictionary(b => b, _ => _lattice.Bottom);
        var outStates = cfg.Blocks.ToImmutableDictionary(b => b, _ => _lattice.Bottom);

        var entryBlock = cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Entry);
        if (entryBlock is not null)
        {
            inStates = inStates.SetItem(entryBlock, initialEntryState);
            outStates = outStates.SetItem(entryBlock, _transfer.Apply(initialEntryState, entryBlock));
        }

        var rpo = ComputeReversePostOrder(cfg);
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

            inStates = inStates.SetItem(block, newIn);
            var newOut = _transfer.Apply(newIn, block);

            if (AreEqual(newOut, outStates[block]))
            {
                continue;
            }

            outStates = outStates.SetItem(block, newOut);

            EnqueueSuccessor(block.FallThroughSuccessor, worklist);
            EnqueueSuccessor(block.ConditionalSuccessor, worklist);
        }

        return new WorklistSolverResult<T>(inStates, converged: worklist.Count == 0);
    }

    private T ComputeInState(BasicBlock block, ImmutableDictionary<BasicBlock, T> outStates, BasicBlock? entryBlock, T initialEntryState)
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

        var state = outStates[preds[0].Source];
        for (int i = 1; i < preds.Length; i++)
        {
            state = _lattice.Join(state, outStates[preds[i].Source]);
        }

        return state;
    }

    private bool AreEqual(T left, T right)
        => _lattice.LessOrEqual(left, right) && _lattice.LessOrEqual(right, left);

    private static void EnqueueSuccessor(ControlFlowBranch? branch, Queue<BasicBlock> worklist)
    {
        if (branch?.Destination is not null)
        {
            worklist.Enqueue(branch.Destination);
        }
    }

    private static ImmutableArray<BasicBlock> ComputeReversePostOrder(ControlFlowGraph cfg)
    {
        var visited = new HashSet<BasicBlock>();
        var postOrder = new List<BasicBlock>();

        void Dfs(BasicBlock block)
        {
            if (!visited.Add(block))
            {
                return;
            }

            if (block.FallThroughSuccessor?.Destination is not null)
            {
                Dfs(block.FallThroughSuccessor.Destination);
            }

            if (block.ConditionalSuccessor?.Destination is not null)
            {
                Dfs(block.ConditionalSuccessor.Destination);
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
