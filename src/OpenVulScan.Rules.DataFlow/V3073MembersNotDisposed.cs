using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3073 — a class implementing <see cref="System.IDisposable"/> has IDisposable instance fields
/// that its <c>Dispose</c> method does not dispose on all paths.
/// </summary>
/// <remarks>
/// Seeds every disposable field as <see cref="OwnershipState.Open"/> at the <c>Dispose</c> method's
/// entry, then reports any field still <see cref="OwnershipState.Open"/> at the Exit in-state. v1
/// handles the body's own <c>field.Dispose()</c> calls shallowly; the virtual
/// <c>Dispose(bool disposing)</c> / <c>base.Dispose()</c> pattern is a documented follow-up.
/// </remarks>
[Rule("V3073", RuleSeverity.Level0, "CWE-404", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3073MembersNotDisposed : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3073",
        "Not all IDisposable members are disposed in Dispose",
        "IDisposable member '{0}' is not disposed on all paths in the Dispose method",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnMethodDeclaration(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        if (context.Node is not MethodDeclarationSyntax method)
            return;
        if (method.Identifier.Text != "Dispose")
            return;
        if (context.SemanticModel.GetDeclaredSymbol(method, ct) is not { } methodSymbol)
            return;
        if (methodSymbol.ContainingType is not { } classSymbol
            || !DisposeFlow.ImplementsDisposable(classSymbol, context.Compilation))
            return;
        if (context.SemanticModel.GetOperation(context.Node, ct) is not IMethodBodyOperation body)
            return;

        var fields = DisposeFlow.CollectDisposableFields(classSymbol, context.Compilation);
        if (fields.Count == 0)
            return;

        var cfg = ControlFlowGraph.Create(body, ct);

        var fieldKeys = fields.Select(f => (TrackedKey)new TrackedKey.InstanceField(f)).ToHashSet();
        var seed = ImmutableDictionary.CreateRange(
            fields.Select(f => new KeyValuePair<TrackedKey, OwnershipState>(
                new TrackedKey.InstanceField(f), OwnershipState.Open)));

        var transfer = new ResourceOwnershipTransfer(fieldKeys, context.Compilation);
        var solver = new WorklistSolver<ImmutableDictionary<TrackedKey, OwnershipState>>(
            new MapLattice<TrackedKey, ResourceOwnershipLattice, OwnershipState>(), transfer);

        var result = solver.Solve(cfg, seed, ct);

        var exit = cfg.Blocks.FirstOrDefault(b => b.Kind == BasicBlockKind.Exit);
        if (exit is null)
            return;

        var exitState = result.InStates[exit];

        foreach (var field in fields)
        {
            var key = (TrackedKey)new TrackedKey.InstanceField(field);
            if (exitState.TryGetValue(key, out var ownership) && ownership == OwnershipState.Open)
            {
                var location = field.Locations.FirstOrDefault() ?? method.Identifier.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(s_descriptor, location, field.Name));
            }
        }
    }
}
