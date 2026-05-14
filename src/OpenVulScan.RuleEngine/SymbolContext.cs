using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public sealed class SymbolContext
{
    private readonly List<Diagnostic> _diagnostics = new();

    public ISymbol Symbol { get; }

    public SemanticModel SemanticModel { get; }

    public Compilation Compilation { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public SymbolContext(ISymbol symbol, SemanticModel semanticModel, Compilation compilation, CancellationToken cancellationToken)
    {
        Symbol = symbol;
        SemanticModel = semanticModel;
        Compilation = compilation;
        CancellationToken = cancellationToken;
    }

    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }
}
