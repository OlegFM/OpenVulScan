# ADR-001: Architecture Overview

**Status:** Accepted

**Date:** 2026-05-13

**Deciders:** OpenVulScan Core Team

**References:**
- [ANALYZER_PLAN.md §4 — Architecture](../ANALYZER_PLAN.md#4-архитектура-анализатора)
- [ANALYZER_PLAN.md §5 — Technology Stack](../ANALYZER_PLAN.md#5-технологический-стек)

---

## Context

OpenVulScan is an open-source static analyzer for C# targeting functional parity with PVS-Studio (70–80% of 275 diagnostic rules). The project is currently in Phase 0 (R&D and spikes). We need to capture the high-level architectural decisions that will guide development over the next 3–4 years.

Key forces driving these decisions:
- **Single-engineer constraint:** The project is developed by one senior .NET engineer; complexity must be minimized.
- **Target parity:** PVS-Studio for C# has 275 rules, 5 mandatory analysis methods per GOST R 71207-2024, and extensive infrastructure.
- **Modern ecosystem:** Target IDE versions are Rider 2026+ and Visual Studio 2026, both running on .NET 10.
- **No legacy burden:** Conscious decision to drop support for older runtimes, IDEs, and `netstandard2.0` to reduce maintenance.

---

## Decision

We adopt the following architectural decisions, derived from ANALYZER_PLAN.md sections 4 and 5.

### 1. Runtime: .NET 10 (LTS) with Roslyn 5.x

**Decision:** Target .NET 10 exclusively. Use Roslyn 5.x (shipped with .NET 10 SDK) as the only compiler platform.

**Rationale:**
- .NET 10 is the current LTS release and includes an updated JIT and Roslyn 5.x.
- Target IDEs (Rider 2026+, VS 2026) run on .NET 10, eliminating the need for multitargeting.
- Dropping `netstandard2.0` and older Roslyn 4.x versions simplifies the codebase and removes a class of compatibility bugs.
- Enables use of modern C# language features (collection expressions, `field` keyword, primary constructors) in analyzer code.

**Consequences:**
- (+) Reduced complexity — no conditional compilation or polyfills.
- (+) Access to latest Roslyn APIs and performance improvements.
- (−) Projects that cannot build with .NET 10 SDK are out of scope.
- (−) Roslyn API churn between major versions requires controlled upgrades.

---

### 2. Frontend: MSBuildWorkspace + Microsoft.Build.Locator

**Decision:** Use `Microsoft.Build.Locator` to discover the .NET SDK and `MSBuildWorkspace` (from `Microsoft.CodeAnalysis.CSharp.Workspaces.MSBuild`) to load `.sln` and `.csproj` files.

**Rationale:**
- `MSBuildWorkspace` is the standard, stable way to load MSBuild-based projects into a Roslyn workspace.
- Supports both SDK-style and legacy MSBuild projects.
- `Microsoft.Build.Locator` resolves MSBuild assemblies from the installed SDK, avoiding binding issues.
- Fallback to `AdhocWorkspace` + manual `ProjectReference` for exotic scenarios.

**Consequences:**
- (+) Mature, well-documented API for project loading.
- (+) Handles NuGet restore and reference resolution.
- (−) May fail on exotic projects (Unity, F#-mixed, legacy `net461` with custom targets); fallback mechanisms required.

---

### 3. No `netstandard2.0` Support

**Decision:** All assemblies target `net10.0` only. No multitargeting, no `netstandard2.0` compatibility shims.

**Rationale:**
- Target IDEs are guaranteed to run on .NET 10.
- Eliminates `#if NET...` conditional compilation in rule code.
- Avoids dependency on older Roslyn 4.x APIs that differ in semantic model details.

**Consequences:**
- (+) Cleaner build system and faster compilation.
- (+) Smaller distribution (single TFM).
- (−) Prevents use in older .NET Framework or .NET 8 environments.
- (−) Third-party consumers must be on .NET 10.

---

### 4. Rule Format: `[Rule]` Attribute-Based Registration

**Decision:** Rules are C# classes decorated with a `[Rule]` attribute and inheriting from one of four base classes: `AstRule`, `SymbolRule`, `DataFlowRule<TLattice>`, or `TaintRule`.

**Rationale:**
- Familiar to Roslyn analyzer authors (similar shape to `DiagnosticAnalyzer`).
- Declarative metadata (code, severity, CWE, category, capabilities) enables tooling: rule registries, coverage reports, IDE quick-fixes.
- Separation of rule types allows the scheduler to run lightweight AST rules before heavy data-flow rules.
- Roslyn's `DiagnosticAnalyzer` API does not expose full CFG/fixpoint infrastructure, so a custom engine is required; we keep the surface similar for onboarding.

**Consequences:**
- (+) Self-documenting rules — all metadata is visible in source.
- (+) Reflection-based registry is simple to implement and inspect.
- (−) Slight runtime overhead for attribute scanning at startup (negligible compared to analysis time).

---

### 5. SARIF: Custom Implementation Against OASIS SARIF 2.1.0

**Decision:** Implement SARIF 2.1.0 output against the OASIS schema without using `Microsoft.CodeAnalysis.Sarif.Driver`.

**Rationale:**
- The Microsoft SARIF driver library is under-maintained and carries heavy dependencies.
- A custom implementation gives full control over serialization, baseline correlation, and extensions (e.g., fuzzy fingerprints).
- SARIF 2.1.0 is a stable, well-specified format; implementing a subset is straightforward.

**Consequences:**
- (+) Full control over output format and extensions.
- (+) No dependency on an external library with uncertain maintenance.
- (−) Must keep schema compliance up to date manually.
- (−) Minor increase in initial implementation effort.

---

### 6. Cache: MessagePack-CSharp

**Decision:** Use `MessagePack-CSharp` for serialization of compilation cache, method summaries, and incremental analysis state.

**Rationale:**
- MessagePack is significantly faster and more compact than JSON or BinaryFormatter.
- Method summaries are produced in large volume during inter-procedural analysis; compact serialization reduces I/O.
- Supports schema evolution via version headers if needed.

**Consequences:**
- (+) High performance and low memory footprint for cache operations.
- (+) Mature library with good .NET support.
- (−) Requires careful handling of Roslyn symbol serialization (custom resolvers for `ISymbol` fingerprints).

---

### 7. IR Built on Roslyn IOperation + ControlFlowGraph

**Decision:** Use Roslyn's `IOperation` tree and built-in `ControlFlowGraph` as the foundation for our intermediate representation. Build custom slabs (`OvsMethod`, `OvsHeapObject`, `OvsSymbolFingerprint`) on top.

**Rationale:**
- `IOperation` provides a language-neutral, semantic-aware operation tree — type information, constant values, and conversions are already resolved.
- Roslyn's `ControlFlowGraph.Create()` gives a standard CFG over `IOperation` blocks; rebuilding this would cost ~person-years.
- Custom IR adds SSA numbering, heap abstraction, and serializable fingerprints without duplicating Roslyn's work.

**Consequences:**
- (+) Leverages thousands of engineer-hours invested in Roslyn.
- (+) Automatic support for new C# language features as Roslyn updates.
- (−) Roslyn CFG may lag behind newest language constructs (e.g., file-scoped types, collection expressions); regression tests required.

---

### 8. Intra-Procedural DFA with Lattice-Based Analysis

**Decision:** Implement intra-procedural data-flow analysis using the monotone framework with abstract lattices (`NullState`, `ConstantValue`, `IntervalDomain`, `InitializedState`, `DisposeState`). Solve via worklist over reverse post-order CFG.

**Rationale:**
- Monotone framework is the standard approach for static analysis; well-understood, provably sound (with appropriate abstractions).
- Lattices compose via product domains (`MapLattice<TKey, TValue>`).
- Path-sensitivity is achieved by splitting states on conditional edges (e.g., `if (x != null)` refines `x.NullState` to `NotNull` on the true branch).
- Bounded path exploration (64 branches per method) prevents explosion, with graceful fallback to flow-sensitive merge.

**Consequences:**
- (+) Covers the majority of General Analysis (V3xxx) rules.
- (+) Bounded path-sensitivity gives good precision without SMT solver complexity.
- (−) Without inter-procedural context, precision on real codebases is insufficient (addressed in Decision 9).
- (−) Bounded exploration may miss bugs in deeply nested code.

---

### 9. Inter-Procedural Analysis via CHA/RTA Call Graph + Procedure Summaries

**Decision:** Build a call graph using Class Hierarchy Analysis (CHA) for virtual calls and Rapid Type Analysis (RTA) for interfaces. Compute procedure summaries per method and propagate them bottom-up over SCCs.

**Rationale:**
- Inter-procedural context is mandatory for acceptable false positive rates on real projects.
- We use CHA for virtual calls and RTA for interfaces to construct the call graph because it strikes a balance between precision and scalability; whole-program context-sensitive analysis is too expensive for a solo project.
- Procedure summaries are used for inter-procedural data flow propagation, capturing nullability effects, taint propagation, and exception paths in a compact, serializable form that can be propagated bottom-up over SCCs.
- The IFDS/IDE framework (Reps-Horwitz-Sagiv) is a well-known reference for inter-procedural analysis and may be evaluated as a future enhancement for context-sensitive precision, but the current chosen approach is CHA/RTA + summaries for pragmatic reasons.

**Consequences:**
- (+) Significant reduction in false positives on real codebases.
- (+) Summaries enable incremental analysis (Decision 10).
- (−) CHA/RTA may over-approximate call targets for interfaces/delegates, leading to spurious edges.
- (−) SCCs with recursion require iterative fixed-point computation.

---

### 10. Taint Analysis with YAML-Configured Sources/Sinks/Sanitizers

**Decision:** Implement a dedicated taint analysis engine with a YAML configuration file defining sources, sinks, and sanitizers. Taint rules (`TaintRule`) operate on a `TaintState` lattice.

**Rationale:**
- Taint analysis is mandatory for OWASP/SAST rules (V56xx) per GOST R 71207-2024.
- YAML configuration allows non-programmers (security engineers) to extend source/sink definitions.
- Aligns with industry practice (Semgrep, CodeQL).
- String propagators (concat, format, interpolation, `StringBuilder`, LINQ) are explicitly modeled.

**Consequences:**
- (+) Rules for SQLi, XSS, path traversal, etc., are data-driven and updateable without recompilation.
- (+) Community can contribute API models via PRs.
- (−) YAML models require periodic updates as libraries evolve.
- (−) Incomplete models lead to false negatives; requires ongoing maintenance.

---

## Consequences (Summary)

### Positive
1. **Simplified codebase:** Dropping `netstandard2.0` and older Roslyn versions removes compatibility shim code.
2. **Modern toolchain:** .NET 10 + latest Roslyn gives access to performance improvements and language features.
3. **Scalable architecture:** Layered design (Frontend → IR → Analysis Core → Rule Engine → Emitters) allows incremental delivery.
4. **Precision roadmap:** Intra-procedural → inter-procedural → taint engine provides a clear path from MVP to production quality.
5. **Extensibility:** Attribute-based rules and YAML taint config enable community contributions.

### Negative
1. **Limited backward compatibility:** No support for .NET Framework, .NET 8, or older IDEs.
2. **Roslyn dependency:** API changes in future Roslyn versions require adapter updates.
3. **Manual SARIF maintenance:** No external library handles schema compliance.
4. **Maintenance burden:** Taint models and API annotations require ongoing updates.
5. **Memory pressure:** SemanticModel + IOperation trees are large; large solutions (>500k LoC) need memory limits and graceful degradation.

### Neutral
- The project consciously trades breadth (275 rules) for depth (200 high-quality rules) — this is a product decision, not a technical constraint.

---

## Related Decisions

- ADR-002 (planned): Incremental analysis and caching strategy
- ADR-003 (planned): IDE integration architecture (Rider 2026+, VS 2026)
- ADR-004 (planned): CI/CD integration and GitHub Actions design

---

## Notes

- This ADR was created during Phase 0 (R&D). Decisions may be revisited as spikes complete and implementation constraints become clearer.
- For detailed layer diagrams, lattice interfaces, and technology comparison tables, see ANALYZER_PLAN.md §4 and §5.
- **Peer-review note:** This document has undergone spec compliance review as part of the development process.
