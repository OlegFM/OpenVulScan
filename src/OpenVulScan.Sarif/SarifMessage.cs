namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifMessage(
    [property: JsonPropertyName("text")] string Text);
