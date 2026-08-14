using Xunit;

namespace OpenVulScan.Tests;

public class V3073Tests
{
    private const string Res = "sealed class Res : System.IDisposable { public void Dispose() {} }\n";

    [Fact] // FLAG: field never disposed in Dispose().
    public Task FieldNotDisposed() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "FieldNotDisposed", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() { }
        }
        """);

    [Fact] // NO FLAG: field disposed.
    public Task FieldDisposed() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "FieldDisposed", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() { _a.Dispose(); }
        }
        """);

    [Fact] // FLAG: one of two fields disposed, the other not.
    public Task OneFieldDisposedOneNot() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "OneFieldDisposedOneNot", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            private Res _b = new Res();
            public void Dispose() { _a.Dispose(); }
        }
        """);

    [Fact] // NO FLAG: both fields disposed.
    public Task BothFieldsDisposed() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "BothFieldsDisposed", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            private Res _b = new Res();
            public void Dispose() { _a.Dispose(); _b.Dispose(); }
        }
        """);

    [Fact] // FLAG: field disposed only under an unrelated condition ⇒ genuine partial dispose.
    public Task FieldConditionallyDisposedFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "FieldConditionallyDisposedFlag", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            private bool _flag;
            public void Dispose() { if (_flag) { _a.Dispose(); } }
        }
        """);

    [Fact] // NO FLAG: idiomatic null-guarded dispose — the null branch has nothing to leak.
    public Task NullGuardedFieldDisposeNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "NullGuardedFieldDisposeNoFlag", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() { if (_a != null) { _a.Dispose(); } }
        }
        """);

    [Fact] // NO FLAG: early-return null guard, then dispose.
    public Task EarlyReturnGuardNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "EarlyReturnGuardNoFlag", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() {
                if (_a == null) { return; }
                _a.Dispose();
            }
        }
        """);

    [Fact] // NO FLAG: non-disposable field is ignored.
    public Task NonDisposableFieldIgnored() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "NonDisposableFieldIgnored", Res + """
        class C : System.IDisposable {
            private int _n = 1;
            public void Dispose() { }
        }
        """);

    [Fact] // NO FLAG: field disposed via this.
    public Task FieldDisposedViaThis() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "FieldDisposedViaThis", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() { this._a.Dispose(); }
        }
        """);

    [Fact] // NO FLAG: null-conditional dispose of the field (disposes on all paths).
    public Task NullConditionalDisposedNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "NullConditionalDisposedNoFlag", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() { _a?.Dispose(); }
        }
        """);

    [Fact] // NO FLAG: canonical Dispose(bool) pattern — parameterless Dispose() delegates.
    public Task DisposePatternNoFlag() => SnapshotTestHarness.RunRuleSnapshotAsync("V3073", "DisposePatternNoFlag", Res + """
        class C : System.IDisposable {
            private Res _a = new Res();
            public void Dispose() { Dispose(true); }
            protected virtual void Dispose(bool disposing) { if (disposing) { _a.Dispose(); } }
        }
        """);
}
