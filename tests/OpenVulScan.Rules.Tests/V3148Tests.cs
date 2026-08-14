using Xunit;

namespace OpenVulScan.Tests;

public class V3148Tests
{
    [Fact]
    public Task DefinitelyNullNullableCastDetected()
    {
        const string source = @"
class C
{
    int M()
    {
        int? x = null;
        return (int)x;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "DefinitelyNullNullableCastDetected", source);
    }

    [Fact]
    public Task MaybeNullNullableCastDetected()
    {
        const string source = @"
class C
{
    int M(bool c)
    {
        int? x = null;
        if (c)
        {
            x = 5;
        }
        return (int)x;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "MaybeNullNullableCastDetected", source);
    }

    [Fact]
    public Task UnboxingMaybeNullObjectDetected()
    {
        const string source = @"
class C
{
    int M(bool c)
    {
        object o = null;
        if (c)
        {
            o = 42;
        }
        return (int)o;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "UnboxingMaybeNullObjectDetected", source);
    }

    [Fact]
    public Task NullCheckGuardedCastNotFlagged()
    {
        const string source = @"
class C
{
    int M(bool c)
    {
        int? x = null;
        if (c)
        {
            x = 5;
        }
        if (x != null)
        {
            return (int)x;
        }
        return 0;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "NullCheckGuardedCastNotFlagged", source);
    }

    [Fact]
    public Task HasValueGuardedCastNotFlagged()
    {
        const string source = @"
class C
{
    int M(bool c)
    {
        int? x = null;
        if (c)
        {
            x = 5;
        }
        if (x.HasValue)
        {
            return (int)x;
        }
        return 0;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "HasValueGuardedCastNotFlagged", source);
    }

    [Fact]
    public Task NotNullNullableCastNotFlagged()
    {
        const string source = @"
class C
{
    int M()
    {
        int? x = 5;
        return (int)x;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "NotNullNullableCastNotFlagged", source);
    }

    [Fact]
    public Task UnknownParameterCastNotFlagged()
    {
        const string source = @"
class C
{
    int M(int? x)
    {
        return (int)x;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3148", "UnknownParameterCastNotFlagged", source);
    }
}
