using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

[Rule("V3080", RuleSeverity.Level1, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3080PossibleNullDereference : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3080",
        "Possible null dereference",
        "Possible null dereference of '{0}'",
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
        if (deref is { Code: "V3080" })
        {
            context.ReportDiagnostic(Diagnostic.Create(s_descriptor, deref.Location, deref.ReceiverName));
        }
    }
}
