namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifTool(
    [property: JsonPropertyName("driver")] SarifToolDriver Driver);
