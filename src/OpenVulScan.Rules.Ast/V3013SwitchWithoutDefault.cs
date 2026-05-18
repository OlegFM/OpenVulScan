using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenVulScan;

[Rule("V3013", RuleSeverity.Level1, "CWE-691", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast)]
public sealed class V3013SwitchWithoutDefault : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3013",
        "Switch without default",
        "The 'switch' statement does not have a 'default' label",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnSwitchStatement(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Node is not SwitchStatementSyntax switchStatement)
        {
            return;
        }

        foreach (var section in switchStatement.Sections)
        {
            foreach (var label in section.Labels)
            {
                if (label is DefaultSwitchLabelSyntax)
                {
                    return;
                }
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(s_descriptor, switchStatement.SwitchKeyword.GetLocation()));
    }
}
