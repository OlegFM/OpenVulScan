namespace OpenVulScan;

public sealed record RuleDescriptor(
    string Code,
    RuleSeverity DefaultLevel,
    string Cwe,
    RuleCategory Category,
    AnalysisCapability Capabilities,
    Type RuleType);
