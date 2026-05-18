# ADR-002: Custom SARIF Implementation

**Status:** Accepted

**Date:** 2026-05-18

**Deciders:** OpenVulScan Core Team

**References:**
- [OASIS SARIF 2.1.0 Specification](https://docs.oasis-open.org/static-analysis-interchange-format/sarif-2.1.0/)
- [Microsoft.CodeAnalysis.Sarif NuGet Package](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Sarif)

---

## Context

OpenVulScan needs to produce SARIF 2.1.0 output for integration with CI systems, GitHub Code Scanning, and other SAST tooling. Two options exist:

1. **Use Microsoft.CodeAnalysis.Sarif** — Microsoft's official SARIF SDK
2. **Implement SARIF manually** — write records and JSON serialization ourselves

## Decision

We implement SARIF 2.1.0 manually using simple C# `record` types with `System.Text.Json` serialization.

## Consequences

### Positive
- **Zero external dependencies** for SARIF output — no version conflicts, no transitive dependency bloat
- **Full control over serialization** — we can optimize for our specific use case and ensure exact OASIS 2.1.0 compliance
- **Smaller binary size** — no need to ship Microsoft.CodeAnalysis.Sarif.dll and its dependencies
- **Faster build times** — fewer packages to restore
- **No maintenance risk** — Microsoft.CodeAnalysis.Sarif has historically had infrequent updates and breaking changes between versions

### Negative
- **Manual schema updates** — if SARIF spec evolves, we must update our records manually
- **No built-in validation** — we must validate output against OASIS schema ourselves (mitigated by integration tests)
- **Reinventing the wheel** — some utility functions (URI handling, region formatting) must be implemented manually

## Alternatives Considered

### Microsoft.CodeAnalysis.Sarif.Driver
- Rejected: package is heavy (~5MB), has many transitive dependencies, and update cadence is unpredictable
- The package is primarily designed for Microsoft's own tools (BinSkim, etc.) and carries features we don't need

### Third-party SARIF libraries
- Rejected: ecosystem is small, most libraries are unmaintained or have incompatible licensing

## Implementation Notes

Our SARIF implementation consists of:
- Immutable `record` types in `OpenVulScan.Sarif` namespace
- `System.Text.Json` serialization with camelCase naming
- `SarifWriter` orchestrating the construction of `SarifLog`, `SarifRun`, `SarifResult`, etc.
- Snapshot tests verifying output structure against `.verified.txt` baselines
