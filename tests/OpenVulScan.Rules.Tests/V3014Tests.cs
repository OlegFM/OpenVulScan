namespace OpenVulScan.Tests;

public class V3014Tests
{
    [Fact]
    public Task WrongLoopVariable()
    {
        const string source = """
            class C {
                void M(int n) {
                    for (int i = 0; i < n; j++) {
                    }
                }
                int j;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3014", "WrongLoopVariable", source);
    }

    [Fact]
    public Task CorrectLoopVariable()
    {
        const string source = """
            class C {
                void M(int n) {
                    for (int i = 0; i < n; i++) {
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3014", "CorrectLoopVariable", source);
    }

    [Fact]
    public Task MultipleVariablesCorrectIncrement()
    {
        const string source = """
            class C {
                void M(int n) {
                    for (int i = 0, j = 0; i < n; i++, j++) {
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3014", "MultipleVariablesCorrectIncrement", source);
    }

    [Fact]
    public Task NoDeclarationInFor()
    {
        const string source = """
            class C {
                void M(int n) {
                    int i = 0;
                    for (; i < n; i++) {
                    }
                }
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3014", "NoDeclarationInFor", source);
    }

    [Fact]
    public Task WrongVariableWithMultipleDecls()
    {
        const string source = """
            class C {
                void M(int n) {
                    for (int i = 0, j = 0; i < n; k++) {
                    }
                }
                int k;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3014", "WrongVariableWithMultipleDecls", source);
    }
}
