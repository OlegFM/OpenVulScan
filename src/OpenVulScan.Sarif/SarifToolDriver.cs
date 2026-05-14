namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifToolDriver(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("rules")] IReadOnlyList<SarifRule> Rules);
