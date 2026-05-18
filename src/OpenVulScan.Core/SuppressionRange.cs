namespace OpenVulScan;

public sealed record SuppressionRange(
    string FilePath,
    int StartLine,
    int EndLine,
    IReadOnlySet<string> RuleCodes);
