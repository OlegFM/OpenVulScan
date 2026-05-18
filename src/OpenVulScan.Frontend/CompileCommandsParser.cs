using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenVulScan;

internal static class CompileCommandsParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CompileCommands? Parse(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<CompileCommandsRoot>(json, JsonOptions);
            if (root?.Compilation is null)
            {
                return null;
            }

            return new CompileCommands(
                root.Compilation.AssemblyName ?? Path.GetFileNameWithoutExtension(path),
                root.Compilation.Sources ?? new List<string>(),
                root.Compilation.References ?? new List<string>(),
                root.Compilation.PreprocessorSymbols ?? new List<string>());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class CompileCommandsRoot
    {
        [JsonPropertyName("compilation")]
        public CompilationEntry? Compilation { get; set; }
    }

    private sealed class CompilationEntry
    {
        [JsonPropertyName("assemblyName")]
        public string? AssemblyName { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("sources")]
        public List<string>? Sources { get; set; }

        [JsonPropertyName("references")]
        public List<string>? References { get; set; }

        [JsonPropertyName("preprocessorSymbols")]
        public List<string>? PreprocessorSymbols { get; set; }
    }
}

internal sealed record CompileCommands(
    string AssemblyName,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> References,
    IReadOnlyList<string> PreprocessorSymbols);
