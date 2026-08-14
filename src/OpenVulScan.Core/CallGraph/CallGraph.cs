using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

/// <summary>
/// A call site inside <see cref="Caller"/> with its CHA candidate target set.
/// </summary>
/// <param name="Caller">The method whose body contains the call site (original definition).</param>
/// <param name="CallSite">The syntax node of the invocation / object creation.</param>
/// <param name="Candidates">
/// Possible runtime targets (original definitions). A single element means the call is
/// statically bound (static, non-virtual, sealed receiver, constructor).
/// </param>
public sealed record CallEdge(
    IMethodSymbol Caller,
    SyntaxNode CallSite,
    ImmutableArray<IMethodSymbol> Candidates);

/// <summary>
/// Whole-compilation call graph produced by <see cref="ChaBuilder"/>. Immutable; both the
/// forward (callees) and inverse (callers) indices are precomputed.
/// </summary>
public sealed class CallGraph
{
    private readonly ImmutableDictionary<IMethodSymbol, ImmutableArray<CallEdge>> _callees;
    private readonly ImmutableDictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>> _callers;

    internal CallGraph(
        ImmutableDictionary<IMethodSymbol, ImmutableArray<CallEdge>> callees,
        ImmutableDictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>> callers,
        ImmutableArray<IMethodSymbol> methods)
    {
        _callees = callees;
        _callers = callers;
        Methods = methods;
    }

    /// <summary>All source methods (original definitions) discovered in the compilation,
    /// including those without call sites.</summary>
    public ImmutableArray<IMethodSymbol> Methods { get; }

    /// <summary>The call edges leaving <paramref name="method"/>'s body.</summary>
    public ImmutableArray<CallEdge> Callees(IMethodSymbol method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return _callees.TryGetValue(method.OriginalDefinition, out var edges)
            ? edges
            : ImmutableArray<CallEdge>.Empty;
    }

    /// <summary>The methods whose bodies may invoke <paramref name="method"/> (inverse of
    /// the candidate sets).</summary>
    public ImmutableArray<IMethodSymbol> Callers(IMethodSymbol method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return _callers.TryGetValue(method.OriginalDefinition, out var callers)
            ? callers
            : ImmutableArray<IMethodSymbol>.Empty;
    }
}
