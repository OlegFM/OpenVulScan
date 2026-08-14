using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests;

/// <summary>
/// Acceptance guard for ovs-2qi.11 / ADR-004: path-sensitivity is edge-condition
/// refinement, not path enumeration, so branch-heavy methods must converge in time
/// linear in the CFG — there is no split budget and no fallback mode to exercise.
/// </summary>
public class SolverStressTests
{
    [Fact]
    public void SeventySequentialBranches_IntervalPipeline_Converges()
    {
        // 2^70 paths if enumerated; linear for the flow-sensitive solver.
        var body = new StringBuilder();
        body.AppendLine("        int x = 0;");
        for (int i = 0; i < 70; i++)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"        if (n > {i}) {{ x = x + 1; }} else {{ x = x - 1; }}");
        }

        body.AppendLine("        return x;");

        var (cfg, model, _) = CfgTestHarness.Compile($@"
class C
{{
    int M(int n)
    {{
{body}
    }}
}}");
        var index = SsaBuilder.Build(cfg, model);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, IntervalValue>>(
            new MapLattice<SsaId, IntervalLattice, IntervalValue>(),
            new IntervalSsaTransfer(index),
            new IntervalSsaEdgeRefiner(index));

        var result = solver.Solve(cfg);

        Assert.True(result.Converged);
    }

    [Fact]
    public void SeventyNestedBranchesInsideLoop_IntervalPipeline_Converges()
    {
        // Nested ifs inside a widened loop: the worst structural mix we support.
        var body = new StringBuilder();
        body.AppendLine("        int x = 0;");
        body.AppendLine("        for (int i = 0; i < n; i = i + 1)");
        body.AppendLine("        {");
        for (int depth = 0; depth < 70; depth++)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"{new string(' ', 12 + depth)}if (n > {depth}) {{");
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"{new string(' ', 12 + 70)}x = x + 1;");
        for (int depth = 69; depth >= 0; depth--)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"{new string(' ', 12 + depth)}}}");
        }

        body.AppendLine("        }");
        body.AppendLine("        return x;");

        var (cfg, model, _) = CfgTestHarness.Compile($@"
class C
{{
    int M(int n)
    {{
{body}
    }}
}}");
        var index = SsaBuilder.Build(cfg, model);
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, IntervalValue>>(
            new MapLattice<SsaId, IntervalLattice, IntervalValue>(),
            new IntervalSsaTransfer(index),
            new IntervalSsaEdgeRefiner(index));

        var result = solver.Solve(cfg);

        Assert.True(result.Converged);
    }
}
