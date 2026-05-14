namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifLog(
    [property: JsonPropertyName("$schema")] string Schema,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("runs")] IReadOnlyList<SarifRun> Runs);
