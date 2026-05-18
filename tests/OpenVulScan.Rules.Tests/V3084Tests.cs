namespace OpenVulScan.Tests;

public class V3084Tests
{
    [Fact]
    public Task UnsubscribeParenthesizedLambda()
    {
        const string source = @"
using System;
class C
{
    event EventHandler? MyEvent;
    void M() { MyEvent -= (sender, e) => { }; }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "UnsubscribeParenthesizedLambda", source);
    }

    [Fact]
    public Task UnsubscribeAnonymousDelegate()
    {
        const string source = @"
using System;
class C
{
    event EventHandler? MyEvent;
    void M() { MyEvent -= delegate { }; }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "UnsubscribeAnonymousDelegate", source);
    }

    [Fact]
    public Task UnsubscribeSimpleLambda()
    {
        const string source = @"
using System;
class C
{
    event Action<int>? MyEvent;
    void M() { MyEvent -= x => { }; }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "UnsubscribeSimpleLambda", source);
    }

    [Fact]
    public Task UnsubscribeParenthesizedLambdaWithBody()
    {
        const string source = @"
using System;
class C
{
    event EventHandler? MyEvent;
    void M() { MyEvent -= (sender, e) => Console.WriteLine(""x""); }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "UnsubscribeParenthesizedLambdaWithBody", source);
    }

    [Fact]
    public Task UnsubscribeNamedHandler()
    {
        const string source = @"
using System;
class C
{
    event EventHandler? MyEvent;
    void Handler(object? s, EventArgs e) { }
    void M() { MyEvent -= Handler; }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "UnsubscribeNamedHandler", source);
    }

    [Fact]
    public Task SubscribeLambda()
    {
        const string source = @"
using System;
class C
{
    event EventHandler? MyEvent;
    void M() { MyEvent += (sender, e) => { }; }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "SubscribeLambda", source);
    }

    [Fact]
    public Task NumericSubtractAssign()
    {
        const string source = "class C { void M() { int x = 5; x -= 1; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "NumericSubtractAssign", source);
    }

    [Fact]
    public Task UnsubscribeNull()
    {
        const string source = @"
using System;
class C
{
    event EventHandler? MyEvent;
    void M() { MyEvent -= null; }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3084", "UnsubscribeNull", source);
    }
}
