namespace OpenVulScan.Tests;

public class V3041Tests
{
    [Fact]
    public Task IntMaxValueToDouble()
    {
        const string source = "class C { void M() { double d = int.MaxValue; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3041", "IntMaxValueToDouble", source);
    }

    [Fact]
    public Task IntLiteralToFloatParameter()
    {
        const string source = "class C { void M() { F(123456789); } void F(float x) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3041", "IntLiteralToFloatParameter", source);
    }

    [Fact]
    public Task LongToDoubleAssignment()
    {
        const string source = "class C { void M() { double d = 123456789012345L; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3041", "LongToDoubleAssignment", source);
    }

    [Fact]
    public Task IntLiteralToDoubleParameter()
    {
        const string source = "class C { void M() { F(123); } void F(double x) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3041", "IntLiteralToDoubleParameter", source);
    }

    [Fact]
    public Task NoLossDoubleLiteral()
    {
        const string source = "class C { void M() { double d = 1.0; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3041", "NoLossDoubleLiteral", source);
    }
}
