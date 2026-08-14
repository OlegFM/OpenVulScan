using System.Collections.Immutable;

namespace OpenVulScan;

/// <summary>
/// Nullability fact about a single out/ref parameter, addressed by its ordinal position.
/// </summary>
public readonly record struct ParameterNullability(int Position, NullState State);

/// <summary>
/// Per-method procedure summary: the facts an inter-procedural analysis needs at a call
/// site without re-analyzing the callee's body.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="MethodId"/> is the Roslyn documentation-comment ID
/// (<c>M:Ns.Type.Method(...)</c>) — the only symbol identity stable across compilations,
/// which makes summaries cacheable between runs.
/// </para>
/// <para>
/// <paramref name="TaintPassThrough"/> lists argument ordinals whose value flows to the
/// return value (identity-shaped methods); populated by the taint phase, empty until then.
/// </para>
/// </remarks>
public sealed record MethodSummary(
    string MethodId,
    NullState ReturnNullability,
    ImmutableArray<ParameterNullability> OutParameters,
    ImmutableArray<string> Throws,
    bool IsPure,
    ImmutableArray<int> TaintPassThrough);
