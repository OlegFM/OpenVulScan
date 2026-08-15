namespace OpenVulScan.Tests;

/// <summary>
/// Inter-procedural NRE cases (ovs-xwx.12): call results carry the callee's summarized
/// return nullability, so derefs of null-returning callees' results are flagged while
/// unknown/metadata callees stay silent.
/// </summary>
public class V3080InterProcTests
{
    [Fact]
    public Task CalleeReturnsNullDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_callee_returns_null_deref",
        @"
class C
{
    string F() { return null; }

    int M()
    {
        var s = F();
        return s.Length;
    }
}");

    [Fact]
    public Task CalleeMaybeNullDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_callee_maybe_null_deref",
        @"
class C
{
    string F(bool b)
    {
        if (b)
        {
            return null;
        }

        return ""x"";
    }

    int M()
    {
        var s = F(true);
        return s.Length;
    }
}");

    [Fact]
    public Task NotNullFactorySilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_notnull_factory_silent",
        @"
class C
{
    object F() { return new object(); }

    string M()
    {
        var o = F();
        return o.ToString();
    }
}");

    [Fact]
    public Task GuardedCallResultSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_guarded_call_result_silent",
        @"
class C
{
    string F() { return null; }

    int M()
    {
        var s = F();
        if (s != null)
        {
            return s.Length;
        }

        return 0;
    }
}");

    [Fact]
    public Task TwoMethodChainDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_two_method_chain_deref",
        @"
class C
{
    string F() { return null; }

    string G() { return F(); }

    int M()
    {
        var s = G();
        return s.Length;
    }
}");

    [Fact]
    public Task RecursiveCalleeDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_recursive_callee_deref",
        @"
class C
{
    string F(int n)
    {
        if (n > 0)
        {
            return F(n - 1);
        }

        return null;
    }

    int M()
    {
        var s = F(3);
        return s.Length;
    }
}");

    [Fact]
    public Task MetadataCalleeSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_metadata_callee_silent",
        @"
class C
{
    int M()
    {
        var s = ""abc"".ToString();
        return s.Length;
    }
}");

    [Fact]
    public Task ExpressionBodiedCalleeDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_expression_bodied_callee_deref",
        @"
class C
{
    string F() => null;

    int M()
    {
        var s = F();
        return s.Length;
    }
}");

    [Fact]
    public Task CalleeReturnsParameterSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_callee_returns_parameter_silent",
        @"
class C
{
    string F(string s) { return s; }

    int M()
    {
        var s = F(""x"");
        return s.Length;
    }
}");

    [Fact]
    public Task ConditionalAccessOnCallResultSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_conditional_access_silent",
        @"
class C
{
    string F() { return null; }

    int? M()
    {
        return F()?.Length;
    }
}");

    [Fact]
    public Task ResultThroughLocalCopyDeref() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_result_through_local_copy_deref",
        @"
class C
{
    string F() { return null; }

    int M()
    {
        var s = F();
        var t = s;
        return t.Length;
    }
}");

    [Fact]
    public Task ValueTypeResultSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3080",
        "interproc_value_type_result_silent",
        @"
class C
{
    int F() { return 1; }

    string M()
    {
        var n = F();
        return n.ToString();
    }
}");
}
