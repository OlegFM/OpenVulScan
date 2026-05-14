namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifLocation(
    [property: JsonPropertyName("physicalLocation")] SarifPhysicalLocation PhysicalLocation);
