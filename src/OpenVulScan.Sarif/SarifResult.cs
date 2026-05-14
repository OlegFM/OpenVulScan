namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifResult(
    [property: JsonPropertyName("ruleId")] string RuleId,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("message")] SarifMessage Message,
    [property: JsonPropertyName("locations")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SarifLocation>? Locations);
