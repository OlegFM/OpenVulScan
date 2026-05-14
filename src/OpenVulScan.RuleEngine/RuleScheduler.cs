using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public sealed class RuleScheduler
{
    private readonly RuleRegistry _registry;
    private readonly Action<string>? _logWarning;

    public RuleScheduler(RuleRegistry registry, Action<string>? logWarning = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _logWarning = logWarning;
    }

    public Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(Compilation compilation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        cancellationToken.ThrowIfCancellationRequested();

        var descriptors = _registry.GetAll();
        var astRules = new List<AstRule>();
        var symbolRules = new List<SymbolRule>();

        foreach (var descriptor in descriptors)
        {
            try
            {
                var instance = Activator.CreateInstance(descriptor.RuleType);
                if (instance is AstRule astRule)
                {
                    astRules.Add(astRule);
                }

                if (instance is SymbolRule symbolRule)
                {
                    symbolRules.Add(symbolRule);
                }
            }
#pragma warning disable CA1031 // A single broken rule must not crash the entire analysis.
            catch (Exception ex)
            {
                _logWarning?.Invoke($"Failed to instantiate rule '{descriptor.Code}': {ex.Message}");
            }
#pragma warning restore CA1031
        }

        var allDiagnostics = new List<Diagnostic>();

        if (astRules.Count > 0)
        {
            var astDispatcher = new AstRuleDispatcher(astRules, compilation);
            allDiagnostics.AddRange(astDispatcher.Run(cancellationToken));
        }

        if (symbolRules.Count > 0)
        {
            var symbolDispatcher = new SymbolRuleDispatcher(symbolRules, compilation);
            allDiagnostics.AddRange(symbolDispatcher.Run(cancellationToken));
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(allDiagnostics);
    }
}
