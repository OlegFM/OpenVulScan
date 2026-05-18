namespace OpenVulScan.Tests;

public class V3038Tests
{
    [Fact]
    public Task SameVariableTwice()
    {
        const string source = "class C { void M(int a) { F(a, a); } void F(int x, int y) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3038", "SameVariableTwice", source);
    }

    [Fact]
    public Task SameVariableThreeTimes()
    {
        const string source = "class C { void M(int a) { F(a, a, a); } void F(int x, int y, int z) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3038", "SameVariableThreeTimes", source);
    }

    [Fact]
    public Task SameLiteralTwice()
    {
        const string source = "class C { void M() { F(1, 1); } void F(int x, int y) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3038", "SameLiteralTwice", source);
    }

    [Fact]
    public Task DifferentArguments()
    {
        const string source = "class C { void M(int a, int b) { F(a, b); } void F(int x, int y) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3038", "DifferentArguments", source);
    }

    [Fact]
    public Task SingleArgument()
    {
        const string source = "class C { void M(int a) { F(a); } void F(int x) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3038", "SingleArgument", source);
    }
}
