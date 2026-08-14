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
        // Out-states start at ⊥ (standard Kildall seeding). Pre-computing transfer(⊥) here
        // would evaluate blocks against a state that pretends "nothing is known yet" means
        // "every variable is ⊤" (map-miss semantics), and an early join could pick up that
        // garbage before the block's first real visit — under a widening ratchet the
        // contamination then never recovers (found via the interval pipeline, ovs-kmj).
        var outStates = cfg.Blocks.ToDictionary(b => b, _ => _lattice.Bottom);

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

        // Widening points: blocks with a back-edge predecessor (RPO index of the predecessor
        // >= the block's own), i.e. loop headers. Only relevant when the lattice opts into
        // widening — finite-height lattices converge under Join alone.
        var wideningLattice = _lattice as IWideningLattice<T>;
        var rpoIndex = new Dictionary<BasicBlock, int>(rpo.Length);
        for (int i = 0; i < rpo.Length; i++)
        {
            rpoIndex[rpo[i]] = i;
        }

        // Predecessors whose out-state has not been computed yet contribute nothing to a
        // join: their out is ⊥ "because unvisited", not "because empty", and refining that
        // ⊥ would materialize spurious facts (a map-miss reads as ⊤). Tracking computed
        // blocks explicitly keeps first-sweep joins clean — critical under widening, whose
        // ratchet would otherwise lock in the garbage forever (found via ovs-kmj).
        var hasComputedOut = new HashSet<BasicBlock>();
        if (entryBlock is not null)
        {
            hasComputedOut.Add(entryBlock);
        }

        int iterations = 0;
        while (worklist.Count > 0 && iterations < _maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            var block = worklist.Dequeue();
            iterations++;

            var newIn = ComputeInState(block, outStates, entryBlock, initialEntryState, hasComputedOut);
            bool firstVisit = hasComputedOut.Add(block);

            if (wideningLattice is not null && HasBackEdgePredecessor(block, rpoIndex))
            {
                newIn = wideningLattice.Widen(inStates[block], newIn);
            }

            if (!firstVisit && AreEqual(newIn, inStates[block]))
            {
                continue;
            }

            inStates[block] = newIn;
            var newOut = _transfer.Apply(newIn, block);

            bool outChanged = !AreEqual(newOut, outStates[block]);
            outStates[block] = newOut;

            // On the first visit the successors must re-examine this edge even when the
            // out-state stayed ⊥-equal: they may have skipped it as not-yet-computed.
            if ((outChanged || firstVisit) && successors.TryGetValue(block, out var succs))
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

    private static bool HasBackEdgePredecessor(BasicBlock block, Dictionary<BasicBlock, int> rpoIndex)
    {
        foreach (var pred in block.Predecessors)
        {
            if (pred.Source is { } source
                && rpoIndex.TryGetValue(source, out int sourceIndex)
                && rpoIndex.TryGetValue(block, out int blockIndex)
                && sourceIndex >= blockIndex)
            {
                return true;
            }
        }

        return false;
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

    private T ComputeInState(
        BasicBlock block,
        Dictionary<BasicBlock, T> outStates,
        BasicBlock? entryBlock,
        T initialEntryState,
        HashSet<BasicBlock> hasComputedOut)
    {
        if (block == entryBlock)
        {
            return initialEntryState;
        }

        // Edges from predecessors whose out-state is not computed yet are skipped: their
        // out is ⊥-as-unvisited, and refine(⊥) must stay ⊥ — but the refiner cannot tell
        // an unvisited ⊥ map from a computed empty one (a map-miss reads as ⊤), so the
        // solver enforces it here. Every block is visited at least once, so no reachable
        // edge is skipped at fixpoint.
        T state = _lattice.Bottom;
        bool any = false;
        foreach (var pred in block.Predecessors)
        {
            if (pred.Source is not { } source || !hasComputedOut.Contains(source))
            {
                continue;
            }

            var refined = RefineOutState(outStates[source], pred);
            state = any ? _lattice.Join(state, refined) : refined;
            any = true;
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
