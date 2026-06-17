using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3178 — a member is invoked, a property accessed, or <c>Dispose</c> called again on an
/// object that is already disposed on at least one reaching path (MAY semantics, matching
/// PVS's "potentially disposed").
/// </summary>
/// <remarks>
/// Runs <see cref="DisposeStateTransfer"/> to a fixpoint via <see cref="WorklistSolver{T}"/>,
/// then replays each block from its in-state, checking <em>before</em> applying each
/// operation whether it uses a symbol whose state is ⊒ <see cref="DisposeState.Disposed"/>.
/// The first <c>Dispose()</c> (Live → Disposed) is not a use; a second <c>Dispose()</c>
/// reads an already-Disposed receiver and is reported as a re-dispose.
/// MAY semantics may over-report on branchy flow; accepted per the PVS-faithful choice.
/// </remarks>
[Rule("V3178", RuleSeverity.Level1, "CWE-672", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3178UseOfDisposedObject : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3178",
        "Calling a method or accessing a member of a potentially disposed object",
        "Object may already be disposed at this point",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnMethodDeclaration(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        if (context.SemanticModel.GetOperation(context.Node, ct) is not IMethodBodyOperation body)
            return;

        var cfg = ControlFlowGraph.Create(body, ct);
        var transfer = new DisposeStateTransfer();
        var solver = new WorklistSolver<ImmutableDictionary<TrackedKey, DisposeState>>(
            new MapLattice<TrackedKey, DisposeLattice, DisposeState>(), transfer);

        var result = solver.Solve(cfg, ct);

        foreach (var block in cfg.Blocks)
        {
            var state = result.InStates[block];
            foreach (var op in OperationTree.Enumerate(block))
            {
                if (TryGetUsedResource(op) is { } key
                    && state.TryGetValue(key, out var disposeState)
                    && disposeState != DisposeState.Live)
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_descriptor, op.Syntax.GetLocation()));
                }

                state = transfer.Apply(state, op);
            }
        }
    }

    /// <summary>
    /// Returns the resource used as the receiver of <paramref name="op"/> (invocation,
    /// property reference, or field reference), or <see langword="null"/>. Conversions and
    /// parentheses on the receiver are unwrapped, so the lowered
    /// <c>((IDisposable)x).Dispose()</c> form is recognised for the double-dispose case.
    /// </summary>
    private static TrackedKey.Symbol? TryGetUsedResource(IOperation op) => op switch
    {
        IInvocationOperation { Instance: { } instance } => ResolveResourceKey(instance),
        IPropertyReferenceOperation { Instance: { } instance } => ResolveResourceKey(instance),
        IFieldReferenceOperation { Instance: ILocalReferenceOperation or IParameterReferenceOperation } f =>
            ResolveResourceKey(f.Instance!),
        _ => null,
    };

    private static TrackedKey.Symbol? ResolveResourceKey(IOperation instance) => Unwrap(instance) switch
    {
        ILocalReferenceOperation l => new TrackedKey.Symbol(l.Local),
        IParameterReferenceOperation p => new TrackedKey.Symbol(p.Parameter),
        _ => null,
    };

    private static IOperation Unwrap(IOperation op) => op switch
    {
        IConversionOperation c => Unwrap(c.Operand),
        IParenthesizedOperation p => Unwrap(p.Operand),
        _ => op,
    };
}
