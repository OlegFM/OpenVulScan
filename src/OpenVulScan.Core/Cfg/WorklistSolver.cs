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

        // Entry block starts with the caller-supplied initial state
        var entryBlock = cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Entry);
        if (entryBlock is not null)
        {
            inStates = inStates.SetItem(entryBlock, initialEntryState);
            outStates = outStates.SetItem(entryBlock, _transfer.Apply(initialEntryState, entryBlock));
        }

        var worklist = new Queue<BasicBlock>();
        foreach (var block in cfg.Blocks)
        {
            worklist.Enqueue(block);
        }

        int iterations = 0;
        while (worklist.Count > 0 && iterations < _maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            var block = worklist.Dequeue();

            var newIn = ComputeInState(block, outStates);
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

            // Enqueue all successors
            EnqueueSuccessor(block.FallThroughSuccessor, worklist);
            EnqueueSuccessor(block.ConditionalSuccessor, worklist);

            iterations++;
        }

        return new WorklistSolverResult<T>(inStates, converged: worklist.Count == 0);
    }

    private T ComputeInState(BasicBlock block, ImmutableDictionary<BasicBlock, T> outStates)
    {
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
}
