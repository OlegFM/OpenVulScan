#pragma warning disable CA1056 // Uri properties should be Uri type
#pragma warning disable CA1054 // Uri parameters should not be strings

namespace OpenVulScan;

using System.Text.Json.Serialization;

public sealed record SarifArtifactLocation(
    [property: JsonPropertyName("uri")] string Uri);

#pragma warning restore CA1054
#pragma warning restore CA1056
