# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:ca08a54f -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

## Session Completion

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd dolt push
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
<!-- END BEADS INTEGRATION -->

## Project Overview

OpenVulScan is an open-source SAST (static application security testing) tool for C# on .NET 10, aiming for ~70–80% functional parity with PVS-Studio's 275 C# rules. Currently in early phases (R&D → MVP → intra-procedural DFA → inter-procedural → taint).

Detection categories: null-dereference, dead code, identical sub-expressions, always-true/false conditions, taint flows (SQLi, XSS, path traversal), Unity/performance, etc. Rule codes mirror PVS-Studio's `V3xxx` numbering.

Solution file is `OpenVulScan.slnx` (XML solution format), not `.sln`.

## Build & Test

```powershell
# Restore + build (matches CI)
dotnet restore
dotnet build --configuration Release --no-restore

# Run all tests
dotnet test --configuration Release --no-build

# Run a single test project
dotnet test tests/OpenVulScan.Rules.Tests/OpenVulScan.Rules.Tests.csproj

# Run a single test by name filter
dotnet test --filter "FullyQualifiedName~V3001Tests"

# Run the CLI against a solution / project
dotnet run --project src/OpenVulScan.Cli -- analyze path/to/MyApp.slnx
dotnet run --project src/OpenVulScan.Cli -- analyze path/to/MyApp.csproj --format sarif --output result.sarif
dotnet run --project src/OpenVulScan.Cli -- rules list
dotnet run --project src/OpenVulScan.Cli -- baseline create result.sarif
```

CI matrix: Windows / Linux / macOS on `.NET 10.x` (see `.github/workflows/build.yml`). Tests emit `.trx` artifacts.

### Snapshot tests (Verify)

Rule tests under `tests/OpenVulScan.Rules.Tests/` use [Verify](https://github.com/VerifyTests/Verify) for snapshot assertions via `SnapshotTestHarness.RunRuleSnapshotAsync(ruleCode, testCase, source)`. When a rule's output changes intentionally, accept the new snapshot by renaming the generated `*.received.txt` to `*.verified.txt` (or use a Verify diff tool). Snapshots live next to the test files and are committed.

## Architecture

### Pipeline

```
ProjectLoader (MSBuildWorkspace + MSBuildLocator)
  → LoadedProject(s) with Roslyn Compilation
  → RuleScheduler.AnalyzeAsync(compilation)
      → AstRuleDispatcher / SymbolRuleDispatcher / DataFlow worklist
  → SuppressionFilter (inline `// VOVS:disable …` + [SuppressMessage])
  → BaselineFilter (optional --suppress)
  → Include/Exclude glob filters
  → Diagnostic[] → SARIF / JSON / text emitter
```

Entry point: `src/OpenVulScan.Cli/Program.cs` → `AnalyzeCommand` → `AnalyzeCommandHandler` → `AnalysisRunner.RunAnalysisAsync`.

### Project structure (one assembly per layer)

- `OpenVulScan.Frontend` — `ProjectLoader` loads `.sln` / `.slnx` / `.csproj` via `MSBuildWorkspace`. Falls back to `AdhocProjectLoader` + `compile_commands.json` when MSBuild fails (missing SDK / unresolved refs). `FailDetector` converts workspace diagnostics into `AnalysisFail` records (e.g. V051 missing reference, V052 build fail).
- `OpenVulScan.Core` — analysis primitives:
  - `Cfg/WorklistSolver<T>` — generic forward worklist over Roslyn's `ControlFlowGraph` with reverse-postorder ordering and an optional per-edge refiner.
  - `Lattice/` — `ILattice<T>`, `ITransfer<T>`, `IEdgeRefiner<T>` interfaces; built-in lattices: `NullStateLattice`, `BoolFlatLattice`, `ConstantLattice`, generic `MapLattice<TKey, TLat, TVal>`.
  - Suppressions: `InlineSuppressionParser`, `SuppressMessageAttributeParser`, `SuppressionFilter`.
- `OpenVulScan.RuleEngine` — extension model:
  - `[Rule(code, severity, cwe, category, capabilities)]` attribute on every rule class.
  - `RuleRegistry.Scan(assembly)` reflects over `[Rule]` types.
  - `RuleScheduler` instantiates rules per analysis, splits into `AstRule` / `SymbolRule` instances, hands them to dispatchers.
  - `AstRule` uses reflection over `protected void On<SyntaxKind>(SyntaxNodeContext)` methods (cached in a static `ConcurrentDictionary<Type, …>`). `OnBinaryExpression` and `OnAssignmentExpression` are syntactic-sugar fan-outs to ~20 underlying `SyntaxKind`s each.
  - `DataFlowRule<TLattice>` exposes `Lattice`, `Transfer`, optional `EdgeRefiner`, and an `OnState(operation, state, context)` callback driven by the worklist solver.
- `OpenVulScan.Rules.{Ast, DataFlow, PathSensitive, Taint, Performance}` — separate assemblies, one rule per file (filename = rule code), named `V3xxxRuleName.cs`.
- `OpenVulScan.Sarif` — SARIF 2.1.0 emitter, JSON emitter, plain-text emitter.
- `OpenVulScan.Cache` / `OpenVulScan.Configuration` — placeholders for incremental cache and YAML/JSON config (Phase 3+).

### Rule discovery is dynamic, not by ProjectReference

`AnalysisRunner.CreateRuleRegistry()` scans `OpenVulScan.Rules.*.dll` in `AppContext.BaseDirectory` and calls `RuleRegistry.Scan(assembly)`. The CLI does **not** reference the rule assemblies directly — `OpenVulScan.Cli.csproj` lists them via `ProjectReference … OutputItemType=Analyzer` or similar so the DLLs land next to the executable. When adding a new rules assembly, ensure its DLL ends up in the CLI output directory.

### Writing a new rule

1. Pick the right assembly: `Rules.Ast` for syntactic patterns, `Rules.DataFlow` for state-based, `Rules.PathSensitive` for refined branches, `Rules.Taint` for source→sink, `Rules.Performance` for perf/Unity.
2. Decorate the class with `[Rule("Vxxxx", RuleSeverity.LevelN, "CWE-nnn", RuleCategory.X, AnalysisCapability.Ast|DataFlow|…)]`.
3. Inherit from `AstRule`, `SymbolRule`, or `DataFlowRule<TLattice>` and override the relevant callback(s).
4. Add a `DiagnosticDescriptor` (id must equal the rule code) and report via `context.ReportDiagnostic(...)`.
5. Add at least one snapshot test in `tests/OpenVulScan.Rules.Tests/Vxxxx*Tests.cs` using `SnapshotTestHarness.RunRuleSnapshotAsync`.

### ADRs

Architectural decisions live in `docs/adr/NNN-*.md`. Read ADR-001 (architecture overview), ADR-002 (own SARIF emitter), ADR-003 (.NET 10 targeting) before changing core assumptions.

## Conventions

- **Single namespace `OpenVulScan`** across all assemblies (no per-folder namespaces). Folder layout is purely organisational.
- **C# settings** (from `Directory.Build.props`): `TargetFramework=net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, `LangVersion=preview`. CA warnings are errors — fix them or suppress narrowly with `#pragma warning disable CAxxxx` + justification, mirroring existing patterns in `AnalysisRunner.cs` and `RuleScheduler.cs`.
- **Central package management**: all NuGet versions live in `Directory.Packages.props`. Add `<PackageVersion>` there, then `<PackageReference Include="…" />` (no version) in the csproj.
- **`.editorconfig`**: 4-space indent for code, 2-space for `csproj`/`props`/`slnx`. Trim trailing whitespace.
- **Cancellation**: every async public API takes `CancellationToken` and propagates it. `AnalyzeAsync` etc. call `ct.ThrowIfCancellationRequested()` between projects/blocks.
- **Diagnostic IDs** must match the `[Rule]` code exactly — the registry rejects duplicates and tests filter by `d.Id == ruleCode`.

## Important repo quirks

- The top-level directory has several oddly-named folders like `srcOpenVulScan.Cli/` and `spikesMsbuildLoader…/`. These are leftover artefacts from a path-normalisation issue, **not** the real source. Real code lives under `src/` and `spikes/`. Do not edit the `srcOpenVulScan.*` directories unless explicitly cleaning them up.
- Solution file is `OpenVulScan.slnx` (XML format), not a classic `.sln`. Some tools that don't support `.slnx` may fail — point them at the `.csproj` files directly.
- `RulesMap.xml` (~270 KB) is a reference catalogue mapping PVS-Studio rule IDs to descriptions/CWE — useful when implementing a new `V3xxx`.
- `ANALYZER_PLAN.md` (~80 KB) is the long-form design doc the ADRs reference.

## Non-Interactive Shell

When invoking `cp`, `mv`, `rm`, `apt-get`, etc., always use force/non-interactive flags (`-f`, `-y`, `-rf`) — some shells alias these to `-i` and will hang the agent. See `AGENTS.md` for the full list.
