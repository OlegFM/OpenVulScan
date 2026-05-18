namespace OpenVulScan.Tests;

public class V3025Tests
{
    [Fact]
    public Task TooFewArguments()
    {
        const string source = "class C { void M() { string.Format(\"{0} {1}\", \"a\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "TooFewArguments", source);
    }

    [Fact]
    public Task TooManyArguments()
    {
        const string source = "class C { void M() { string.Format(\"{0}\", \"a\", \"b\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "TooManyArguments", source);
    }

    [Fact]
    public Task CorrectCount()
    {
        const string source = "class C { void M() { string.Format(\"{0} {1}\", \"a\", \"b\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "CorrectCount", source);
    }

    [Fact]
    public Task NoPlaceholders()
    {
        const string source = "class C { void M() { string.Format(\"hello\", \"a\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "NoPlaceholders", source);
    }

    [Fact]
    public Task EscapedBraces()
    {
        const string source = "class C { void M() { string.Format(\"{{0}} {0}\", \"a\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "EscapedBraces", source);
    }

    [Fact]
    public Task ConsoleWriteLineMismatch()
    {
        const string source = "class C { void M() { System.Console.WriteLine(\"{0} {1} {2}\", \"a\", \"b\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "ConsoleWriteLineMismatch", source);
    }

    [Fact]
    public Task VariableFormatString()
    {
        const string source = "class C { void M(string fmt) { string.Format(fmt, \"a\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "VariableFormatString", source);
    }

    [Fact]
    public Task NoArguments()
    {
        const string source = "class C { void M() { string.Format(\"hello\"); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "NoArguments", source);
    }

    [Fact]
    public Task ComplexPlaceholders()
    {
        const string source = "class C { void M() { string.Format(\"{0:D} {1,5} {2}\", 1, 2, 3); } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "ComplexPlaceholders", source);
    }

    [Fact]
    public Task NonFormatMethod()
    {
        const string source = "class C { void M() { SomeMethod(\"{0} {1}\", \"a\"); } void SomeMethod(string s, string a) { } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3025", "NonFormatMethod", source);
    }
}
