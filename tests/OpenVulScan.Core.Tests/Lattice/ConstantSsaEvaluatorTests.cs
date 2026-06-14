using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using OpenVulScan.Tests.Ssa;
using Xunit;

namespace OpenVulScan.Tests.Lattice;

/// <summary>
/// Smoke tests for <see cref="ConstantSsaEvaluator"/>: every operation in a method
/// is evaluated against the running constant state and must never let an exception
/// escape. The evaluator's dispatch tables throw <see cref="NotSupportedException"/>
/// for type combinations they do not model (and the runtime throws
/// <see cref="DivideByZeroException"/> / <see cref="InvalidCastException"/> /
/// <see cref="OverflowException"/>); all of these must be swallowed and degrade to ⊥,
/// not crash the analysis. Mixed-type operands are the interesting cases because
/// <c>Unwrap</c> strips conversions, so the dispatch sees differently-typed boxes.
/// </summary>
public class ConstantSsaEvaluatorTests
{
    [Theory]
    // Mixed integer widths: conversions are stripped, so the dispatch sees int vs long.
    [InlineData("int x = 5; long y = 3L; if ((x - y) > 0) { }")]
    [InlineData("int x = 5; long y = 3L; if (x < y) { }")]
    [InlineData("int x = 5; long y = 3L; if ((x * y) == 0) { }")]
    // Mixed integer / floating point.
    [InlineData("int x = 5; double d = 2.0; if ((x * d) > 0.0) { }")]
    [InlineData("int x = 5; decimal m = 2m; if (x < m) { }")]
    // Division / modulo by a tracked zero constant.
    [InlineData("int z = 0; if ((1 / z) == 0) { }")]
    [InlineData("long z = 0L; if ((10L / z) == 0L) { }")]
    // Char and string arithmetic flowing into comparisons.
    [InlineData("char c = 'a'; if (c == 'b') { }")]
    [InlineData("char c = 'a'; if ((c + 1) > 0) { }")]
    [InlineData("string s = \"a\"; if ((s + \"b\") == \"ab\") { }")]
    // Narrow integer bitwise (promoted to int, folds without throwing).
    [InlineData("short s = 1; if ((s | 2) != 0) { }")]
    [InlineData("byte b = 1; if ((b & 3) == 1) { }")]
    // Unsigned / long bitwise and xor.
    [InlineData("uint u = 5u; if ((u ^ 3u) == 6u) { }")]
    [InlineData("ulong u = 5UL; if ((u & 3UL) == 1UL) { }")]
    // Unary operators on exotic types.
    [InlineData("decimal m = 5m; if (-m < 0m) { }")]
    [InlineData("long l = 5L; if (~l < 0L) { }")]
    [InlineData("bool b = true; if ((b & false) == false) { }")]
    // Enum and nullable references that fall through to ⊤ rather than folding.
    [InlineData("E e = E.A; if (e == E.B) { }")]
    [InlineData("int? n = 5; if (n == 5) { }")]
    public void EvaluatingEveryOperation_NeverThrows(string body)
    {
        var snippet = $@"
enum E {{ A, B }}
class C
{{
    void M()
    {{
        {body}
    }}
}}";
        var exception = Record.Exception(() => EvaluateAllOperations(snippet));
        Assert.Null(exception);
    }

    private static void EvaluateAllOperations(string snippet)
    {
        var (cfg, model, _) = CfgTestHarness.Compile(snippet);
        var index = SsaBuilder.Build(cfg, model);
        var transfer = new ConstantSsaTransfer(index);
        var lattice = new MapLattice<SsaId, ConstantLattice, ConstantLatticeValue>();
        var solver = new WorklistSolver<ImmutableDictionary<SsaId, ConstantLatticeValue>>(lattice, transfer);
        var result = solver.Solve(cfg, CancellationToken.None);

        foreach (var block in cfg.Blocks)
        {
            var state = transfer.ApplyPhis(result.InStates[block], block);
            foreach (var op in OperationTree.Enumerate(block))
            {
                _ = ConstantSsaEvaluator.Evaluate(op, state, index);
                state = transfer.Apply(state, op);
            }
        }
    }
}
