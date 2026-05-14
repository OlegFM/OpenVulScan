namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifPhysicalLocation(
    [property: JsonPropertyName("artifactLocation")] SarifArtifactLocation ArtifactLocation,
    [property: JsonPropertyName("region")] SarifRegion Region);
