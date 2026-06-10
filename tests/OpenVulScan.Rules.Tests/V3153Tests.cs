using Xunit;

namespace OpenVulScan.Tests;

public class V3153Tests
{
    [Fact]
    public Task MemberAccessOnParenthesizedConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "member_access_on_parenthesized_conditional", @"
class C
{
    string F;
    void M(C a)
    {
        var n = (a?.F).Length;
    }
}");

    [Fact]
    public Task InvocationOnParenthesizedConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "invocation_on_parenthesized_conditional", @"
class C
{
    string F() => """";
    void M(C a)
    {
        var n = (a?.F()).ToString();
    }
}");

    [Fact]
    public Task ElementAccessOnParenthesizedConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "element_access_on_parenthesized_conditional", @"
class C
{
    int[] F;
    void M(C a)
    {
        var n = (a?.F)[0];
    }
}");

    [Fact]
    public Task NestedConditionalChainDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "nested_conditional_chain_deref", @"
class C
{
    C Next;
    string F;
    void M(C a)
    {
        var n = (a?.Next?.F).Length;
    }
}");

    [Fact]
    public Task ChainedConditionalAccessIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "chained_conditional_access_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var n = a?.F.Length;
    }
}");

    [Fact]
    public Task CoalesceGuardedConditionalIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "coalesce_guarded_conditional_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var n = (a?.F ?? ""y"").Length;
    }
}");

    [Fact]
    public Task VariableDerefDoesNotFireV3153() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3153", "variable_deref_not_v3153", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F;
        var n = x.Length;
    }
}");
}
