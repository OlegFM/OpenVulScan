# MethodSummary + MessagePack Serialization Plan (ovs-xwx.3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-method procedure summary model + MessagePack persistence with a documented format, ready for Phase 3 inter-procedural analyses.

**Architecture:** `MethodSummary` is a plain record in `OpenVulScan.Core` (no serializer dependency — mirrors the ADR-002 minimal-deps stance). `OpenVulScan.Cache` owns the `[MessagePackObject]` DTO contract, the `SummarySerializer` (record ⇄ DTO ⇄ bytes), and a versioned `SummaryFile` envelope. Methods are identified by Roslyn documentation-comment ID (`M:Ns.Type.Method(...)`) — the only symbol identity stable across compilations.

**Tech Stack:** MessagePack-CSharp (central package management), xUnit; new test project `tests/OpenVulScan.Cache.Tests` (added to `OpenVulScan.slnx`).

**Spec (inline):** Summary fields per the bead — return nullability (`NullState`), out/ref parameter nullability by parameter position, throws set (fully-qualified metadata names), purity flag, taint-pass-through placeholder (argument indices flowing to the return value; populated in Phase 4). Envelope carries `FormatVersion = 1`; readers must reject unknown majors. Format documented in `docs/cache-format.md`.

## Global Constraints

- `TreatWarningsAsErrors=true`; central package versions only (`Directory.Packages.props`).
- Flat namespace `OpenVulScan`; Cache references Core, never the reverse.
- Commits: `git -c user.name="Oleg" -c user.email="olegefm@gmail.com" commit …` + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

### Task 1: model + serializer + roundtrip tests

**Files:**
- Create: `src/OpenVulScan.Core/Summaries/MethodSummary.cs`
- Create: `src/OpenVulScan.Cache/SummarySerializer.cs` (DTOs + envelope + API)
- Modify: `Directory.Packages.props` (+`MessagePack`), `src/OpenVulScan.Cache/OpenVulScan.Cache.csproj` (+reference), `OpenVulScan.slnx` (+test project)
- Create: `tests/OpenVulScan.Cache.Tests/OpenVulScan.Cache.Tests.csproj` (clone of Core.Tests csproj shape, referencing Cache)
- Test: `tests/OpenVulScan.Cache.Tests/SummarySerializerTests.cs`

**Interfaces:**
- Produces: `MethodSummary(string MethodId, NullState ReturnNullability, ImmutableArray<ParameterNullability> OutParameters, ImmutableArray<string> Throws, bool IsPure, ImmutableArray<int> TaintPassThrough)`; `ParameterNullability(int Position, NullState State)`; `SummarySerializer.Serialize(IReadOnlyList<MethodSummary>) : byte[]`, `SummarySerializer.Deserialize(byte[]) : ImmutableArray<MethodSummary>` (throws `InvalidDataException` on version mismatch).

- [x] **Step 1:** Write `SummarySerializerTests` — roundtrip of 5 template summaries (pure getter NotNull; nullable factory MaybeNull; `bool TryGet(out T)` out-param MaybeNull-on-false shape; throwing validator with 2 exception types; taint-pass-through identity `[0]`), plus empty-collection roundtrip and version-rejection (patch the version byte, expect `InvalidDataException`).
- [x] **Step 2:** RED (projects/types missing).
- [x] **Step 3:** Implement model, DTOs (`[MessagePackObject]` int keys), envelope `{ FormatVersion, Summaries[] }`, serializer with `MessagePackSerializerOptions.Standard.WithCompression(Lz4BlockArray)`.
- [x] **Step 4:** GREEN; full suite.
- [x] **Step 5:** Write `docs/cache-format.md` (envelope, keys table, versioning policy, LZ4, identity = doc-comment ID).
- [x] **Step 6:** Commit; close ovs-xwx.3; export beads; push.
