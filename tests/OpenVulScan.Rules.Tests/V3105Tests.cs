namespace OpenVulScan.Tests;

public class V3105Tests
{
    [Fact]
    public Task SimpleConditionalAccessFollowedByMemberAccess()
    {
        const string source = "class C { void M() { var x = obj?.A.B; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "SimpleConditionalAccessFollowedByMemberAccess", source);
    }

    [Fact]
    public Task ConditionalAccessFollowedByTwoMemberAccesses()
    {
        const string source = "class C { void M() { var x = obj?.A.B.C; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "ConditionalAccessFollowedByTwoMemberAccesses", source);
    }

    [Fact]
    public Task NestedConditionalAccessFollowedByMemberAccess()
    {
        const string source = "class C { void M() { var x = obj?.A?.B.C; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "NestedConditionalAccessFollowedByMemberAccess", source);
    }

    [Fact]
    public Task ParenthesizedConditionalAccessFollowedByMemberAccess()
    {
        const string source = "class C { void M() { var x = (obj?.A).B; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "ParenthesizedConditionalAccessFollowedByMemberAccess", source);
    }

    [Fact]
    public Task ConditionalAccessFollowedByMethodCall()
    {
        const string source = "class C { void M() { var x = obj?.A.B.ToString(); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "ConditionalAccessFollowedByMethodCall", source);
    }

    [Fact]
    public Task SafeSingleConditionalAccess()
    {
        const string source = "class C { void M() { var x = obj?.A; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "SafeSingleConditionalAccess", source);
    }

    [Fact]
    public Task SafeNestedConditionalAccess()
    {
        const string source = "class C { void M() { var x = obj?.A?.B; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "SafeNestedConditionalAccess", source);
    }

    [Fact]
    public Task NonConditionalMemberAccessChain()
    {
        const string source = "class C { void M() { var x = obj.A.B; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "NonConditionalMemberAccessChain", source);
    }

    [Fact]
    public Task ConditionalAccessWithElementAccess()
    {
        const string source = "class C { void M() { var x = obj?.A[0]; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "ConditionalAccessWithElementAccess", source);
    }

    [Fact]
    public Task ConditionalAccessWithMethodInvocation()
    {
        const string source = "class C { void M() { var x = obj?.A.ToString(); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3105", "ConditionalAccessWithMethodInvocation", source);
    }

}
