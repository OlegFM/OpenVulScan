using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3142 — unreachable code detected.
/// </summary>
/// <remarks>
/// <para>
/// Implemented as an <see cref="AstRule"/> (a once-per-method hook) rather than a
/// <see cref="DataFlowRule{T}"/> because reachability needs no lattice: Roslyn already
/// computes <see cref="BasicBlock.IsReachable"/> over the control-flow graph, folding
/// constant conditions (<c>if (false)</c>), code after <c>return</c>/<c>throw</c>, and
/// code after a non-terminating loop (<c>while (true)</c>).
/// </para>
/// <para>
/// It lives in Rules.DataFlow next to its constant-condition cousins V3022/V3063: the
/// base class is <see cref="AstRule"/> purely as the per-method entry point.
/// </para>
/// </remarks>
[Rule("V3142", RuleSeverity.Level2, "CWE-561", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3142UnreachableCode : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3142",
        "Unreachable code",
        "Unreachable code detected",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnMethodDeclaration(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.CancellationToken;
        var operation = context.SemanticModel.GetOperation(context.Node, cancellationToken);
        if (operation is not IMethodBodyOperation methodBody)
        {
            return;
        }

        var cfg = ControlFlowGraph.Create(methodBody, cancellationToken);

        // Collect every source statement that owns at least one operation living in an
        // unreachable basic block.
        var unreachable = new HashSet<StatementSyntax>();
        foreach (var block in cfg.Blocks)
        {
            if (block.IsReachable)
            {
                continue;
            }

            foreach (var op in block.Operations)
            {
                AddEnclosingStatement(op, unreachable);
            }

            if (block.BranchValue is not null)
            {
                AddEnclosingStatement(block.BranchValue, unreachable);
            }
        }

        // Report once per contiguous dead region: a statement is the region head when it
        // is neither nested inside another dead statement nor preceded by a dead sibling.
        foreach (var statement in unreachable)
        {
            if (IsRegionHead(statement, unreachable))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_descriptor, statement.GetLocation()));
            }
        }
    }

    private static void AddEnclosingStatement(IOperation operation, HashSet<StatementSyntax> set)
    {
        var statement = operation.Syntax.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is not null)
        {
            set.Add(statement);
        }
    }

    private static bool IsRegionHead(StatementSyntax statement, HashSet<StatementSyntax> unreachable)
    {
        // Nested inside a larger dead statement → not a head.
        foreach (var ancestor in statement.Ancestors())
        {
            if (ancestor is StatementSyntax ancestorStatement && unreachable.Contains(ancestorStatement))
            {
                return false;
            }
        }

        // Preceded by a dead sibling → mid-region, not a head.
        var previous = PreviousStatement(statement);
        return previous is null || !unreachable.Contains(previous);
    }

    private static StatementSyntax? PreviousStatement(StatementSyntax statement)
    {
        var siblings = statement.Parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default,
        };

        if (siblings.Count == 0)
        {
            return null;
        }

        var index = siblings.IndexOf(statement);
        return index > 0 ? siblings[index - 1] : null;
    }
}
