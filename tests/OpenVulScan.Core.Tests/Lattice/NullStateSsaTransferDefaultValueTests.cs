using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests.Lattice;

/// <summary>
/// Direct unit tests for the <see cref="NullStateSsaTransfer"/> handling of
/// <see cref="IDefaultValueOperation"/> introduced in commit 5ed53ec.
/// The key branch is:
///   <c>IDefaultValueOperation dv when dv.Type is { IsReferenceType: true } =&gt; DefinitelyNull</c>
/// </summary>
public class NullStateSsaTransferDefaultValueTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IEnumerable<IOperation> AllOps(ControlFlowGraph cfg)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in block.Operations)
                foreach (var d in Descend(op))
                    yield return d;
            if (block.BranchValue is not null)
                foreach (var d in Descend(block.BranchValue))
                    yield return d;
        }

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    private static ImmutableDictionary<SsaId, NullState> RunTransfer(
        ControlFlowGraph cfg, SsaIndex index)
    {
        var transfer = new NullStateSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, NullState>.Empty;
        foreach (var block in cfg.Blocks)
            state = transfer.Apply(state, block);
        return state;
    }

    // ------------------------------------------------------------------
    // Test 4 – reference-type default: DefinitelyNull
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>string s = default;</c> should cause the SSA version of s to be
    /// <see cref="NullState.DefinitelyNull"/>.
    /// <para>
    /// Lowering note: the C# compiler may constant-fold <c>default(string)</c>
    /// to an <c>ILiteralOperation(null)</c> rather than emitting a raw
    /// <c>IDefaultValueOperation</c>.  We cover both paths: if a
    /// <c>IDefaultValueOperation</c> appears for s we assert DefinitelyNull
    /// directly; otherwise we assert DefinitelyNull through the null-literal
    /// path to ensure the end-to-end result is correct regardless of lowering.
    /// </para>
    /// </summary>
    [Fact]
    public void DefaultValue_ReferenceType_EvaluatesDefinitelyNull()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        string s = default;
        var n = s;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunTransfer(cfg, index);

        // Find the SSA version for s.
        var sSym = AllOps(cfg)
            .OfType<IVariableDeclaratorOperation>()
            .Select(d => d.Symbol)
            .Concat(AllOps(cfg).OfType<ILocalReferenceOperation>().Select(l => l.Local))
            .First(sym => sym.Name == "s");

        var sVersions = index.AllVersions(new TrackedKey.Symbol(sSym));
        Assert.NotEmpty(sVersions);

        // The initialising def version (version 0) must be DefinitelyNull because
        // default(string) is null regardless of whether Roslyn emits a
        // IDefaultValueOperation or a constant-folded null literal.
        Assert.Equal(NullState.DefinitelyNull, state[sVersions[0]]);
    }

    // ------------------------------------------------------------------
    // Test 4b – direct IDefaultValueOperation coverage via the ?. null arm
    // ------------------------------------------------------------------

    /// <summary>
    /// The Roslyn CFG always emits <see cref="IDefaultValueOperation"/> for
    /// the null arm of a <c>?.</c> operator (the "short-circuit" branch).
    /// This test targets that lowering directly to exercise the
    /// <c>IsReferenceType: true</c> guard in <c>NullStateSsaTransfer.Evaluate</c>.
    /// </summary>
    [Fact]
    public void DefaultValue_ConditionalAccessNullArm_EvaluatesDefinitelyNull()
    {
        // `a?.F` lowers to a conditional that, on the null branch, writes
        // IDefaultValueOperation(string) into a FlowCaptureOperation.
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    string F;
    void M(C? a)
    {
        var r = a?.F;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // Find an IFlowCaptureOperation whose Value is an IDefaultValueOperation
        // with a reference type — this is the null arm of the ?.
        var nullArmCapture = AllOps(cfg)
            .OfType<IFlowCaptureOperation>()
            .FirstOrDefault(c => c.Value is IDefaultValueOperation dv
                              && dv.Type is { IsReferenceType: true });

        Assert.NotNull(nullArmCapture);  // guard: the lowering must produce this

        var transfer = new NullStateSsaTransfer(index);
        var state = ImmutableDictionary<SsaId, NullState>.Empty;

        // Apply only up to (and including) the block that contains the null-arm capture.
        var nullArmBlock = cfg.Blocks.First(b =>
            b.Operations.SelectMany(Descend).Contains(nullArmCapture));

        // Process all blocks before the null-arm block in CFG order, then the block itself.
        foreach (var block in cfg.Blocks)
        {
            state = transfer.Apply(state, block);
            if (ReferenceEquals(block, nullArmBlock)) break;
        }

        var captureDef = index.DefinitionAt(nullArmCapture!);
        Assert.NotNull(captureDef);
        Assert.Equal(NullState.DefinitelyNull, state[captureDef!.Value]);

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    // ------------------------------------------------------------------
    // Test 5 – value-type default: Unknown
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>int i = default;</c> must NOT produce <see cref="NullState.DefinitelyNull"/>
    /// because int is a value type.  The IsReferenceType guard must be false.
    /// </summary>
    [Fact]
    public void DefaultValue_ValueType_StaysUnknown()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int i = default;
        var n = i;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunTransfer(cfg, index);

        // Retrieve the symbol for `i` via the semantic model (more reliable than
        // searching CFG ops, as value-type declarators may not appear as
        // IVariableDeclaratorOperation in every block depending on the CFG shape).
        var iSym = AllOps(cfg)
            .OfType<IVariableDeclaratorOperation>()
            .Select(d => d.Symbol)
            .Concat(AllOps(cfg).OfType<ILocalReferenceOperation>().Select(l => l.Local))
            .First(sym => sym.Name == "i");

        var iVersions = index.AllVersions(new TrackedKey.Symbol(iSym));
        Assert.NotEmpty(iVersions);

        // Value type default must not be tracked as null.
        Assert.Equal(NullState.Unknown, state[iVersions[0]]);
    }

    // ------------------------------------------------------------------
    // Test 6 – unconstrained generic default: Unknown
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>T t = default;</c> for an unconstrained generic parameter must stay
    /// <see cref="NullState.Unknown"/> because T may be either a reference type
    /// or a value type.  The IsReferenceType guard on IDefaultValueOperation
    /// must be false for an unconstrained type parameter.
    /// </summary>
    [Fact]
    public void DefaultValue_UnconstrainedGeneric_StaysUnknown()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M<T>()
    {
        T t = default;
        var u = t;
    }
}");
        var index = SsaBuilder.Build(cfg, model);
        var state = RunTransfer(cfg, index);

        var tSym = AllOps(cfg)
            .OfType<IVariableDeclaratorOperation>()
            .Select(d => d.Symbol)
            .Concat(AllOps(cfg).OfType<ILocalReferenceOperation>().Select(l => l.Local))
            .First(sym => sym.Name == "t");

        var tVersions = index.AllVersions(new TrackedKey.Symbol(tSym));
        Assert.NotEmpty(tVersions);

        // Unconstrained generic default must remain Unknown, not DefinitelyNull.
        Assert.Equal(NullState.Unknown, state[tVersions[0]]);
    }
}
