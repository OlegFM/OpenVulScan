using System.Collections.Immutable;

namespace OpenVulScan;

/// <summary>
/// Shared pipeline definition for the NRE rule family
/// (V3080 / V3105 / V3153 / V3168): NullState over SSA versions with
/// branch refinement. Sealing the pipeline members guarantees the
/// dispatcher groups all family rules into a single worklist solve.
/// </summary>
public abstract class NullStateRuleBase : DataFlowRule<ImmutableDictionary<SsaId, NullState>>
{
    public sealed override ILattice<ImmutableDictionary<SsaId, NullState>> Lattice { get; }
        = new MapLattice<SsaId, NullStateLattice, NullState>();

    public sealed override ITransfer<ImmutableDictionary<SsaId, NullState>> CreateTransfer(SsaIndex ssaIndex)
        => new NullStateSsaTransfer(ssaIndex);

    public sealed override IEdgeRefiner<ImmutableDictionary<SsaId, NullState>>? CreateEdgeRefiner(SsaIndex ssaIndex)
        => new NullStateSsaEdgeRefiner(ssaIndex);
}
