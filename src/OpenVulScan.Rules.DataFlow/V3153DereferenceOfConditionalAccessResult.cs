using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3153", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3153DereferenceOfConditionalAccessResult : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3153",
        "Dereference of null-conditional access result",
        "Dereferencing the result of null-conditional access operator can lead to NullReferenceException",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var deref = NullDerefClassifier.Classify(operation, state, context.SsaIndex);
        if (deref is { Code: "V3153" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location));
        }
    }
}
