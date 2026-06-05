using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public sealed record Phi(SsaId Result, ImmutableArray<PhiOperand> Operands);

public readonly record struct PhiOperand(BasicBlock PredecessorBlock, SsaId Version);
