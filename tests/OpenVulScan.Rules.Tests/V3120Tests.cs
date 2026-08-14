using Xunit;

namespace OpenVulScan.Tests;

public class V3120Tests
{
    [Fact]
    public Task WhileConditionVariableNeverChangedDetected()
    {
        const string source = @"
class C
{
    void M()
    {
        int i = 0;
        int n = 0;
        while (i < 10)
        {
            n = n + 1;
        }
        System.Console.WriteLine(n);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "WhileConditionVariableNeverChangedDetected", source);
    }

    [Fact]
    public Task DoWhileNeverChangedDetected()
    {
        const string source = @"
class C
{
    void M(int i)
    {
        int n = 0;
        do
        {
            n = n + 1;
        }
        while (i != 0);
        System.Console.WriteLine(n);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "DoWhileNeverChangedDetected", source);
    }

    [Fact]
    public Task ForWithoutIncrementorDetected()
    {
        const string source = @"
class C
{
    void M()
    {
        int n = 0;
        for (int i = 0; i < 10;)
        {
            n = n + 1;
        }
        System.Console.WriteLine(n);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "ForWithoutIncrementorDetected", source);
    }

    [Fact]
    public Task MutatedInBodyNotFlagged()
    {
        const string source = @"
class C
{
    void M()
    {
        int i = 0;
        while (i < 10)
        {
            i = i + 1;
        }
        System.Console.WriteLine(i);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "MutatedInBodyNotFlagged", source);
    }

    [Fact]
    public Task BreakInBodyNotFlagged()
    {
        const string source = @"
class C
{
    void M(bool stop)
    {
        int i = 0;
        while (i < 10)
        {
            if (stop) { break; }
        }
        System.Console.WriteLine(i);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "BreakInBodyNotFlagged", source);
    }

    [Fact]
    public Task ReturnInBodyNotFlagged()
    {
        const string source = @"
class C
{
    int M(bool stop)
    {
        int i = 0;
        while (i < 10)
        {
            if (stop) { return 1; }
        }
        return 0;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "ReturnInBodyNotFlagged", source);
    }

    [Fact]
    public Task RefEscapeNotFlagged()
    {
        const string source = @"
class C
{
    void F(ref int v) { v = 100; }

    void M()
    {
        int i = 0;
        while (i < 10)
        {
            F(ref i);
        }
        System.Console.WriteLine(i);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "RefEscapeNotFlagged", source);
    }

    [Fact]
    public Task ConditionWithCallNotFlagged()
    {
        const string source = @"
class C
{
    bool Check(int v) => v < 10;

    void M()
    {
        int i = 0;
        int n = 0;
        while (Check(i))
        {
            n = n + 1;
        }
        System.Console.WriteLine(n);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "ConditionWithCallNotFlagged", source);
    }

    [Fact]
    public Task LambdaMutationNotFlagged()
    {
        const string source = @"
class C
{
    void M()
    {
        int i = 0;
        while (i < 10)
        {
            System.Action a = () => { i = i + 1; };
            a();
        }
        System.Console.WriteLine(i);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3120", "LambdaMutationNotFlagged", source);
    }
}
