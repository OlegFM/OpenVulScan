namespace OpenVulScan.Tests;

public class V3016Tests
{
    [Fact]
    public Task NestedForSameVariable()
    {
        const string source = "class C { void M() { for (int i = 0; i < 10; i++) for (int i = 0; i < 10; i++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "NestedForSameVariable", source);
    }

    [Fact]
    public Task NestedForEachSameVariable()
    {
        const string source = "class C { void M(int[] a, int[] b) { foreach (var x in a) foreach (var x in b) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "NestedForEachSameVariable", source);
    }

    [Fact]
    public Task ForInsideForEachSameVariable()
    {
        const string source = "class C { void M(int[] a) { foreach (var i in a) for (int i = 0; i < 10; i++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "ForInsideForEachSameVariable", source);
    }

    [Fact]
    public Task ForEachInsideForSameVariable()
    {
        const string source = "class C { void M(int[] a) { for (int i = 0; i < 10; i++) foreach (var i in a) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "ForEachInsideForSameVariable", source);
    }

    [Fact]
    public Task TripleNestedSameVariable()
    {
        const string source = "class C { void M() { for (int i = 0; i < 10; i++) for (int j = 0; j < 10; j++) for (int i = 0; i < 10; i++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "TripleNestedSameVariable", source);
    }

    [Fact]
    public Task DifferentVariableNames()
    {
        const string source = "class C { void M() { for (int i = 0; i < 10; i++) for (int j = 0; j < 10; j++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "DifferentVariableNames", source);
    }

    [Fact]
    public Task SequentialLoopsSameVariable()
    {
        const string source = "class C { void M() { for (int i = 0; i < 10; i++) { } for (int i = 0; i < 10; i++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "SequentialLoopsSameVariable", source);
    }

    [Fact]
    public Task SingleLoop()
    {
        const string source = "class C { void M() { for (int i = 0; i < 10; i++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "SingleLoop", source);
    }

    [Fact]
    public Task NestedForDifferentVariables()
    {
        const string source = "class C { void M() { for (int i = 0; i < 10; i++) for (int j = 0; j < 10; j++) for (int k = 0; k < 10; k++) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "NestedForDifferentVariables", source);
    }

    [Fact]
    public Task ForEachInsideForDifferentVariable()
    {
        const string source = "class C { void M(int[] a) { for (int i = 0; i < 10; i++) foreach (var x in a) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3016", "ForEachInsideForDifferentVariable", source);
    }
}
