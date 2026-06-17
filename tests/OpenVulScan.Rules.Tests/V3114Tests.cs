using Xunit;

namespace OpenVulScan.Tests;

public class V3114Tests
{
    // Every source defines its own IDisposable: the test compilation references only corelib.
    private const string Res = "sealed class Res : System.IDisposable { public void Dispose() {} public void Use() {} }\n";

    [Fact] // FLAG: created, never disposed.
    public Task DirectLeak() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "DirectLeak", Res + """
        class C {
            void M() {
                var r = new Res();
                r.Use();
            }
        }
        """);

    [Fact] // NO FLAG: disposed on the single path.
    public Task DisposedNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "DisposedNoFlag", Res + """
        class C {
            void M() {
                var r = new Res();
                r.Dispose();
            }
        }
        """);

    [Fact] // NO FLAG: `using` disposes by construction (excluded from tracking).
    public Task UsingDeclarationNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "UsingDeclarationNoFlag", Res + """
        class C {
            void M() {
                using var r = new Res();
                r.Use();
            }
        }
        """);

    [Fact] // NO FLAG: manual try/finally dispose (direct LocalReference, no null-guard).
    public Task TryFinallyNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "TryFinallyNoFlag", Res + """
        class C {
            void M() {
                var r = new Res();
                try { r.Use(); }
                finally { r.Dispose(); }
            }
        }
        """);

    [Fact] // FLAG: partial dispose — disposed only on the `flag` path.
    public Task PartialDisposeFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "PartialDisposeFlag", Res + """
        class C {
            void M(bool flag) {
                var r = new Res();
                if (flag) { r.Dispose(); }
            }
        }
        """);

    [Fact] // NO FLAG: returned — ownership escapes.
    public Task ReturnedNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "ReturnedNoFlag", Res + """
        class C {
            Res M() {
                var r = new Res();
                return r;
            }
        }
        """);

    [Fact] // NO FLAG: returned via an interface upcast — ownership still escapes.
    public Task ReturnCastNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "ReturnCastNoFlag", Res + """
        class C {
            System.IDisposable M() {
                var r = new Res();
                return (System.IDisposable)r;
            }
        }
        """);

    [Fact] // NO FLAG: stored to a field — ownership escapes.
    public Task StoredToFieldNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3114", "StoredToFieldNoFlag", Res + """
        class C {
            Res _f;
            void M() {
                var r = new Res();
                _f = r;
            }
        }
        """);
}
