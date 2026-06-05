using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public sealed class DataFlowContext
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IOperation Operation { get; }

    public SemanticModel SemanticModel { get; }

    public Compilation Compilation { get; }

    public SsaIndex SsaIndex { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public DataFlowContext(IOperation operation, SemanticModel semanticModel, Compilation compilation, SsaIndex ssaIndex, CancellationToken cancellationToken)
    {
        Operation = operation;
        SemanticModel = semanticModel;
        Compilation = compilation;
        SsaIndex = ssaIndex;
        CancellationToken = cancellationToken;
    }

    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }
}
