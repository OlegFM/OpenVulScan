using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// Computes per-method return nullability for a whole compilation: CHA→RTA call graph,
/// bottom-up SCC traversal, and per method a NullState SSA solve whose invocation sites
/// consult the summaries computed so far (recursion converges via the scheduler's
/// fixed-point iteration). Context-insensitive (k=0) — the summary is one state per
/// method, joined over all return sites.
/// </summary>
public static class NullabilitySummaryProvider
{
    private static readonly MapLattice<SsaId, NullStateLattice, NullState> s_mapLattice = new();
    private static readonly NullStateLattice s_nullLattice = new();

    public static INullabilitySummaryLookup Compute(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var graph = RtaRefiner.Refine(ChaBuilder.Build(compilation, cancellationToken));
        var summaries = BottomUpSummaryScheduler.Run(
            graph,
            (method, current) => Extract(compilation, method, current, cancellationToken),
            cancellationToken);

        var returnStates = new Dictionary<IMethodSymbol, NullState>(SymbolEqualityComparer.Default);
        foreach (var (method, summary) in summaries)
        {
            returnStates[method.OriginalDefinition] = summary.ReturnNullability;
        }

        return new DictionaryLookup(returnStates);
    }

    private static MethodSummary Extract(
        Compilation compilation,
        IMethodSymbol method,
        IReadOnlyDictionary<IMethodSymbol, MethodSummary> current,
        CancellationToken cancellationToken)
    {
        var methodId = method.GetDocumentationCommentId() ?? method.ToDisplayString();
        var returnState = ComputeReturnState(compilation, method, current, cancellationToken);
        return new MethodSummary(methodId, returnState, [], [], IsPure: false, []);
    }

    private static NullState ComputeReturnState(
        Compilation compilation,
        IMethodSymbol method,
        IReadOnlyDictionary<IMethodSymbol, MethodSummary> current,
        CancellationToken cancellationToken)
    {
        if (method.ReturnsVoid)
        {
            return NullState.Unknown;
        }

        if (method.ReturnType is { IsValueType: true } valueType
            && valueType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            return NullState.NotNull;
        }

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax syntax)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            if (model.GetOperation(syntax, cancellationToken) is not IMethodBodyOperation body)
            {
                continue;
            }

            var cfg = ControlFlowGraph.Create(body, cancellationToken);
            var ssa = SsaBuilder.Build(cfg, model);
            var transfer = new NullStateSsaTransfer(ssa, new SchedulerLookup(current));
            var solver = new WorklistSolver<ImmutableDictionary<SsaId, NullState>>(
                s_mapLattice, transfer, new NullStateSsaEdgeRefiner(ssa));
            var result = solver.Solve(cfg, cancellationToken);

            var joined = NullState.Unknown;
            var any = false;
            foreach (var block in cfg.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (block.FallThroughSuccessor?.Semantics != ControlFlowBranchSemantics.Return
                    || block.BranchValue is not { } returned)
                {
                    continue;
                }

                // March the block's in-state through its operations (branch value included)
                // so captures feeding the return expression carry their states.
                var state = transfer.ApplyPhis(result.InStates[block], block);
                foreach (var op in OperationTree.Enumerate(block))
                {
                    state = transfer.Apply(state, op);
                }

                var valueState = transfer.EvaluateValue(returned, state);
                joined = any ? s_nullLattice.Join(joined, valueState) : valueState;
                any = true;
            }

            return any ? joined : NullState.Unknown;
        }

        return NullState.Unknown;
    }

    private sealed class SchedulerLookup : INullabilitySummaryLookup
    {
        private readonly IReadOnlyDictionary<IMethodSymbol, MethodSummary> _current;

        public SchedulerLookup(IReadOnlyDictionary<IMethodSymbol, MethodSummary> current) => _current = current;

        public NullState ReturnStateOf(IMethodSymbol method)
            => _current.TryGetValue(method.OriginalDefinition, out var summary)
                ? summary.ReturnNullability
                : NullState.Unknown;
    }

    private sealed class DictionaryLookup : INullabilitySummaryLookup
    {
        private readonly Dictionary<IMethodSymbol, NullState> _states;

        public DictionaryLookup(Dictionary<IMethodSymbol, NullState> states) => _states = states;

        public NullState ReturnStateOf(IMethodSymbol method)
            => _states.TryGetValue(method.OriginalDefinition, out var state)
                ? state
                : NullState.Unknown;
    }
}
