using System.Collections.Concurrent;
using System.Reflection;

namespace OpenVulScan;

public sealed class RuleRegistry
{
    private readonly ConcurrentDictionary<string, RuleDescriptor> _rules = new(StringComparer.Ordinal);

    public void Scan(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var ruleTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<RuleAttribute>() != null);

        foreach (var type in ruleTypes)
        {
            var attr = type.GetCustomAttribute<RuleAttribute>()!;
            var descriptor = new RuleDescriptor(
                attr.Code,
                attr.DefaultLevel,
                attr.Cwe,
                attr.Category,
                attr.Capabilities,
                type);

            if (!_rules.TryAdd(descriptor.Code, descriptor))
            {
                throw new InvalidOperationException(
                    $"Rule with code '{descriptor.Code}' is already registered. " +
                    $"Duplicate found in type '{type.FullName}'.");
            }
        }
    }

    public void Scan(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            Scan(assembly);
        }
    }

    public IReadOnlyList<RuleDescriptor> GetAll() => _rules.Values.ToList();

    public RuleDescriptor? GetByCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return _rules.TryGetValue(code, out var descriptor) ? descriptor : null;
    }

    public IReadOnlyList<RuleDescriptor> GetByCategory(RuleCategory category) =>
        _rules.Values.Where(r => r.Category == category).ToList();
}
