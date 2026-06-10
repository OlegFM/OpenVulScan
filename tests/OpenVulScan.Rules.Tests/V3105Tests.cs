using Xunit;

namespace OpenVulScan.Tests;

public class V3105Tests
{
    [Fact]
    public Task DeclaratorFromConditionalAccessThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "declarator_from_conditional_then_deref", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F;
        var n = x.Length;
    }
}");

    [Fact]
    public Task AssignmentFromConditionalAccessThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "assignment_from_conditional_then_deref", @"
class C
{
    string F;
    void M(C a)
    {
        string x;
        x = a?.F;
        var n = x.Length;
    }
}");

    [Fact]
    public Task ConditionalInvocationResultThenInvocation() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "conditional_invocation_result_then_call", @"
class C
{
    string F() => """";
    void M(C a)
    {
        var x = a?.F();
        var n = x.ToString();
    }
}");

    [Fact]
    public Task ParenthesizedConditionalRhsThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "parenthesized_conditional_rhs_then_deref", @"
class C
{
    string F;
    void M(C a)
    {
        var x = (a?.F);
        var n = x.Length;
    }
}");

    [Fact]
    public Task NullCheckedAfterConditionalIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "null_checked_after_conditional_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F;
        if (x != null)
        {
            var n = x.Length;
        }
    }
}");

    [Fact]
    public Task CoalesceFallbackIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "coalesce_fallback_silent", @"
class C
{
    string F;
    void M(C a)
    {
        var x = a?.F ?? ""y"";
        var n = x.Length;
    }
}");

    [Fact]
    public Task PlainAssignmentDoesNotFireV3105() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3105", "plain_null_assignment_not_v3105", @"
class C
{
    void M()
    {
        string x = null;
        var n = x.Length;
    }
}");
}
