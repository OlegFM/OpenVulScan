namespace OpenVulScan;

public sealed record AnalysisFail(
    string Code,
    string Message,
    string? FilePath = null,
    int? Line = null);
