using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3114 — an <see cref="System.IDisposable"/> local is created with <c>new</c> but is not
/// disposed before the method returns (full leak or partial dispose).
/// </summary>
/// <remarks>
/// Own-solve <see cref="AstRule"/> (precedent: V3151/V3142). The
/// <see cref="ResourceOwnershipLattice"/> makes the leak-dangerous <see cref="OwnershipState.Open"/>
/// state ⊤, so a partial dispose survives the merge and is reported. <c>using</c> variables are
/// excluded by <see cref="DisposeFlow"/> (they dispose by construction and their lowered
/// null-guarded finally would otherwise false-positive). Escaping resources are dropped.
/// </remarks>
[Rule("V3114", RuleSeverity.Level2, "CWE-404", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3114NotDisposedBeforeReturn : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3114",
        "IDisposable object is not disposed before method returns",
        "IDisposable object is created but not disposed on all paths before the method returns",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnMethodDeclaration(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        if (context.Node is not MethodDeclarationSyntax method)
            return;
        if (context.SemanticModel.GetOperation(context.Node, ct) is not IMethodBodyOperation body)
            return;

        var cfg = ControlFlowGraph.Create(body, ct);

        var owned = DisposeFlow.CollectOwnedLocals(cfg, method, context.SemanticModel, context.Compilation);
        if (owned.Count == 0)
            return;

        var tracked = owned.Keys.ToHashSet();
        var transfer = new ResourceOwnershipTransfer(tracked, context.Compilation);
        var solver = new WorklistSolver<ImmutableDictionary<TrackedKey, OwnershipState>>(
            new MapLattice<TrackedKey, ResourceOwnershipLattice, OwnershipState>(), transfer);

        var result = solver.Solve(cfg, ct);

        var exit = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Exit);
        var exitState = result.InStates[exit];

        foreach (var (key, location) in owned)
        {
            if (exitState.TryGetValue(key, out var ownership) && ownership == OwnershipState.Open)
                context.ReportDiagnostic(Diagnostic.Create(s_descriptor, location));
        }
    }
}
