using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

public abstract class DataFlowRule<TLattice>
{
    public abstract ILattice<TLattice> Lattice { get; }

    public abstract ITransfer<TLattice> Transfer { get; }

    protected virtual void OnState(IOperation operation, TLattice state, DataFlowContext context)
    {
    }

    internal void InvokeOnState(IOperation operation, TLattice state, DataFlowContext context)
        => OnState(operation, state, context);
}
