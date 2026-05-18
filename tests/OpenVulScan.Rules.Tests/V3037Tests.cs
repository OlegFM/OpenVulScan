namespace OpenVulScan.Tests;

public class V3037Tests
{
    [Fact]
    public Task SimpleVariableSwapWithoutTemp()
    {
        const string source = "class C { void M() { int a = 1, b = 2; a = b; b = a; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3037", "SimpleVariableSwapWithoutTemp", source);
    }

    [Fact]
    public Task FieldSwapWithoutTemp()
    {
        const string source = "class C { int x, y; void M() { this.x = this.y; this.y = this.x; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3037", "FieldSwapWithoutTemp", source);
    }

    [Fact]
    public Task IndexerSwapWithoutTemp()
    {
        const string source = "class C { void M(int[] arr) { arr[0] = arr[1]; arr[1] = arr[0]; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3037", "IndexerSwapWithoutTemp", source);
    }

    [Fact]
    public Task NotASwap()
    {
        const string source = "class C { void M() { int a = 1, b = 2, c = 3; a = b; b = c; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3037", "NotASwap", source);
    }

    [Fact]
    public Task SwapWithTemp()
    {
        const string source = "class C { void M() { int a = 1, b = 2, t; t = a; a = b; b = t; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3037", "SwapWithTemp", source);
    }
}
