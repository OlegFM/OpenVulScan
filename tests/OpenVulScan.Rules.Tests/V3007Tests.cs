namespace OpenVulScan.Tests;

public class V3007Tests
{
    [Fact]
    public Task EmptyIfStatement()
    {
        const string source = "class C { void M() { if (true); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyIfStatement", source);
    }

    [Fact]
    public Task EmptyForStatement()
    {
        const string source = "class C { void M() { for (;;); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyForStatement", source);
    }

    [Fact]
    public Task EmptyWhileStatement()
    {
        const string source = "class C { void M() { while (true); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyWhileStatement", source);
    }

    [Fact]
    public Task EmptyDoWhileStatement()
    {
        const string source = "class C { void M() { do; while (true); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyDoWhileStatement", source);
    }

    [Fact]
    public Task EmptyForeachStatement()
    {
        const string source = "class C { void M(System.Collections.Generic.List<int> list) { foreach (var x in list); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyForeachStatement", source);
    }

    [Fact]
    public Task EmptyLockStatement()
    {
        const string source = "class C { void M() { object obj = new object(); lock (obj); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyLockStatement", source);
    }

    [Fact]
    public Task EmptyUsingStatement()
    {
        const string source = "class C { void M() { using (var x = new System.IO.MemoryStream()); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "EmptyUsingStatement", source);
    }

    [Fact]
    public Task IfWithBlock()
    {
        const string source = "class C { void M() { if (true) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "IfWithBlock", source);
    }

    [Fact]
    public Task IfWithStatement()
    {
        const string source = "class C { void M() { int x = 0; if (true) x = 1; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "IfWithStatement", source);
    }

    [Fact]
    public Task ForWithBlock()
    {
        const string source = "class C { void M() { for (;;) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "ForWithBlock", source);
    }

    [Fact]
    public Task WhileWithStatement()
    {
        const string source = "class C { void M() { while (true) break; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "WhileWithStatement", source);
    }

    [Fact]
    public Task DoWhileWithBlock()
    {
        const string source = "class C { void M() { do { } while (true); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3007", "DoWhileWithBlock", source);
    }
}
