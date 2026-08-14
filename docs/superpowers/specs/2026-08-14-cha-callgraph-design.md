# CHA call graph — design (ovs-xwx.1)

**Bead:** ovs-xwx.1. **Date:** 2026-08-14. **Status:** Accepted.

## Goal

`OpenVulScan.Core` gains a call graph built by Class Hierarchy Analysis: for every call
site, the candidate target set derived from the receiver's static type hierarchy (all
overrides / interface implementations). Foundation for summaries (ovs-xwx.3), bottom-up
SCC traversal (ovs-xwx.5) and inter-procedural rules. RTA refinement is ovs-xwx.2.

## Model

```
CallEdge  = (Caller: IMethodSymbol, CallSite: SyntaxNode, Candidates: ImmutableArray<IMethodSymbol>)
CallGraph = { Callees(method) -> edges from its body,
              Callers(method) -> methods that may invoke it,
              Methods -> all source methods with bodies }
```

Symbols are normalised to `OriginalDefinition`; all maps use `SymbolEqualityComparer.Default`.

## Algorithm

1. **Subtype index** — one pass over `compilation.Assembly.GlobalNamespace`: for each
   source `INamedTypeSymbol`, register it under every base class in its chain and every
   entry of `AllInterfaces`. Metadata (referenced-assembly) types are NOT enumerated —
   CHA candidates come from the analysed source; documented limitation until RTA.
2. **Edges** — for each method body (methods, constructors, accessors, local functions),
   walk its `IOperation` tree:
   - `IInvocationOperation`:
     - static / non-virtual / sealed-receiver target ⇒ single candidate;
     - interface member ⇒ for every source type implementing the interface,
       `FindImplementationForInterfaceMember`, plus the interface method itself (unseen
       implementers may exist outside the compilation);
     - virtual/abstract/override ⇒ the *declared* target plus, for every source subtype of
       the target's containing type, the override that final-binds there (walk
       `OverriddenMethod` chains up to the declared target).
   - `IObjectCreationOperation` ⇒ single constructor candidate.
   - Delegates / lambdas / function pointers ⇒ out of scope v1 (no edge).

## Complexity / acceptance

Index pass O(types × bases); edge pass O(operations). The "< 60 s on Roslyn" acceptance
belongs to the ovs-xwx.11 bench run; this bead ships the builder + unit coverage of the
dispatch shapes (static, virtual, interface, sealed, override chains, callers index).
