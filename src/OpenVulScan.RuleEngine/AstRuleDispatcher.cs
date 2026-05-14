using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace OpenVulScan;

public sealed class AstRuleDispatcher
{
    private readonly IReadOnlyList<AstRule> _rules;
    private readonly Compilation _compilation;

    public AstRuleDispatcher(IEnumerable<AstRule> rules, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(compilation);

        _rules = rules.ToList();
        _compilation = compilation;
    }

    public void Run(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = _compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            foreach (var node in root.DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var kind = node.Kind();
                var context = new SyntaxNodeContext(node, model, _compilation, cancellationToken);

                foreach (var rule in _rules)
                {
                    if (rule.SupportedSyntaxKinds.Contains(kind))
                    {
                        rule.Visit(node, context);
                    }
                }
            }
        }
    }
}
