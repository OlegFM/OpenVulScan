namespace OpenVulScan;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RuleAttribute : Attribute
{
    public string Code { get; }

    public RuleSeverity DefaultLevel { get; }

    public string Cwe { get; }

    public RuleCategory Category { get; }

    public AnalysisCapability Capabilities { get; }

    public RuleAttribute(string code, RuleSeverity defaultLevel, string cwe, RuleCategory category, AnalysisCapability capabilities)
    {
        Code = code;
        DefaultLevel = defaultLevel;
        Cwe = cwe;
        Category = category;
        Capabilities = capabilities;
    }
}
