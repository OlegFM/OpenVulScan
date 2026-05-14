using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public sealed class SyntaxNodeContext
{
    private readonly List<Diagnostic> _diagnostics = new();

    public SyntaxNode Node { get; }

    public SemanticModel SemanticModel { get; }

    public Compilation Compilation { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public SyntaxNodeContext(SyntaxNode node, SemanticModel semanticModel, Compilation compilation, CancellationToken cancellationToken)
    {
        Node = node;
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
