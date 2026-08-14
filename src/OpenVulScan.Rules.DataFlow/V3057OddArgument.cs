using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3057: a function receives an odd argument — v1 scope: a value that is ALWAYS negative
/// passed where only non-negative values make sense.
/// </summary>
/// <remarks>
/// Runs on the interval SSA pipeline. Definite-only: reports when the argument interval's
/// upper bound is below zero (every possible value is negative); unknown (⊤) stays silent.
/// Covered sinks: <c>string.Substring</c>/<c>Remove</c>/<c>PadLeft</c>/<c>PadRight</c>
/// integer arguments and rank-1 array creation sizes. Broader API coverage and the MAY
/// variant are follow-ups.
/// </remarks>
[Rule("V3057", RuleSeverity.Level2, "CWE-628", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3057OddArgument : DataFlowRule<ImmutableDictionary<SsaId, IntervalValue>>
{
    private static readonly FrozenSet<string> s_checkedStringMethods =
        new[] { "Substring", "Remove", "PadLeft", "PadRight" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3057",
        "Function receives an odd argument",
        "'{0}' receives a definitely negative value {1}",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ILattice<ImmutableDictionary<SsaId, IntervalValue>> Lattice { get; }
        = new MapLattice<SsaId, IntervalLattice, IntervalValue>();

    public override ITransfer<ImmutableDictionary<SsaId, IntervalValue>> CreateTransfer(SsaIndex ssaIndex)
        => new IntervalSsaTransfer(ssaIndex);

    public override IEdgeRefiner<ImmutableDictionary<SsaId, IntervalValue>>? CreateEdgeRefiner(SsaIndex ssaIndex)
        => new IntervalSsaEdgeRefiner(ssaIndex);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, IntervalValue> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        switch (operation)
        {
            case IInvocationOperation { Instance: not null, TargetMethod: { } method } invocation
                when method.ContainingType?.SpecialType == SpecialType.System_String
                     && s_checkedStringMethods.Contains(method.Name):
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter?.Type.SpecialType != SpecialType.System_Int32)
                    {
                        continue;
                    }

                    Report(argument.Value, method.Name, state, context);
                }

                break;

            case IArrayCreationOperation { DimensionSizes.Length: 1 } creation:
                Report(creation.DimensionSizes[0], "array size", state, context);
                break;

            default:
                break;
        }
    }

    private static void Report(
        IOperation valueOperation,
        string sinkName,
        ImmutableDictionary<SsaId, IntervalValue> state,
        DataFlowContext context)
    {
        var interval = IntervalSsaEvaluator.Evaluate(valueOperation, state, context.SsaIndex);
        if (interval.IsEmpty || interval.Upper >= 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_descriptor,
            valueOperation.Syntax.GetLocation(),
            sinkName,
            interval.ToString()));
    }
}
