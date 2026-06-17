using Xunit;

namespace OpenVulScan.Tests;

public class V3178Tests
{
    private const string Res = "sealed class Res : System.IDisposable { public void Dispose() {} public void Use() {} public int P { get; set; } }\n";

    [Fact] // FLAG: method call after dispose.
    public Task UseAfterDispose() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "UseAfterDispose", Res + """
        class C {
            void M() {
                var r = new Res();
                r.Dispose();
                r.Use();
            }
        }
        """);

    [Fact] // FLAG: property access after dispose.
    public Task PropertyAfterDispose() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "PropertyAfterDispose", Res + """
        class C {
            int M() {
                var r = new Res();
                r.Dispose();
                return r.P;
            }
        }
        """);

    [Fact] // FLAG: double dispose (straight-line).
    public Task DoubleDispose() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "DoubleDispose", Res + """
        class C {
            void M() {
                var r = new Res();
                r.Dispose();
                r.Dispose();
            }
        }
        """);

    [Fact] // NO FLAG: use before dispose.
    public Task UseBeforeDisposeNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "UseBeforeDisposeNoFlag", Res + """
        class C {
            void M() {
                var r = new Res();
                r.Use();
                r.Dispose();
            }
        }
        """);

    [Fact] // FLAG (MAY): used on a path where it may already be disposed.
    public Task MayBeDisposedBranch() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "MayBeDisposedBranch", Res + """
        class C {
            void M(bool flag) {
                var r = new Res();
                if (flag) { r.Dispose(); }
                r.Use();
            }
        }
        """);

    [Fact] // NO FLAG: `using` body uses the resource before the lowered dispose.
    public Task UsingBodyNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "UsingBodyNoFlag", Res + """
        class C {
            void M() {
                using var r = new Res();
                r.Use();
            }
        }
        """);

    [Fact] // NO FLAG: two distinct objects, only one disposed.
    public Task DistinctObjectsNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3178", "DistinctObjectsNoFlag", Res + """
        class C {
            void M() {
                var a = new Res();
                var b = new Res();
                a.Dispose();
                b.Use();
            }
        }
        """);
}
