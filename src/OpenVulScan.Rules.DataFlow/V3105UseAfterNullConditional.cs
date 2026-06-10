using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3105", RuleSeverity.Level1, "CWE-690", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3105UseAfterNullConditional : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3105",
        "Use after null-conditional assignment",
        "The '{0}' variable was used after it was assigned through null-conditional operator. NullReferenceException is possible.",
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
        if (deref is { Code: "V3105" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location, deref.ReceiverName));
        }
    }
}
