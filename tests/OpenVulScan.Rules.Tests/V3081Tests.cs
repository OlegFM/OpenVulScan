namespace OpenVulScan.Tests;

public class V3081Tests
{
    [Fact]
    public Task StandaloneArgumentException()
    {
        const string source = "class C { void M() { new System.ArgumentException(\"msg\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "StandaloneArgumentException", source);
    }

    [Fact]
    public Task StandaloneInvalidOperationException()
    {
        const string source = "class C { void M() { new System.InvalidOperationException(); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "StandaloneInvalidOperationException", source);
    }

    [Fact]
    public Task StandaloneCustomException()
    {
        const string source = @"
class CustomException : System.Exception { }
class C { void M() { new CustomException(); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "StandaloneCustomException", source);
    }

    [Fact]
    public Task StandaloneException()
    {
        const string source = "class C { void M() { new System.Exception(\"msg\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "StandaloneException", source);
    }

    [Fact]
    public Task ThrownException()
    {
        const string source = "class C { void M() { throw new System.ArgumentException(\"msg\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "ThrownException", source);
    }

    [Fact]
    public Task AssignedToVariable()
    {
        const string source = "class C { void M() { var ex = new System.ArgumentException(\"msg\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "AssignedToVariable", source);
    }

    [Fact]
    public Task PassedAsArgument()
    {
        const string source = "class C { void M() { Log(new System.ArgumentException(\"msg\")); } void Log(System.Exception ex) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "PassedAsArgument", source);
    }

    [Fact]
    public Task ReturnedFromMethod()
    {
        const string source = "class C { System.Exception M() { return new System.ArgumentException(\"msg\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "ReturnedFromMethod", source);
    }

    [Fact]
    public Task NonExceptionObjectCreation()
    {
        const string source = "class C { void M() { new System.Object(); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3081", "NonExceptionObjectCreation", source);
    }
}
