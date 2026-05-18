using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace OpenVulScan;

internal sealed class RulesListCommandHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> ExecuteAsync(string format, bool enabledOnly, Stream outputStream)
    {
        try
        {
            var registry = CreateRuleRegistry();
            var rules = registry.GetAll();

            if (enabledOnly)
            {
                // Currently all rules are considered enabled; flag is reserved for future use.
                // No filtering applied.
            }

            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(rules, outputStream).ConfigureAwait(false);
            }
            else
            {
                await WriteTextAsync(rules, outputStream).ConfigureAwait(false);
            }

            return 0;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            using var writer = new StreamWriter(outputStream, leaveOpen: true);
            await writer.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            return 2;
        }
#pragma warning restore CA1031
    }

    private static RuleRegistry CreateRuleRegistry()
    {
        var registry = new RuleRegistry();
        var baseDir = AppContext.BaseDirectory;
        var ruleDlls = Directory.GetFiles(baseDir, "OpenVulScan.Rules.*.dll");

        foreach (var dll in ruleDlls)
        {
#pragma warning disable CA1031
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                registry.Scan(assembly);
            }
            catch
            {
                // Ignore unloadable assemblies
            }
#pragma warning restore CA1031
        }

        return registry;
    }

    private static async Task WriteJsonAsync(IReadOnlyList<RuleDescriptor> rules, Stream outputStream)
    {
        var data = rules.Select(r => new
        {
            r.Code,
            Level = r.DefaultLevel.ToString(),
            Category = r.Category.ToString(),
            r.Cwe,
            Capabilities = r.Capabilities.ToString(),
        }).ToList();

        await JsonSerializer.SerializeAsync(outputStream, data, s_jsonOptions).ConfigureAwait(false);
    }

    private static async Task WriteTextAsync(IReadOnlyList<RuleDescriptor> rules, Stream outputStream)
    {
        using var writer = new StreamWriter(outputStream, leaveOpen: true);

        const string codeHeader = "Code";
        const string levelHeader = "Level";
        const string categoryHeader = "Category";
        const string cweHeader = "Cwe";
        const string capabilitiesHeader = "Capabilities";

        var codeWidth = Math.Max(codeHeader.Length, rules.Count > 0 ? rules.Max(r => r.Code.Length) : 0);
        var levelWidth = Math.Max(levelHeader.Length, rules.Count > 0 ? rules.Max(r => r.DefaultLevel.ToString().Length) : 0);
        var categoryWidth = Math.Max(categoryHeader.Length, rules.Count > 0 ? rules.Max(r => r.Category.ToString().Length) : 0);
        var cweWidth = Math.Max(cweHeader.Length, rules.Count > 0 ? rules.Max(r => r.Cwe.Length) : 0);
        var capabilitiesWidth = Math.Max(capabilitiesHeader.Length, rules.Count > 0 ? rules.Max(r => r.Capabilities.ToString().Length) : 0);

        // Header
        await writer.WriteLineAsync(
            $"{codeHeader.PadRight(codeWidth)}  {levelHeader.PadRight(levelWidth)}  {categoryHeader.PadRight(categoryWidth)}  {cweHeader.PadRight(cweWidth)}  {capabilitiesHeader.PadRight(capabilitiesWidth)}")
            .ConfigureAwait(false);

        await writer.WriteLineAsync(
            $"{new string('-', codeWidth)}  {new string('-', levelWidth)}  {new string('-', categoryWidth)}  {new string('-', cweWidth)}  {new string('-', capabilitiesWidth)}")
            .ConfigureAwait(false);

        // Rows
        foreach (var rule in rules)
        {
            await writer.WriteLineAsync(
                $"{rule.Code.PadRight(codeWidth)}  {rule.DefaultLevel.ToString().PadRight(levelWidth)}  {rule.Category.ToString().PadRight(categoryWidth)}  {rule.Cwe.PadRight(cweWidth)}  {rule.Capabilities.ToString().PadRight(capabilitiesWidth)}")
                .ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }
}
