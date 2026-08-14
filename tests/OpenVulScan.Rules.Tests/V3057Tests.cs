using Xunit;

namespace OpenVulScan.Tests;

public class V3057Tests
{
    [Fact]
    public Task NegativeSubstringStartDetected()
    {
        const string source = @"
class C
{
    string M(string s)
    {
        return s.Substring(-1);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "NegativeSubstringStartDetected", source);
    }

    [Fact]
    public Task GuardProvenNegativeDetected()
    {
        const string source = @"
class C
{
    string M(string s, int i)
    {
        if (i < 0)
        {
            return s.Substring(i);
        }
        return s;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "GuardProvenNegativeDetected", source);
    }

    [Fact]
    public Task NegativeArraySizeDetected()
    {
        const string source = @"
class C
{
    int[] M()
    {
        int n = -5;
        return new int[n];
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "NegativeArraySizeDetected", source);
    }

    [Fact]
    public Task NegativeRemoveDetected()
    {
        const string source = @"
class C
{
    string M(string s)
    {
        int start = 2 - 4;
        return s.Remove(start);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "NegativeRemoveDetected", source);
    }

    [Fact]
    public Task NonNegativeNotFlagged()
    {
        const string source = @"
class C
{
    string M(string s)
    {
        return s.Substring(0);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "NonNegativeNotFlagged", source);
    }

    [Fact]
    public Task UnknownArgumentNotFlagged()
    {
        const string source = @"
class C
{
    string M(string s, int i)
    {
        return s.Substring(i);
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "UnknownArgumentNotFlagged", source);
    }

    [Fact]
    public Task GuardedNonNegativeNotFlagged()
    {
        const string source = @"
class C
{
    string M(string s, int i)
    {
        if (i >= 0)
        {
            return s.Substring(i);
        }
        return s;
    }
}";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3057", "GuardedNonNegativeNotFlagged", source);
    }
}
