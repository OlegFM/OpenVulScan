using Xunit;

namespace OpenVulScan.Tests;

public class V3008Tests
{
    [Fact]
    public Task TwoSuccessiveAssignmentsDetected()
    {
        const string source = @"
class C
{
    void M()
    {
        int x = 1;
        x = 2;
        System.Console.WriteLine(x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "TwoSuccessiveAssignmentsDetected", source);
    }

    [Fact]
    public Task ThreeAssignmentsDetectedTwice()
    {
        const string source = @"
class C
{
    void M()
    {
        int x = 1;
        x = 2;
        x = 3;
        System.Console.WriteLine(x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "ThreeAssignmentsDetectedTwice", source);
    }

    [Fact]
    public Task ReadBetweenAssignmentsNotFlagged()
    {
        const string source = @"
class C
{
    void M()
    {
        int x = 1;
        int y = x;
        x = 2;
        System.Console.WriteLine(y + x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "ReadBetweenAssignmentsNotFlagged", source);
    }

    [Fact]
    public Task SelfReferencingSecondAssignmentNotFlagged()
    {
        const string source = @"
class C
{
    void M()
    {
        int x = 1;
        x = x + 1;
        System.Console.WriteLine(x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "SelfReferencingSecondAssignmentNotFlagged", source);
    }

    [Fact]
    public Task BranchBetweenAssignmentsNotFlagged()
    {
        const string source = @"
class C
{
    void M(bool c)
    {
        int x = 1;
        if (c) { System.Console.WriteLine(1); }
        x = 2;
        System.Console.WriteLine(x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "BranchBetweenAssignmentsNotFlagged", source);
    }

    [Fact]
    public Task RefArgumentBetweenNotFlagged()
    {
        const string source = @"
class C
{
    void F(ref int v) { v = 7; }

    void M()
    {
        int x = 1;
        F(ref x);
        x = 2;
        System.Console.WriteLine(x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "RefArgumentBetweenNotFlagged", source);
    }

    [Fact]
    public Task CompoundAssignmentNotFlagged()
    {
        const string source = @"
class C
{
    void M()
    {
        int x = 1;
        x += 2;
        System.Console.WriteLine(x);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3008", "CompoundAssignmentNotFlagged", source);
    }
}
