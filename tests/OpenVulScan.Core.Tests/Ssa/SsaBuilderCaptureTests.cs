using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

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

        var captureOp = cfg.Blocks
            .SelectMany(b => b.Operations)
            .SelectMany(EnumerateOps)
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
        var captures = cfg.Blocks
            .SelectMany(b => b.Operations)
            .SelectMany(EnumerateOps)
            .OfType<IFlowCaptureOperation>()
            .ToList();

        Assert.NotEmpty(captures);

        var captureRefs = cfg.Blocks
            .SelectMany(b => b.Operations.SelectMany(EnumerateOps)
                .Concat(b.BranchValue is not null ? EnumerateOps(b.BranchValue) : []))
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

    private static IEnumerable<IOperation> EnumerateOps(IOperation op)
    {
        yield return op;
        foreach (var c in op.ChildOperations)
        {
            if (c is null) continue;
            foreach (var d in EnumerateOps(c)) yield return d;
        }
    }
}
