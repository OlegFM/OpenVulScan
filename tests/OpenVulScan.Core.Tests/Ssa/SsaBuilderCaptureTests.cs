using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;
using static OpenVulScan.Tests.Ssa.CfgTestHarness;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderCaptureTests
{
    [Fact]
    public void SingleDefFlowCapture_GetsVersionZero()
    {
        // Captures are ordinary tracked defs since ovs-tr6 S-2.
        // Multi-def captures (?? / ?: arms) get distinct versions joined by phi.
        // A single-def capture's first version is 0 because versioning starts at 0.
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int Foo(string? s)
    {
        return (s ?? """").Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var captureOp = AllOps(cfg)
            .OfType<IFlowCaptureOperation>()
            .FirstOrDefault();
        Assert.NotNull(captureOp);

        var id = index.DefinitionAt(captureOp);
        Assert.NotNull(id);
        Assert.Equal(0, id!.Value.Version);
        Assert.IsType<TrackedKey.Capture>(id.Value.Key);
    }

    [Fact]
    public void FlowCaptureReference_RecordsUse()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int Foo(string? s)
    {
        return (s ?? """").Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Every IFlowCaptureOperation should have a corresponding use
        // somewhere among IFlowCaptureReferenceOperation sites.
        var captures = AllOps(cfg)
            .OfType<IFlowCaptureOperation>()
            .ToList();

        Assert.NotEmpty(captures);

        var captureRefs = AllOps(cfg)
            .OfType<IFlowCaptureReferenceOperation>()
            .ToList();

        // At least one reference must be tracked as a use.
        var anyUseTracked = captureRefs.Any(refOp =>
        {
            var key = new TrackedKey.Capture(refOp.Id);
            return index.UseAt(refOp, key) is not null;
        });

        Assert.True(anyUseTracked, "Expected at least one IFlowCaptureReferenceOperation to be recorded as a use.");
    }

    // ---------------------------------------------------------------
    // L-value capture aliasing tests (ovs-2qi.14 / commit 5ed53ec)
    // ---------------------------------------------------------------

    /// <summary>
    /// When Roslyn lowers <c>x = a?.F</c> it emits:
    ///   FlowCaptureOperation(id, LocalRef(x))   -- l-value capture of x
    ///   ...conditional blocks...
    ///   SimpleAssignmentOperation(FlowCaptureRef(id), value)  -- indirect write
    /// BuildCaptureToLocalMap maps that capture id to Symbol(x), so the
    /// assignment's def must be registered under TrackedKey.Symbol("x").
    /// </summary>
    [Fact]
    public void LValueCapture_AssignmentToLocal_TracksSymbolDef()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string F;
    void M(C a)
    {
        string x;
        x = a?.F;
        var n = x.Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Find the ISimpleAssignmentOperation whose Target is an
        // IFlowCaptureReferenceOperation (the lowered `x = a?.F`).
        var allOps = AllOps(cfg).ToList();
        var indirectAssign = allOps
            .OfType<ISimpleAssignmentOperation>()
            .FirstOrDefault(a => a.Target is IFlowCaptureReferenceOperation);

        Assert.NotNull(indirectAssign);

        var def = index.DefinitionAt(indirectAssign!);
        Assert.NotNull(def);

        // The def key must be TrackedKey.Symbol whose symbol is named "x".
        var symKey = Assert.IsType<TrackedKey.Symbol>(def!.Value.Key);
        Assert.Equal("x", symKey.Variable.Name);

        // Find the ILocalReferenceOperation for x that appears as the receiver
        // of the .Length member access (not as an assignment target).
        var xAsReceiver = allOps
            .OfType<ILocalReferenceOperation>()
            .FirstOrDefault(lr =>
                lr.Local.Name == "x"
                && lr.Parent is IMemberReferenceOperation);

        Assert.NotNull(xAsReceiver);

        var xKey = new TrackedKey.Symbol(xAsReceiver!.Local);
        var useId = index.UseAt(xAsReceiver, xKey);
        Assert.NotNull(useId);
        Assert.Equal(def.Value, useId!.Value);
    }

    /// <summary>
    /// R-value captures (where the captured value is NOT a local/parameter reference,
    /// e.g. a field-access arm or default arm of a conditional) must keep
    /// <see cref="TrackedKey.Capture"/> keys — no aliasing occurs.
    /// </summary>
    [Fact]
    public void RValueCapture_NotAliased_KeepsCaptureDef()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string F;
    void M(C a)
    {
        var y = (a?.F).Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var allCaptures = AllOps(cfg)
            .OfType<IFlowCaptureOperation>()
            .ToList();

        Assert.NotEmpty(allCaptures);  // guard against vacuity

        // Captures whose Value is not a local/parameter reference must have
        // TrackedKey.Capture — no aliasing.
        var rValueCaptures = allCaptures
            .Where(c => c.Value is not ILocalReferenceOperation
                     && c.Value is not IParameterReferenceOperation)
            .ToList();

        Assert.NotEmpty(rValueCaptures);  // guard: at least one r-value capture must exist

        foreach (var cap in rValueCaptures)
        {
            var def = index.DefinitionAt(cap);
            if (def is null) continue;  // un-tracked is also fine; tracked ones must be Capture
            Assert.IsType<TrackedKey.Capture>(def.Value.Key);
        }
    }

    /// <summary>
    /// When two branches each write to x via <c>x = a?.F</c>, x has 2 def-sites
    /// (semi-pruned criterion) so a φ must appear at the join block with a
    /// <see cref="TrackedKey.Symbol"/> key for x.
    /// </summary>
    [Fact]
    public void LValueCapture_ConditionalAssignBothArms_PlacesPhi()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string F;
    void M(C a, bool c)
    {
        string x;
        if (c) { x = a?.F; } else { x = a?.F; }
        var n = x.Length;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Some block must have a phi whose Result.Key is TrackedKey.Symbol("x").
        var phiForX = cfg.Blocks
            .SelectMany(b => index.PhisAt(b))
            .FirstOrDefault(phi =>
                phi.Result.Key is TrackedKey.Symbol sym && sym.Variable.Name == "x");

        Assert.NotNull(phiForX);
    }
}
