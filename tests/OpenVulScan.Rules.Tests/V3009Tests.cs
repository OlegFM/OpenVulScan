namespace OpenVulScan.Tests;

public class V3009Tests
{
    [Fact]
    public Task SameIntReturn()
    {
        const string source = """
            class C {
                int M(bool x) {
                    if (x) return 1;
                    return 1;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3009", "SameIntReturn", source);
    }

    [Fact]
    public Task SameStringReturn()
    {
        const string source = """
            class C {
                string M(bool x) {
                    if (x) return "hello";
                    return "hello";
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3009", "SameStringReturn", source);
    }

    [Fact]
    public Task SameBoolReturn()
    {
        const string source = """
            class C {
                bool M(bool x) {
                    if (x) return true;
                    return true;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3009", "SameBoolReturn", source);
    }

    [Fact]
    public Task DifferentReturns()
    {
        const string source = """
            class C {
                int M(bool x) {
                    if (x) return 1;
                    return 2;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3009", "DifferentReturns", source);
    }

    [Fact]
    public Task SingleReturn()
    {
        const string source = """
            class C {
                int M() {
                    return 1;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3009", "SingleReturn", source);
    }

    [Fact]
    public Task SameNullReturn()
    {
        const string source = """
            class C {
                string M(bool x) {
                    if (x) return null;
                    return null;
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3009", "SameNullReturn", source);
    }
}
