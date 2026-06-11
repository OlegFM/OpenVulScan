using Xunit;

namespace OpenVulScan.Tests;

public class V3080Tests
{
    [Fact]
    public Task NullLiteralThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_literal_then_deref", @"
class C
{
    void M()
    {
        string s = null;
        var n = s.Length;
    }
}");

    [Fact]
    public Task NullOnOneBranchThenDerefAfterJoin() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_on_one_branch_join", @"
class C
{
    void M(bool c)
    {
        string s = ""x"";
        if (c) { s = null; }
        var n = s.Length;
    }
}");

    [Fact]
    public Task NullAssignedThenInvocation() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_then_invocation", @"
class C
{
    void M()
    {
        string s = null;
        var t = s.ToString();
    }
}");

    [Fact]
    public Task NullArrayElementAccess() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_array_element_access", @"
class C
{
    void M()
    {
        int[] a = null;
        var n = a[0];
    }
}");

    [Fact]
    public Task NullFieldThenDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "null_field_then_deref", @"
class C
{
    string f;
    void M()
    {
        this.f = null;
        var n = this.f.Length;
    }
}");

    [Fact]
    public Task GuardedDerefIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "guarded_deref_silent", @"
class C
{
    void M(bool c)
    {
        string s = null;
        if (c) { s = ""x""; }
        if (s != null)
        {
            var n = s.Length;
        }
    }
}");

    [Fact]
    public Task IsNotNullGuardIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "is_not_null_guard_silent", @"
class C
{
    void M(bool c)
    {
        string s = null;
        if (c) { s = ""x""; }
        if (s is not null)
        {
            var n = s.Length;
        }
    }
}");

    [Fact]
    public Task ReassignedBeforeDerefIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "reassigned_before_deref_silent", @"
class C
{
    void M()
    {
        string s = null;
        s = ""x"";
        var n = s.Length;
    }
}");

    [Fact]
    public Task UncheckedParameterIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "unchecked_parameter_silent", @"
class C
{
    void M(string p)
    {
        var n = p.Length;
    }
}");

    [Fact]
    public Task EarlyReturnGuardIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "early_return_guard_silent", @"
class C
{
    void M(bool c)
    {
        string s = null;
        if (c) { s = ""x""; }
        if (s == null) { return; }
        var n = s.Length;
    }
}");

    // Capture conservativeness (ovs-tr6): a flow capture whose arms carry
    // different values must join to Unknown at the consumer, never to a
    // definite state that would produce a false positive.

    [Fact]
    public Task CoalesceOfUnknownArmsIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "coalesce_unknown_arms_silent", @"
class C
{
    void M(string a, string b)
    {
        var s = a ?? b;
        var n = s.Length;
    }
}");

    [Fact]
    public Task TernaryOfUnknownArmsIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080", "ternary_unknown_arms_silent", @"
class C
{
    void M(bool c, string a, string b)
    {
        var s = c ? a : b;
        var n = s.Length;
    }
}");
}
