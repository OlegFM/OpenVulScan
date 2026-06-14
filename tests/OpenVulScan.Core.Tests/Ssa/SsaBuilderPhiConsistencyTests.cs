using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;
using static OpenVulScan.Tests.Ssa.CfgTestHarness;

namespace OpenVulScan.Tests.Ssa;

/// <summary>
/// Pins the S-4 structural invariant asserted in <c>SsaBuilder.AssertPhiInvariants</c>:
/// every φ joins versions of the same variable, so each operand's key equals the
/// φ-result's key. The Debug.Assert is compiled out of the Release test build, so these
/// tests re-check the same property through the public <see cref="SsaIndex"/> API.
/// </summary>
public class SsaBuilderPhiConsistencyTests
{
    private static List<Phi> AllPhis(ControlFlowGraph cfg, SsaIndex index)
        => cfg.Blocks.SelectMany(index.PhisAt).ToList();

    [Fact]
    public void BranchJoin_PhiOperandsShareResultKey()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(bool c)
    {
        int x;
        if (c) { x = 1; } else { x = 2; }
        var y = x;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var phis = AllPhis(cfg, index);
        Assert.NotEmpty(phis); // x is joined at the merge block

        foreach (var phi in phis)
        {
            foreach (var operand in phi.Operands)
            {
                Assert.Equal(phi.Result.Key, operand.Version.Key);
            }
        }
    }

    [Fact]
    public void CaptureJoin_PhiOperandsShareResultKey()
    {
        // `a ?? b` assigns the SAME capture id in both arms (S-2 amendment), so the
        // capture is a multi-def global with a φ at the join. Its operands must agree
        // on the key with the result.
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M(string a, string b)
    {
        var s = a ?? b;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        var phis = AllPhis(cfg, index);
        Assert.NotEmpty(phis);

        foreach (var phi in phis)
        {
            foreach (var operand in phi.Operands)
            {
                Assert.Equal(phi.Result.Key, operand.Version.Key);
            }
        }
    }
}
