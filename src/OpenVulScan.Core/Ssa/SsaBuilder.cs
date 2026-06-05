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

        var definitions = ImmutableDictionary.CreateBuilder<IOperation, SsaId>();
        var uses = ImmutableDictionary.CreateBuilder<(IOperation, TrackedKey), SsaId>();
        var entryVersions = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableDictionary<TrackedKey, SsaId>>();
        var phis = ImmutableDictionary.CreateBuilder<BasicBlock, ImmutableArray<Phi>>();
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

        // Pass 0: define parameters at entry block, version 0.
        var current = new Dictionary<TrackedKey, SsaId>();
        var entryBlock = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Entry);

        var methodDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDecl is not null)
        {
            var methodSymbol = model.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            if (methodSymbol is not null)
            {
                foreach (var p in methodSymbol.Parameters)
                {
                    var key = new TrackedKey.Symbol(p);
                    var id = NewVersion(key);
                    current[key] = id;
                }
            }
        }

        entryVersions[entryBlock] = ImmutableDictionary.CreateRange(current);

        // Pass 1+2: visit blocks in CFG order.
        // φ handling (multi-predecessor blocks) is added in Task 7.
        var blockOutState = new Dictionary<BasicBlock, Dictionary<TrackedKey, SsaId>>
        {
            [entryBlock] = new Dictionary<TrackedKey, SsaId>(current),
        };

        foreach (var block in cfg.Blocks)
        {
            if (block.Kind == BasicBlockKind.Entry) continue;

            // Single-predecessor: inherit out-state. Multi-predecessor: start empty (Task 7 adds φ).
            if (block.Predecessors.Length == 1
                && blockOutState.TryGetValue(block.Predecessors[0].Source, out var predOut))
            {
                current = new Dictionary<TrackedKey, SsaId>(predOut);
            }
            else
            {
                current = [];
            }

            entryVersions[block] = ImmutableDictionary.CreateRange(current);

            foreach (var op in block.Operations.SelectMany(EnumerateAllOps))
            {
                ProcessOperation(op, current, NewVersion, definitions, uses);
            }

            if (block.BranchValue is not null)
            {
                foreach (var op in EnumerateAllOps(block.BranchValue))
                {
                    ProcessOperation(op, current, NewVersion, definitions, uses);
                }
            }

            blockOutState[block] = current;
        }

        var allVersionsImmutable = allVersions.ToImmutableDictionary(
            kv => kv.Key,
            kv => kv.Value.ToImmutableArray());

        return new SsaIndex(
            definitions.ToImmutable(),
            uses.ToImmutable(),
            entryVersions.ToImmutable(),
            phis.ToImmutable(),
            allVersionsImmutable);
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

    private static void ProcessOperation(
        IOperation op,
        Dictionary<TrackedKey, SsaId> current,
        Func<TrackedKey, SsaId> newVersion,
        ImmutableDictionary<IOperation, SsaId>.Builder definitions,
        ImmutableDictionary<(IOperation, TrackedKey), SsaId>.Builder uses)
    {
        switch (op)
        {
            case IVariableDeclaratorOperation { Symbol: ILocalSymbol local }:
            {
                RegisterDef(op, new TrackedKey.Symbol(local), current, newVersion, definitions);
                break;
            }
            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation lrefTarget }:
            {
                RegisterDef(op, new TrackedKey.Symbol(lrefTarget.Local), current, newVersion, definitions);
                break;
            }
            case ISimpleAssignmentOperation { Target: IParameterReferenceOperation prefTarget }:
            {
                RegisterDef(op, new TrackedKey.Symbol(prefTarget.Parameter), current, newVersion, definitions);
                break;
            }
            case ICompoundAssignmentOperation { Target: ILocalReferenceOperation lrefCompound }:
            {
                RegisterDef(op, new TrackedKey.Symbol(lrefCompound.Local), current, newVersion, definitions);
                break;
            }
            case ICompoundAssignmentOperation { Target: IParameterReferenceOperation prefCompound }:
            {
                RegisterDef(op, new TrackedKey.Symbol(prefCompound.Parameter), current, newVersion, definitions);
                break;
            }
            case IIncrementOrDecrementOperation { Target: ILocalReferenceOperation lrefIncr }:
            {
                RegisterDef(op, new TrackedKey.Symbol(lrefIncr.Local), current, newVersion, definitions);
                break;
            }
            case IIncrementOrDecrementOperation { Target: IParameterReferenceOperation prefIncr }:
            {
                RegisterDef(op, new TrackedKey.Symbol(prefIncr.Parameter), current, newVersion, definitions);
                break;
            }
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
