using System.Collections;
using System.Linq;
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
        var dataFlowRules = new List<IDataFlowRule>();

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

                if (instance is IDataFlowRule dataFlowRule)
                {
                    dataFlowRules.Add(dataFlowRule);
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

        if (dataFlowRules.Count > 0)
        {
            foreach (var group in dataFlowRules.GroupBy(r => GetDataFlowStateType(r.GetType())))
            {
                if (group.Key is null)
                {
                    continue;
                }

                allDiagnostics.AddRange(RunDataFlowGroup(group.Key, group, compilation, cancellationToken));
            }
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(allDiagnostics);
    }

    private static Type? GetDataFlowStateType(Type ruleType)
    {
        for (var type = ruleType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataFlowRule<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static IReadOnlyList<Diagnostic> RunDataFlowGroup(
        Type stateType,
        IEnumerable<IDataFlowRule> rules,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        // The dispatcher is generic in the lattice state type, which is only
        // known at runtime — close it reflectively, mirroring the reflection
        // already used for AstRule handler discovery.
        var ruleBaseType = typeof(DataFlowRule<>).MakeGenericType(stateType);
        var listType = typeof(List<>).MakeGenericType(ruleBaseType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var rule in rules)
        {
            list.Add(rule);
        }

        var dispatcherType = typeof(DataFlowRuleDispatcher<>).MakeGenericType(stateType);
        try
        {
            var dispatcher = Activator.CreateInstance(dispatcherType, list, compilation)!;
            var run = dispatcherType.GetMethod(nameof(DataFlowRuleDispatcher<object>.Run))!;
            return (IReadOnlyList<Diagnostic>)run.Invoke(dispatcher, new object[] { cancellationToken })!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is OperationCanceledException oce)
        {
            // Cancellation must surface as OperationCanceledException, not TargetInvocationException.
            throw oce;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Re-throw the real inner exception so callers see the actual failure,
            // not the reflective wrapper.  ExceptionDispatchInfo preserves the
            // original stack trace rather than replacing it with this catch site.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // unreachable — satisfies the compiler
        }
    }
}
