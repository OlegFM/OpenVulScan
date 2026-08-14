using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3106: array element access whose index is ALWAYS outside the array bounds.
/// </summary>
/// <remarks>
/// Intra-method, definite-only (v1): reports when the index interval lies entirely at or
/// above the array-length interval's upper bound, or entirely below zero. Array lengths are
/// tracked by <see cref="IntervalSsaEvaluator"/>'s convention that an array-typed definition
/// carries its LENGTH interval. The MAY variant ("possibly out of bound", the PVS wording)
/// is a follow-up once corpus false-positive rates are measurable.
/// </remarks>
[Rule("V3106", RuleSeverity.Level1, "CWE-125", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3106IndexOutOfBounds : DataFlowRule<ImmutableDictionary<SsaId, IntervalValue>>
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3106",
        "Index is out of bound",
        "Index {0} is always outside the bounds of the array",
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

        if (operation is not IArrayElementReferenceOperation { Indices.Length: 1 } elementRef)
        {
            return;
        }

        var index = IntervalSsaEvaluator.Evaluate(elementRef.Indices[0], state, context.SsaIndex);
        if (index.IsEmpty)
        {
            return; // Unreachable path — no concrete execution performs this access.
        }

        // Array-typed defs carry the array's LENGTH interval (IntervalSsaEvaluator convention).
        var length = IntervalSsaEvaluator.Evaluate(elementRef.ArrayReference, state, context.SsaIndex);

        bool alwaysNegative = index.Upper < 0;
        bool alwaysPastEnd = !length.IsEmpty
            && !length.UpperIsInfinite
            && !index.LowerIsInfinite
            && index.Lower >= length.Upper;

        if (alwaysNegative || alwaysPastEnd)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                operation.Syntax.GetLocation(),
                index.ToString()));
        }
    }
}
