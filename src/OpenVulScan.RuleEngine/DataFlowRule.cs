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
    public virtual ITransfer<TLattice> CreateTransfer(SsaIndex ssaIndex) => Transfer;

    protected virtual void OnState(IOperation operation, TLattice state, DataFlowContext context)
    {
    }

    internal void InvokeOnState(IOperation operation, TLattice state, DataFlowContext context)
        => OnState(operation, state, context);
}
