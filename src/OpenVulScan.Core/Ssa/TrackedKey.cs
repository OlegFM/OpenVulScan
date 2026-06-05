#pragma warning disable CA1034 // Nested types are intentional: TrackedKey is a discriminated union whose cases must derive from the sealed base.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace OpenVulScan;

public abstract record TrackedKey
{
    public sealed record Symbol(ISymbol Variable) : TrackedKey
    {
        public bool Equals(Symbol? other)
            => other is not null && SymbolEqualityComparer.Default.Equals(Variable, other.Variable);

        public override int GetHashCode()
            => SymbolEqualityComparer.Default.GetHashCode(Variable);
    }

    public sealed record InstanceField(IFieldSymbol Field) : TrackedKey
    {
        public bool Equals(InstanceField? other)
            => other is not null && SymbolEqualityComparer.Default.Equals(Field, other.Field);

        public override int GetHashCode()
            => SymbolEqualityComparer.Default.GetHashCode(Field);
    }

    public sealed record Capture(CaptureId Id) : TrackedKey;
}
