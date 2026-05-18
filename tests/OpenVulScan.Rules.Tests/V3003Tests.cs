namespace OpenVulScan.Tests;

public class V3003Tests
{
    [Fact]
    public Task IfElseIfSameCondition()
    {
        const string source = "class C { void M() { int a = 1, b = 2; if (a == b) { } else if (a == b) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "IfElseIfSameCondition", source);
    }

    [Fact]
    public Task IfElseIfElseIfSameCondition()
    {
        const string source = "class C { void M() { int a = 1, b = 2, c = 3, d = 4; if (a == b) { } else if (c == d) { } else if (a == b) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "IfElseIfElseIfSameCondition", source);
    }

    [Fact]
    public Task IfElseIfSameConditionWithMethodCall()
    {
        const string source = "class C { bool F() => true; void M() { if (F()) { } else if (F()) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "IfElseIfSameConditionWithMethodCall", source);
    }

    [Fact]
    public Task MultipleIdenticalConditions()
    {
        const string source = "class C { void M() { int a = 1; if (a > 0) { } else if (a > 0) { } else if (a > 0) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "MultipleIdenticalConditions", source);
    }

    [Fact]
    public Task NestedExpressionSameCondition()
    {
        const string source = "class C { void M() { int a = 1, b = 2, c = 3; if (a + b > c) { } else if (a + b > c) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "NestedExpressionSameCondition", source);
    }

    [Fact]
    public Task DifferentConditions()
    {
        const string source = "class C { void M() { int a = 1, b = 2; if (a == 1) { } else if (b == 2) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "DifferentConditions", source);
    }

    [Fact]
    public Task SingleIfNoElse()
    {
        const string source = "class C { void M() { int a = 1; if (a == 1) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "SingleIfNoElse", source);
    }

    [Fact]
    public Task IfElseDifferentConditions()
    {
        const string source = "class C { void M() { int a = 1; if (a == 1) { } else { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "IfElseDifferentConditions", source);
    }

    [Fact]
    public Task SimilarButNotIdentical()
    {
        const string source = "class C { void M() { int a = 1, b = 2, c = 3; if (a == b) { } else if (a == c) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "SimilarButNotIdentical", source);
    }

    [Fact]
    public Task ElseIfChainDifferent()
    {
        const string source = "class C { void M() { int a = 1, b = 2, c = 3; if (a == 1) { } else if (b == 2) { } else if (c == 3) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3003", "ElseIfChainDifferent", source);
    }
}
