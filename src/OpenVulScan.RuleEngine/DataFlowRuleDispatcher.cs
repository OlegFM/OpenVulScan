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

                foreach (var rule in _rules)
                {
                    var solver = new WorklistSolver<TLattice>(rule.Lattice, rule.Transfer);
                    var result = solver.Solve(cfg, cancellationToken);

                    foreach (var block in cfg.Blocks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var state = result.InStates[block];

                        foreach (var op in GetAllOperations(block))
                        {
                            var context = new DataFlowContext(op, model, _compilation, cancellationToken);
                            rule.InvokeOnState(op, state, context);
                            diagnostics.AddRange(context.Diagnostics);
                            state = rule.Transfer.Apply(state, op);
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
