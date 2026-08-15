using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// Compilation-level oracle for callee return nullability, consulted by the NullState
/// transfer at invocation sites. Implementations answer <see cref="NullState.Unknown"/>
/// for methods they have no evidence about (metadata callees, unanalyzable bodies) —
/// the deref classifier is silent on Unknown, keeping unknown callees FP-safe.
/// </summary>
public interface INullabilitySummaryLookup
{
    NullState ReturnStateOf(IMethodSymbol method);
}
