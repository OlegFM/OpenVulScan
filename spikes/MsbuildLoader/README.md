# Spike: MSBuild Loader

Console app that loads a C# solution via `MSBuildWorkspace` + `MSBuildLocator`, obtains `Compilation` for each project, and logs metrics.

## Usage

```bash
dotnet run --project spikes/MsbuildLoader/MsbuildLoader.csproj -- <path-to-sln>
```

## Example

```bash
dotnet run --project spikes/MsbuildLoader/MsbuildLoader.csproj -- ../roslyn/Roslyn.sln
```

## Example Run on OpenVulScan.slnx

Run from repository root:

```bash
dotnet run --project spikes/MsbuildLoader/MsbuildLoader.csproj -- OpenVulScan.slnx
```

**Output:**

```
Loading solution: OpenVulScan.slnx
  Project: OpenVulScan.Core (E:\Work\OpenVulScan\src\OpenVulScan.Core\OpenVulScan.Core.csproj)
    -> Compilation OK, diagnostics: 13
  Project: OpenVulScan.Core.Tests (E:\Work\OpenVulScan\tests\OpenVulScan.Core.Tests\OpenVulScan.Core.Tests.csproj)
    -> Compilation OK, diagnostics: 15

=== Results ===
Projects loaded:       2
Compilations obtained: 2
Total diagnostics:     28
Total LoC:             91
Elapsed time:          00:00:04.4456675
Peak managed memory:   12 MB
Peak working set:      139 MB
```

| Metric | Value |
|--------|-------|
| Projects | 2 |
| Compilations | 2 |
| Diagnostics | 28 |
| LoC | 91 |
| Elapsed | 00:00:04.45 |
| Peak managed memory | 12 MB |
| Peak working set | 139 MB |

## Cloning Reference Projects

> **Note:** Results for the three large reference projects below will be populated after corpus setup (separate task).

```bash
# dotnet/roslyn
git clone https://github.com/dotnet/roslyn.git
cd roslyn
git checkout 7edffc0598465e31a83b1e2cef90a7a3e1959912   # HEAD of main branch as of 2026-05-13
cd ..

# dotnet/aspnetcore
git clone https://github.com/dotnet/aspnetcore.git
cd aspnetcore
git checkout c405a576d1f35b02fdcb7bf09af7b60ec8d1151e   # HEAD of main branch as of 2026-05-13
cd ..

# MonoGame
git clone https://github.com/MonoGame/MonoGame.git
cd MonoGame
git checkout d84b36c533d96515374034e44ec4f226ea401729   # HEAD of main branch as of 2026-05-13
cd ..
```

## Results (Reference Projects)

Run the loader against each solution and paste results below:

### dotnet/roslyn

| Metric | Value |
|--------|-------|
| Projects | TBD |
| Compilations | TBD |
| Diagnostics | TBD |
| LoC | TBD |
| Elapsed | TBD |
| Peak RAM | TBD |

### dotnet/aspnetcore

| Metric | Value |
|--------|-------|
| Projects | TBD |
| Compilations | TBD |
| Diagnostics | TBD |
| LoC | TBD |
| Elapsed | TBD |
| Peak RAM | TBD |

### MonoGame

| Metric | Value |
|--------|-------|
| Projects | TBD |
| Compilations | TBD |
| Diagnostics | TBD |
| LoC | TBD |
| Elapsed | TBD |
| Peak RAM | TBD |

## Synthetic Benchmarks

Three synthetic test solutions were created under `test-solutions/` to establish baseline metrics.

### Small

- **1 project** (class library)
- **~45 LoC**

| Metric | Value |
|--------|-------|
| Projects | 1 |
| Compilations | 1 |
| Diagnostics | 4 |
| LoC | 45 |
| Elapsed | 00:00:03.63 |
| Peak managed memory | 11 MB |
| Peak working set | 131 MB |

### Medium

- **3 projects** (class lib + console app + xUnit tests)
- **~540 LoC total**

| Metric | Value |
|--------|-------|
| Projects | 3 |
| Compilations | 3 |
| Diagnostics | 14 |
| LoC | 540 |
| Elapsed | 00:00:04.83 |
| Peak managed memory | 16 MB |
| Peak working set | 149 MB |

### Large

- **5 projects** (4 class libs with cross-references + console app)
- **~2,864 LoC total**

| Metric | Value |
|--------|-------|
| Projects | 5 |
| Compilations | 5 |
| Diagnostics | 23 |
| LoC | 2,864 |
| Elapsed | 00:00:05.79 |
| Peak managed memory | 18 MB |
| Peak working set | 157 MB |

> **Note:** Real-world results for `dotnet/roslyn`, `dotnet/aspnetcore`, and `MonoGame` will be added during corpus setup (task ovs-l0e.2).

## Known Issues

| Project | Issue | Notes |
|---------|-------|-------|
| dotnet/roslyn | Large solutions like dotnet/roslyn may require significant RAM (>4GB) and time (>5 min) for initial load | Depends on machine specs and MSBuild cache state |
| General | `MSBuildWorkspace` may report workspace diagnostics for unsupported project types or missing SDKs | Ensure required .NET SDKs are installed |
