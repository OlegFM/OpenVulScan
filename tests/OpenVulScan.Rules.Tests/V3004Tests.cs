namespace OpenVulScan.Tests;

public class V3004Tests
{
    [Fact]
    public Task IdenticalBlocks()
    {
        const string source = "class C { void M() { int x; bool a = true; if (a) { x = 1; } else { x = 1; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "IdenticalBlocks", source);
    }

    [Fact]
    public Task IdenticalBlocksWithMultipleStatements()
    {
        const string source = "class C { void M() { int x, y; bool a = true; if (a) { x = 1; y = 2; } else { x = 1; y = 2; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "IdenticalBlocksWithMultipleStatements", source);
    }

    [Fact]
    public Task IdenticalMethodCalls()
    {
        const string source = "class C { void F() { } void M() { bool a = true; if (a) { F(); } else { F(); } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "IdenticalMethodCalls", source);
    }

    [Fact]
    public Task IdenticalReturn()
    {
        const string source = "class C { int M() { bool a = true; if (a) { return 1; } else { return 1; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "IdenticalReturn", source);
    }

    [Fact]
    public Task IdenticalBlockWithVariableDecl()
    {
        const string source = "class C { void M() { bool a = true; if (a) { int x = 1; } else { int x = 1; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "IdenticalBlockWithVariableDecl", source);
    }

    [Fact]
    public Task DifferentBlocks()
    {
        const string source = "class C { void M() { int x; bool a = true; if (a) { x = 1; } else { x = 2; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "DifferentBlocks", source);
    }

    [Fact]
    public Task EmptyThenBlock()
    {
        const string source = "class C { void M() { int x; bool a = true; if (a) { } else { x = 1; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "EmptyThenBlock", source);
    }

    [Fact]
    public Task BothBlocksEmpty()
    {
        const string source = "class C { void M() { bool a = true; if (a) { } else { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "BothBlocksEmpty", source);
    }

    [Fact]
    public Task SingleIfNoElse()
    {
        const string source = "class C { void M() { int x; bool a = true; if (a) { x = 1; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "SingleIfNoElse", source);
    }

    [Fact]
    public Task ElseIfNotSimpleElse()
    {
        const string source = "class C { void M() { int x; bool a = true, b = false; if (a) { x = 1; } else if (b) { x = 1; } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3004", "ElseIfNotSimpleElse", source);
    }
}
