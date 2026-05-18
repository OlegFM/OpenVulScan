namespace OpenVulScan.Tests;

public class V3001Tests
{
    [Fact]
    public Task IdenticalSubExpressionsDetected()
    {
        const string source = "class C { void M() { int a = 1; int b = 2; if (a == a) { } } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3001", "IdenticalSubExpressions", source);
    }
}
