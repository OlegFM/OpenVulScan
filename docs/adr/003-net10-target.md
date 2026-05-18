# ADR-003: Target .NET 10 and Drop netstandard2.0 / Roslyn 4.x

**Status:** Accepted

**Date:** 2026-05-18

**Deciders:** OpenVulScan Core Team

**References:**
- [ANALYZER_PLAN.md §2.3 — Чего сознательно нет в первой версии](../ANALYZER_PLAN.md#23-чего-сознательно-нет-в-первой-версии)
- [.NET 10 Release Notes](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Roslyn 5.0 Release Notes](https://github.com/dotnet/roslyn/releases)

---

## Context

OpenVulScan must choose a target framework and Roslyn version. The C# analyzer ecosystem is fragmented across:
- .NET Framework 4.8 + Roslyn 3.x (legacy, still used in old VS extensions)
- .NET 6/8 + Roslyn 4.x (current stable, VS 2022)
- .NET 10 + Roslyn 5.x (upcoming, Rider 2026+, VS 2026)

Supporting multiple targets increases maintenance burden, especially for a single-engineer project.

## Decision

Target **.NET 10** with **Roslyn 5.x** only. Explicitly drop support for:
- `netstandard2.0`
- `net472` / `net48`
- Roslyn 3.x and 4.x
- Visual Studio 2022 and earlier
- JetBrains Rider 2025 and earlier

## Consequences

### Positive
- **Modern API surface** — access to `IIncrementalGenerator`, enhanced `IOperation`, and latest Roslyn performance improvements
- **Simpler build matrix** — single TFM, single Roslyn version, no conditional compilation
- **Smaller distribution** — no multi-targeting, no compatibility shims
- **Future-proof** — .NET 10 is an LTS candidate; Rider 2026+ and VS 2026 will be the standard by the time OpenVulScan reaches beta
- **No legacy baggage** — can use `required` members, `static abstract` in interfaces, `Span<T>` everywhere without fallback

### Negative
- **Narrower user base initially** — developers on older .NET versions cannot use the tool until they upgrade their environment
- **CI limitations** — some corporate CI images may lag behind; users need .NET 10 SDK installed
- **Plugin ecosystem delay** — VS 2026 extension must wait for the new extensibility model; VS 2022 plugin is explicitly out of scope

## Alternatives Considered

### Multi-target netstandard2.0 + net10.0
- Rejected: `netstandard2.0` severely restricts Roslyn API usage (no `IIncrementalGenerator`, limited `IOperation`)
- Would require two code paths and extensive `#if` directives

### Support Roslyn 4.x as minimum
- Rejected: Roslyn 4.x lacks several performance optimizations and new APIs available in 5.x
- Would require compatibility testing across multiple Visual Studio versions

### Target .NET 8 (LTS) instead of .NET 10
- Rejected: .NET 8 will reach end of support before OpenVulScan reaches maturity
- .NET 10 provides better alignment with target IDE releases (Rider 2026, VS 2026)

## Implementation Notes

- All `.csproj` files use `<TargetFramework>net10.0</TargetFramework>`
- `Directory.Build.props` enforces `LangVersion` and `Nullable` settings
- Roslyn packages (Microsoft.CodeAnalysis.*) are pinned to 5.0.0 in `Directory.Packages.props`
- CI workflow uses `dotnet-install` with .NET 10 channel
- Documentation explicitly states: "Requires .NET 10 SDK or later"
