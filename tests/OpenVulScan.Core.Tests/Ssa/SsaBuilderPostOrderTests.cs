using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaBuilderPostOrderTests
{
    private static IEnumerable<IOperation> AllOps(ControlFlowGraph cfg)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in block.Operations)
                foreach (var d in Descend(op))
                    yield return d;
            if (block.BranchValue is not null)
                foreach (var d in Descend(block.BranchValue))
                    yield return d;
        }

        static IEnumerable<IOperation> Descend(IOperation op)
        {
            yield return op;
            foreach (var child in op.ChildOperations)
            {
                if (child is null) continue;
                foreach (var d in Descend(child)) yield return d;
            }
        }
    }

    [Fact]
    public void FieldAssignFromThisCall_DefSurvivesRhsKill()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        this.f = 0;
        this.f = GetX();
        var y = this.f;
    }
    int GetX() => 0;
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var key = new TrackedKey.InstanceField(field);

        // Second assignment: RHS call kills the seeded version FIRST (post-order),
        // then the def registers; the downstream read must bind exactly that def.
        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .Last(a => a.Target is IFieldReferenceOperation);
        var read = AllOps(cfg).OfType<IFieldReferenceOperation>()
            .Single(f => f.Parent is not ISimpleAssignmentOperation parent
                         || !ReferenceEquals(parent.Target, f));

        Assert.Equal(index.DefinitionAt(assign), index.UseAt(read, key));
        // Versions: v0 seed def, v1 RHS kill, v2 the second assignment's def.
        Assert.Equal(3, index.AllVersions(key).Count);
    }

    [Fact]
    public void ArgumentFieldRead_BindsPreKillVersion()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        this.f = 1;
        Use(this.f);
    }
    void Use(int v) { }
}");
        var index = SsaBuilder.Build(cfg, model);

        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!
            .GetMembers("f").First();
        var key = new TrackedKey.InstanceField(field);

        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .Single(a => a.Target is IFieldReferenceOperation);
        var argRead = AllOps(cfg).OfType<IFieldReferenceOperation>()
            .Single(f => f.Parent is IArgumentOperation);

        // The argument is evaluated before the call executes: the read binds
        // the pre-kill version (the assignment's def), and the kill still
        // produces a later version afterwards.
        Assert.Equal(index.DefinitionAt(assign), index.UseAt(argRead, key));
        Assert.Equal(2, index.AllVersions(key).Count);
    }

    [Fact]
    public void SelfReferencingAssignment_RhsReadsOldVersion()
    {
        var (cfg, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x = x + 1;
    }
}");
        var index = SsaBuilder.Build(cfg, model);

        // int x = 0 lowers to an SA with Parent=null; x = x + 1 wraps in ExpressionStatement.
        var assign = AllOps(cfg).OfType<ISimpleAssignmentOperation>()
            .Single(a => a.Target is ILocalReferenceOperation lref
                         && lref.Local.Name == "x"
                         && a.Parent is IExpressionStatementOperation);
        var key = new TrackedKey.Symbol(((ILocalReferenceOperation)assign.Target).Local);

        // Find the single non-target read of x on the RHS of the assignment.
        // We exclude the assignment target itself and any references that are
        // the target of another write; what remains is the use inside x + 1.
        var read = AllOps(cfg).OfType<ILocalReferenceOperation>()
            .Where(l => l.Local.Name == "x"
                        && !(l.Parent is ISimpleAssignmentOperation sa && ReferenceEquals(sa.Target, l))
                        && !(l.Parent is ICompoundAssignmentOperation ca && ReferenceEquals(ca.Target, l))
                        && !(l.Parent is IIncrementOrDecrementOperation inc && ReferenceEquals(inc.Target, l)))
            .Single();

        // RHS reads the OLD version (0); the assignment defines version 1.
        Assert.Equal(0, index.UseAt(read, key)!.Value.Version);
        Assert.Equal(1, index.DefinitionAt(assign)!.Value.Version);
    }
}
