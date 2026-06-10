namespace OpenVulScan.Tests;

public class V3027UseBeforeNullCheckTests
{
    [Fact]
    public Task AndChainMemberAccessBeforeNotNullFlags()
    {
        const string source = "class C { void M(int[] a) { var r = a.Length > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "AndChain_MemberAccessBeforeNotNull_Flags", source);
    }
}
