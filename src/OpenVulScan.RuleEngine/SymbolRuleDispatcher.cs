using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public sealed class SymbolRuleDispatcher
{
    private readonly IReadOnlyList<SymbolRule> _rules;
    private readonly Compilation _compilation;

    public SymbolRuleDispatcher(IEnumerable<SymbolRule> rules, Compilation compilation)
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
        WalkNamespace(_compilation.GlobalNamespace, diagnostics, cancellationToken);
        return diagnostics;
    }

    private void WalkNamespace(INamespaceSymbol namespaceSymbol, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            DispatchSymbol(member, diagnostics, cancellationToken);

            if (member is INamedTypeSymbol namedType)
            {
                WalkTypeMembers(namedType, diagnostics, cancellationToken);
            }

            if (member is INamespaceSymbol nestedNamespace)
            {
                WalkNamespace(nestedNamespace, diagnostics, cancellationToken);
            }
        }
    }

    private void WalkTypeMembers(INamedTypeSymbol typeSymbol, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            DispatchSymbol(member, diagnostics, cancellationToken);

            if (member is INamedTypeSymbol nestedType)
            {
                WalkTypeMembers(nestedType, diagnostics, cancellationToken);
            }
        }
    }

    private void DispatchSymbol(ISymbol symbol, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (!symbol.Locations.Any(l => l.IsInSource))
        {
            return;
        }

        var semanticModel = GetSemanticModel(symbol);
        if (semanticModel is null)
        {
            return;
        }

        var context = new SymbolContext(symbol, semanticModel, _compilation, cancellationToken);

        foreach (var rule in _rules)
        {
            if (rule.SupportedSymbolKinds.Contains(symbol.Kind))
            {
                rule.Visit(symbol, context);
            }
        }

        diagnostics.AddRange(context.Diagnostics);
    }

    private SemanticModel? GetSemanticModel(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.SourceTree is not null);
        if (location?.SourceTree is not null)
        {
            return _compilation.GetSemanticModel(location.SourceTree);
        }

        // Fallback for metadata symbols: use the first available syntax tree.
        var firstTree = _compilation.SyntaxTrees.FirstOrDefault();
        return firstTree is not null ? _compilation.GetSemanticModel(firstTree) : null;
    }
}
