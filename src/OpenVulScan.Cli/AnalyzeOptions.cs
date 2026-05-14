namespace OpenVulScan;

internal sealed class AnalyzeOptions
{
    public required string Path { get; init; }

    public required string Format { get; init; }

    public required string? Output { get; init; }

    public required IReadOnlyList<string>? Include { get; init; }

    public required IReadOnlyList<string>? Exclude { get; init; }

    public required string? Suppress { get; init; }
}
