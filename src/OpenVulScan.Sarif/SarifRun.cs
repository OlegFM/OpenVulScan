namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifRun(
    [property: JsonPropertyName("tool")] SarifTool Tool,
    [property: JsonPropertyName("results")] IReadOnlyList<SarifResult> Results);
