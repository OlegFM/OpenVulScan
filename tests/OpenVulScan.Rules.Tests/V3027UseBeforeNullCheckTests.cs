namespace OpenVulScan.Tests;

public class V3027UseBeforeNullCheckTests
{
    [Fact]
    public Task AndChainMemberAccessBeforeNotNullFlags()
    {
        const string source = "class C { void M(int[] a) { var r = a.Length > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "AndChain_MemberAccessBeforeNotNull_Flags", source);
    }

    [Fact]
    public Task OrChainMemberAccessBeforeEqualsNullFlags()
    {
        const string source = "class C { void M(string s) { var r = s.Length == 0 || s == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "OrChain_MemberAccessBeforeEqualsNull_Flags", source);
    }

    [Fact]
    public Task ElementAccessBeforeNotNullFlags()
    {
        const string source = "class C { void M(int[] a) { var r = a[0] > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "ElementAccessBeforeNotNull_Flags", source);
    }

    [Fact]
    public Task MemberOfMemberBeforeEqualsNullFlags()
    {
        const string source = "class N { public N b; } class C { void M(N a) { var r = a.b != null && a == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MemberOfMemberBeforeEqualsNull_Flags", source);
    }

    [Fact]
    public Task InvocationReceiverBeforeNotNullFlags()
    {
        const string source = "class C { void M(object obj) { var r = obj.ToString() != \"\" && obj != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "InvocationReceiverBeforeNotNull_Flags", source);
    }

    [Fact]
    public Task MultiOperandSecondVariableDerefBeforeCheckFlags()
    {
        const string source = "class C { void M(object a, int[] b) { var r = a != null && b.Length > 0 && b == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MultiOperand_SecondVariableDerefBeforeCheck_Flags", source);
    }

    [Fact]
    public Task MemberAccessBeforeIsNullFlags()
    {
        const string source = "class C { void M(string s) { var r = s.Length > 0 && s is null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MemberAccessBeforeIsNull_Flags", source);
    }

    [Fact]
    public Task MemberAccessBeforeIsNotNullFlags()
    {
        const string source = "class C { void M(string s) { var r = s.Length > 0 && s is not null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MemberAccessBeforeIsNotNull_Flags", source);
    }

    [Fact]
    public Task AndChainNullCheckBeforeMemberAccessDoesNotFlag()
    {
        const string source = "class C { void M(int[] a) { var r = a != null && a.Length > 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "AndChain_NullCheckBeforeMemberAccess_DoesNotFlag", source);
    }

    [Fact]
    public Task OrChainNullCheckBeforeMemberAccessDoesNotFlag()
    {
        const string source = "class C { void M(string s) { var r = s == null || s.Length == 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "OrChain_NullCheckBeforeMemberAccess_DoesNotFlag", source);
    }

    [Fact]
    public Task ConditionalAccessBeforeNotNullDoesNotFlag()
    {
        const string source = "class C { void M(int[] a) { var r = a?.Length > 0 && a != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "ConditionalAccessBeforeNotNull_DoesNotFlag", source);
    }

    [Fact]
    public Task TwoNullChecksNoDerefDoesNotFlag()
    {
        const string source = "class C { void M(object a, object b) { var r = a != null && b != null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "TwoNullChecksNoDeref_DoesNotFlag", source);
    }

    [Fact]
    public Task NoNullsInvolvedDoesNotFlag()
    {
        const string source = "class C { void M(int x, int y) { var r = x > 0 && y < 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "NoNullsInvolved_DoesNotFlag", source);
    }

    [Fact]
    public Task IsNotNullGuardBeforeMemberAccessDoesNotFlag()
    {
        const string source = "class C { void M(string s) { var r = s is not null && s.Length > 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "IsNotNullGuardBeforeMemberAccess_DoesNotFlag", source);
    }

    [Fact]
    public Task MixedPrecedenceDerefBeforeCheckFlags()
    {
        const string source = "class C { void M(int[] a, bool b) { var r = a.Length > 0 && b || a == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MixedPrecedence_DerefBeforeCheck_Flags", source);
    }

    [Fact]
    public Task MixedPrecedenceCheckBeforeDerefDoesNotFlag()
    {
        const string source = "class C { void M(int[] a, bool b) { var r = a == null && b || a.Length > 0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "MixedPrecedence_CheckBeforeDeref_DoesNotFlag", source);
    }

    [Fact]
    public Task RedundantCheckAfterGuardDoesNotFlag()
    {
        const string source = "class C { void M(int[] a) { var r = a != null && a.Length > 0 && a == null; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3027", "RedundantCheckAfterGuard_DoesNotFlag", source);
    }
}
