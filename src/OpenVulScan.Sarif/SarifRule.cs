namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifRule(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("shortDescription")] SarifMessage ShortDescription,
    [property: JsonPropertyName("properties")] Dictionary<string, string> Properties);
