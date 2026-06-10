using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public sealed class DataFlowRuleDispatcher<TLattice>
{
    private readonly IReadOnlyList<DataFlowRule<TLattice>> _rules;
    private readonly Compilation _compilation;

    public DataFlowRuleDispatcher(IEnumerable<DataFlowRule<TLattice>> rules, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(compilation);

        _rules = rules.ToList();
        _compilation = compilation;
    }

    public IReadOnlyList<Diagnostic> Run(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<Diagnostic>();

        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = _compilation.GetSemanticModel(tree);

            foreach (var method in tree.GetRoot(cancellationToken).DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = model.GetOperation(method, cancellationToken);
                if (operation is not IMethodBodyOperation methodBody)
                {
                    continue;
                }

                var cfg = ControlFlowGraph.Create(methodBody, cancellationToken);
                var ssaIndex = SsaBuilder.Build(cfg, model);

                var entries = _rules
                    .Select(rule => (
                        Rule: rule,
                        Transfer: rule.CreateTransfer(ssaIndex),
                        Refiner: rule.CreateEdgeRefiner(ssaIndex)))
                    .ToList();

                var groups = entries.GroupBy(e => (
                    LatticeType: e.Rule.Lattice.GetType(),
                    TransferType: e.Transfer.GetType(),
                    RefinerType: e.Refiner?.GetType()));

                foreach (var group in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Transfers and refiners are stateless apart from the SsaIndex they
                    // were built over (same per method), so type equality implies
                    // identical behaviour and one solve serves the whole group.
                    var first = group.First();
                    var solver = new WorklistSolver<TLattice>(first.Rule.Lattice, first.Transfer, first.Refiner);
                    var result = solver.Solve(cfg, cancellationToken);

                    foreach (var block in cfg.Blocks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var state = result.InStates[block];
                        state = first.Transfer.ApplyPhis(state, block);

                        foreach (var op in GetAllOperations(block))
                        {
                            foreach (var entry in group)
                            {
                                var context = new DataFlowContext(op, model, _compilation, ssaIndex, cancellationToken);
                                entry.Rule.InvokeOnState(op, state, context);
                                diagnostics.AddRange(context.Diagnostics);
                            }

                            state = first.Transfer.Apply(state, op);
                        }
                    }
                }
            }
        }

        return diagnostics;
    }

    private static IEnumerable<IOperation> GetAllOperations(BasicBlock block)
    {
        foreach (var operation in block.Operations)
        {
            foreach (var descendant in GetDescendantsAndSelf(operation))
            {
                yield return descendant;
            }
        }

        if (block.BranchValue is not null)
        {
            foreach (var descendant in GetDescendantsAndSelf(block.BranchValue))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<IOperation> GetDescendantsAndSelf(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            if (child is not null)
            {
                foreach (var descendant in GetDescendantsAndSelf(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
