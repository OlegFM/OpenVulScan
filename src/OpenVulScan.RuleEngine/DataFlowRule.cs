using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public abstract class DataFlowRule<TLattice>
{
    public abstract ILattice<TLattice> Lattice { get; }

    public virtual ITransfer<TLattice> Transfer
        => throw new NotSupportedException("Override CreateTransfer(SsaIndex) instead.");

    public virtual IEdgeRefiner<TLattice>? EdgeRefiner => null;

    // Default implementation: ignore SSA (legacy path). Overridden by SSA-aware rules.
    //
    // CONTRACT (statelessness):
    //   DataFlowRuleDispatcher groups rules by transfer/refiner TYPE and runs one shared
    //   worklist solve per group.  Two instances of the same concrete type must therefore
    //   be behaviourally identical — the dispatcher picks an arbitrary representative and
    //   uses its transfer for the entire group.
    //   The returned ITransfer<TLattice> MUST be stateless apart from the SsaIndex it is
    //   built over (which is the same for every rule in the same per-method pass).
    //   Do NOT capture per-rule configuration in the transfer object: if two rules need
    //   distinct transfer behaviour, give each a distinct type so the dispatcher treats
    //   them as separate groups.  Silently sharing a group with a differently-configured
    //   transfer would merge the two pipelines without any error.
    public virtual ITransfer<TLattice> CreateTransfer(SsaIndex ssaIndex) => Transfer;

    // Default implementation: SSA-unaware refiner from the legacy property.
    // SSA-aware rules override this to construct a refiner over the index.
    //
    // CONTRACT (statelessness):
    //   DataFlowRuleDispatcher groups rules by transfer/refiner TYPE and runs one shared
    //   worklist solve per group.  Two instances of the same concrete type must therefore
    //   be behaviourally identical — the dispatcher picks an arbitrary representative and
    //   uses its edge refiner for the entire group.
    //   The returned IEdgeRefiner<TLattice> MUST be stateless apart from the SsaIndex it
    //   is built over (which is the same for every rule in the same per-method pass).
    //   Do NOT capture per-rule configuration in the refiner object: if two rules need
    //   distinct refinement behaviour, give each a distinct type so the dispatcher treats
    //   them as separate groups.  Silently sharing a group with a differently-configured
    //   refiner would merge the two pipelines without any error.
    public virtual IEdgeRefiner<TLattice>? CreateEdgeRefiner(SsaIndex ssaIndex) => EdgeRefiner;

    protected virtual void OnState(IOperation operation, TLattice state, DataFlowContext context)
    {
    }

    internal void InvokeOnState(IOperation operation, TLattice state, DataFlowContext context)
        => OnState(operation, state, context);
}
