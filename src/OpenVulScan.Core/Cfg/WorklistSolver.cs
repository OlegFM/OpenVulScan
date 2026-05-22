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
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var rpo = ComputeReversePostOrder(cfg);

        var inStates = cfg.Blocks.ToImmutableDictionary(b => b, _ => _lattice.Bottom);
        var outStates = cfg.Blocks.ToImmutableDictionary(b => b, _ => _lattice.Bottom);

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

                if (!_lattice.LessOrEqual(outState, outStates[block]))
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
