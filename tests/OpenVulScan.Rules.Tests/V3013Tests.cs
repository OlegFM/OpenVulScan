namespace OpenVulScan.Tests;

public class V3013Tests
{
    [Fact]
    public Task SwitchWithoutDefault()
    {
        const string source = """
            class C {
                void M(int x) {
                    switch (x) {
                        case 1: break;
                        case 2: break;
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3013", "SwitchWithoutDefault", source);
    }

    [Fact]
    public Task SwitchWithDefault()
    {
        const string source = """
            class C {
                void M(int x) {
                    switch (x) {
                        case 1: break;
                        default: break;
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3013", "SwitchWithDefault", source);
    }

    [Fact]
    public Task EmptySwitch()
    {
        const string source = """
            class C {
                void M(int x) {
                    switch (x) {
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3013", "EmptySwitch", source);
    }

    [Fact]
    public Task SwitchWithOnlyDefault()
    {
        const string source = """
            class C {
                void M(int x) {
                    switch (x) {
                        default: break;
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3013", "SwitchWithOnlyDefault", source);
    }

    [Fact]
    public Task NestedSwitchInnerMissingDefault()
    {
        const string source = """
            class C {
                void M(int x, int y) {
                    switch (x) {
                        default:
                            switch (y) {
                                case 1: break;
                            }
                            break;
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3013", "NestedSwitchInnerMissingDefault", source);
    }
}
