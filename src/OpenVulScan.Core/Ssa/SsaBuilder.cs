using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public static class SsaBuilder
{
    public static SsaIndex Build(ControlFlowGraph cfg, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(model);

        // --------- Pass 0: build l-value capture aliases ---------
        // Roslyn emits IFlowCaptureOperation(id, LocalRef(x)) when x is used as
        // an l-value target in an expression like `x = a?.F`.  The subsequent
        // SimpleAssignmentOperation then targets IFlowCaptureReferenceOperation(id)
        // rather than LocalReferenceOperation(x).  We build a map capture-id →
        // TrackedKey so that TryGetDefinitionKey can recognise these indirect defs.
        var captureToLocal = BuildCaptureToLocalMap(cfg);

        // --------- Pass 1: collect def-sites and globals ---------
        var defSites = new Dictionary<TrackedKey, int>();

        foreach (var block in cfg.Blocks)
        {
            foreach (var op in EnumerateBlockOps(block))
            {
                var key = TryGetDefinitionKey(op, captureToLocal);
                if (key is null) continue;
                defSites[key] = defSites.GetValueOrDefault(key, 0) + 1;
            }
        }

        // Globals: keys with >=2 def-sites (semi-pruned criterion).
        var globals = new HashSet<TrackedKey>(defSites.Where(kv => kv.Value >= 2).Select(kv => kv.Key));

        // --------- Pass 2: versioning + phi placement ---------
        var definitions = ImmutableDictionary.CreateBuilder<IOperation, SsaId>();
        var uses = ImmutableDictionary.CreateBuilder<(IOperation, TrackedKey), SsaId>();
        var entryVersions = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>>();
        var phisBuilder = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableArray<Phi>>();
        var allVersions = new Dictionary<TrackedKey, List<SsaId>>();

        var nextVersion = new Dictionary<TrackedKey, int>();
        SsaId NewVersion(TrackedKey key)
        {
            if (!nextVersion.TryGetValue(key, out var v)) v = 0;
            nextVersion[key] = v + 1;
            var id = new SsaId(key, v);
            if (!allVersions.TryGetValue(key, out var list))
            {
                list = [];
                allVersions[key] = list;
            }
            list.Add(id);
            return id;
        }

        var blockOut = new Dictionary<BasicBlock, Dictionary<TrackedKey, SsaId>>();
        var phisToBind = new List<(BasicBlock Block, TrackedKey Key, SsaId Result)>();

        var entryBlock = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Entry);
        var current = new Dictionary<TrackedKey, SsaId>();

        // Pass 0: define parameters at entry block, version 0.
        var methodSymbol = TryGetMethodSymbol(model);
        if (methodSymbol is not null)
        {
            foreach (var p in methodSymbol.Parameters)
            {
                var key = new TrackedKey.Symbol(p);
                var id = NewVersion(key);
                current[key] = id;
            }
        }

        entryVersions[entryBlock] = ImmutableDictionary.CreateRange(current);
        blockOut[entryBlock] = current;

        foreach (var block in cfg.Blocks)
        {
            if (block.Kind == BasicBlockKind.Entry) continue;

            current = [];
            var blockPhis = ImmutableArray.CreateBuilder<Phi>();

            if (block.Predecessors.Length >= 2)
            {
                foreach (var key in globals)
                {
                    var result = NewVersion(key);
                    current[key] = result;
                    blockPhis.Add(new Phi(result, []));
                    phisToBind.Add((block, key, result));
                }
            }
            else if (block.Predecessors.Length == 1
                     && blockOut.TryGetValue(block.Predecessors[0].Source, out var predOut))
            {
                current = new Dictionary<TrackedKey, SsaId>(predOut);
            }

            entryVersions[block] = ImmutableDictionary.CreateRange(current);
            if (blockPhis.Count > 0)
            {
                phisBuilder[block] = blockPhis.ToImmutable();
            }

            foreach (var op in TopLevelBlockOps(block))
            {
                Walk(op, current, NewVersion, definitions, uses, captureToLocal);
            }

            blockOut[block] = current;
        }

        // --------- Pass 3: bind phi operands ---------
        if (phisToBind.Count > 0)
        {
            var grouped = phisToBind.GroupBy(t => t.Block);
            foreach (var group in grouped)
            {
                var block = group.Key;
                var bound = ImmutableArray.CreateBuilder<Phi>();
                foreach (var (_, key, result) in group)
                {
                    var operands = ImmutableArray.CreateBuilder<PhiOperand>();
                    foreach (var predBranch in block.Predecessors)
                    {
                        var predBlock = predBranch.Source;
                        if (blockOut.TryGetValue(predBlock, out var predOut)
                            && predOut.TryGetValue(key, out var predVersion))
                        {
                            operands.Add(new PhiOperand(predBlock, predVersion));
                        }
                    }
                    bound.Add(new Phi(result, operands.ToImmutable()));
                }
                phisBuilder[block] = bound.ToImmutable();
            }
        }

        var allVersionsImmutable = allVersions.ToImmutableDictionary(
            kv => kv.Key,
            kv => kv.Value.ToImmutableArray());

        return new SsaIndex(
            definitions.ToImmutable(),
            uses.ToImmutable(),
            entryVersions.ToImmutable(),
            phisBuilder.ToImmutable(),
            allVersionsImmutable);
    }

    // --- helpers ---

    /// <summary>
    /// Scans the CFG for l-value captures: <c>FlowCaptureOperation(id, LocalRef(x))</c>
    /// or <c>FlowCaptureOperation(id, ParameterRef(p))</c>.  Returns a map from capture
    /// ID to the <see cref="TrackedKey"/> of the aliased local or parameter.
    /// </summary>
    private static Dictionary<CaptureId, TrackedKey> BuildCaptureToLocalMap(ControlFlowGraph cfg)
    {
        var map = new Dictionary<CaptureId, TrackedKey>();
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in EnumerateBlockOps(block))
            {
                if (op is IFlowCaptureOperation { Value: ILocalReferenceOperation lref } fc1)
                {
                    map[fc1.Id] = new TrackedKey.Symbol(lref.Local);
                }
                else if (op is IFlowCaptureOperation { Value: IParameterReferenceOperation pref } fc2)
                {
                    map[fc2.Id] = new TrackedKey.Symbol(pref.Parameter);
                }
            }
        }
        return map;
    }

    private static IMethodSymbol? TryGetMethodSymbol(SemanticModel model)
    {
        // Same heuristic used in Task 6: pick the first method declaration in the syntax tree.
        // Snippets used by the test harness contain a single method; this is sufficient for now.
        var methodSyntax = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        return methodSyntax is null ? null : model.GetDeclaredSymbol(methodSyntax) as IMethodSymbol;
    }

    private static TrackedKey? TryGetDefinitionKey(
        IOperation op,
        IReadOnlyDictionary<CaptureId, TrackedKey>? captureToLocal = null)
    {
        switch (op)
        {
            case IVariableDeclaratorOperation { Symbol: ILocalSymbol local }:
                return new TrackedKey.Symbol(local);
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation lref }:
                return new TrackedKey.Symbol(lref.Local);
            case ISimpleAssignmentOperation { Target: IParameterReferenceOperation pref }:
                return new TrackedKey.Symbol(pref.Parameter);
            case ISimpleAssignmentOperation
            {
                Target: IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field }
            }:
                return new TrackedKey.InstanceField(field);
            // l-value capture target: `FlowCaptureRef(id) = value` where id is an alias for a local/param.
            case ISimpleAssignmentOperation { Target: IFlowCaptureReferenceOperation capRef }
                when captureToLocal is not null && captureToLocal.TryGetValue(capRef.Id, out var aliasKey):
                return aliasKey;
            case ICompoundAssignmentOperation { Target: ILocalReferenceOperation lref }:
                return new TrackedKey.Symbol(lref.Local);
            case ICompoundAssignmentOperation { Target: IParameterReferenceOperation pref }:
                return new TrackedKey.Symbol(pref.Parameter);
            case ICompoundAssignmentOperation
            {
                Target: IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field2 }
            }:
                return new TrackedKey.InstanceField(field2);
            case IIncrementOrDecrementOperation { Target: ILocalReferenceOperation lref }:
                return new TrackedKey.Symbol(lref.Local);
            case IIncrementOrDecrementOperation { Target: IParameterReferenceOperation pref }:
                return new TrackedKey.Symbol(pref.Parameter);
            // Flow captures are ordinary tracked defs: multi-def captures (both arms of ?? / ?:) need
            // distinct versions so phi placement can join them.
            case IFlowCaptureOperation capture:
                return new TrackedKey.Capture(capture.Id);
            default:
                return null;
        }
    }

    private static void RegisterDef(
        IOperation op,
        TrackedKey key,
        Dictionary<TrackedKey, SsaId> current,
        Func<TrackedKey, SsaId> newVersion,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions)
    {
        var id = newVersion(key);
        current[key] = id;
        definitions[op] = id;
    }

    private static IEnumerable<IOperation> TopLevelBlockOps(BasicBlock block)
    {
        foreach (var op in block.Operations)
            yield return op;
        if (block.BranchValue is not null)
            yield return block.BranchValue;
    }

    private static void Walk(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        Func<TrackedKey, SsaId> newVersion,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses,
        IReadOnlyDictionary<CaptureId, TrackedKey>? captureToLocal = null)
    {
        // Tracked defs: walk children first (RHS reads bind pre-def versions and
        // RHS kills happen before the def), then register the def. This matches
        // C# evaluation order: the right-hand side completes before the write.
        // IFlowCaptureOperation is handled here too (via TryGetDefinitionKey).
        var defKey = TryGetDefinitionKey(op, captureToLocal);
        if (defKey is not null)
        {
            WalkChildren(op, current, newVersion, definitions, uses, captureToLocal);
            RegisterDef(op, defKey, current, newVersion, definitions);
            return;
        }

        // this-accessing invocations: argument reads bind pre-kill versions,
        // then all tracked instance fields are killed (callee may mutate them).
        if (IsThisAccessingInvocation(op))
        {
            WalkChildren(op, current, newVersion, definitions, uses, captureToLocal);
            var fieldKeysSnapshot = current.Keys.OfType<TrackedKey.InstanceField>().ToList();
            foreach (var key in fieldKeysSnapshot)
            {
                var id = newVersion(key);
                current[key] = id;
            }
            return;
        }

        RecordUse(op, current, uses, captureToLocal);
        WalkChildren(op, current, newVersion, definitions, uses, captureToLocal);
    }

    private static void WalkChildren(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        Func<TrackedKey, SsaId> newVersion,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses,
        IReadOnlyDictionary<CaptureId, TrackedKey>? captureToLocal = null)
    {
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            Walk(child, current, newVersion, definitions, uses, captureToLocal);
        }
    }

    private static void RecordUse(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses,
        IReadOnlyDictionary<CaptureId, TrackedKey>? captureToLocal = null)
    {
        switch (op)
        {
            case ILocalReferenceOperation lref:
            {
                // Skip: this lref is the assignment target, not a read.
                if (lref.Parent is ISimpleAssignmentOperation { Target: var t1 } && ReferenceEquals(t1, lref)) break;
                if (lref.Parent is ICompoundAssignmentOperation { Target: var t2 } && ReferenceEquals(t2, lref)) break;
                if (lref.Parent is IIncrementOrDecrementOperation { Target: var t3 } && ReferenceEquals(t3, lref)) break;
                var key = new TrackedKey.Symbol(lref.Local);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IParameterReferenceOperation pref:
            {
                // Skip: this pref is the assignment target, not a read.
                if (pref.Parent is ISimpleAssignmentOperation { Target: var t1 } && ReferenceEquals(t1, pref)) break;
                if (pref.Parent is ICompoundAssignmentOperation { Target: var t2 } && ReferenceEquals(t2, pref)) break;
                if (pref.Parent is IIncrementOrDecrementOperation { Target: var t3 } && ReferenceEquals(t3, pref)) break;
                var key = new TrackedKey.Symbol(pref.Parameter);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IFieldReferenceOperation { Instance: IInstanceReferenceOperation, Field: var field } fref:
            {
                // Skip: this fref is the assignment target, not a read.
                if (fref.Parent is ISimpleAssignmentOperation { Target: var t1 } && ReferenceEquals(t1, fref)) break;
                if (fref.Parent is ICompoundAssignmentOperation { Target: var t2 } && ReferenceEquals(t2, fref)) break;
                var key = new TrackedKey.InstanceField(field);
                if (current.TryGetValue(key, out var id))
                    uses[(op, key)] = id;
                break;
            }
            case IFlowCaptureReferenceOperation flowRef:
            {
                // Skip: this flow-capture reference is an l-value assignment target
                // (e.g. `FlowCaptureRef(x_lvalue) = a?.F`).  The real write is tracked
                // against the aliased local/param by TryGetDefinitionKey.
                if (captureToLocal is not null
                    && captureToLocal.ContainsKey(flowRef.Id)
                    && flowRef.Parent is ISimpleAssignmentOperation { Target: var tgt }
                    && ReferenceEquals(tgt, flowRef))
                {
                    break;
                }

                var capKey = new TrackedKey.Capture(flowRef.Id);
                if (current.TryGetValue(capKey, out var capId))
                    uses[(op, capKey)] = capId;
                break;
            }
        }
    }

    private static bool IsThisAccessingInvocation(IOperation op)
    {
        if (op is IInvocationOperation inv)
        {
            if (inv.TargetMethod.IsStatic)
            {
                // Static method without `this` passed as argument → safe.
                foreach (var arg in inv.Arguments)
                {
                    if (arg.Value is IInstanceReferenceOperation)
                        return true;
                }
                return false;
            }
            // Instance method: if receiver is `this` or implicit `this`, it can mutate fields.
            return inv.Instance is IInstanceReferenceOperation or null;
        }
        if (op is IObjectCreationOperation create)
        {
            foreach (var arg in create.Arguments)
            {
                if (arg.Value is IInstanceReferenceOperation)
                    return true;
            }
            return false;
        }
        return false;
    }

    private static IEnumerable<IOperation> EnumerateBlockOps(BasicBlock block)
    {
        foreach (var op in block.Operations)
        {
            foreach (var d in EnumerateAllOps(op))
                yield return d;
        }
        if (block.BranchValue is not null)
        {
            foreach (var d in EnumerateAllOps(block.BranchValue))
                yield return d;
        }
    }

    private static IEnumerable<IOperation> EnumerateAllOps(IOperation op)
    {
        yield return op;
        foreach (var child in op.ChildOperations)
        {
            if (child is null) continue;
            foreach (var d in EnumerateAllOps(child))
                yield return d;
        }
    }
}
