#pragma warning disable CA1812 // Avoid uninstantiated internal classes - these are discovered via reflection
#pragma warning disable CA1852 // Seal internal types

namespace OpenVulScan.Tests;

[Rule("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
internal class TestRuleGeneralAnalysis
{
}

[Rule("V3002", RuleSeverity.Level2, "CWE-570", RuleCategory.Owasp, AnalysisCapability.DataFlow | AnalysisCapability.PathSensitive)]
internal class TestRuleOwasp
{
}

[Rule("V3003", RuleSeverity.Level3, "CWE-572", RuleCategory.Performance, AnalysisCapability.Symbol)]
internal class TestRulePerformance
{
}

[Rule("V3004", RuleSeverity.Level0, "CWE-573", RuleCategory.Unity, AnalysisCapability.Taint)]
internal class TestRuleUnity
{
}

[Rule("V3005", RuleSeverity.Level1, "CWE-574", RuleCategory.Fail, AnalysisCapability.Hierarchy)]
internal class TestRuleFail
{
}
