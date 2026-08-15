using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// Per-run compilation context handed to <see cref="DataFlowRule{TLattice}"/> factories.
/// Carries lazily computed compilation-wide artifacts (call graph products, summaries) so
/// they are built at most once per analysis run and only when some rule asks for them.
/// </summary>
public sealed class AnalysisSession
{
    private readonly Compilation _compilation;
    private readonly CancellationToken _cancellationToken;
    private readonly Lock _gate = new();
    private INullabilitySummaryLookup? _nullabilitySummaries;

    public AnalysisSession(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        _compilation = compilation;
        _cancellationToken = cancellationToken;
    }

    public INullabilitySummaryLookup GetNullabilitySummaries()
    {
        lock (_gate)
        {
            return _nullabilitySummaries ??= NullabilitySummaryProvider.Compute(_compilation, _cancellationToken);
        }
    }
}
