# Spike: MSBuild Solution Loader

## Context
Phase 0 R&D for OpenVulScan — verifying that `MSBuildWorkspace` can load large real-world C# solutions.

## Goal
Create a console app in `spikes/MsbuildLoader/` that loads a `.sln` via `MSBuildWorkspace` + `MSBuildLocator`, obtains `Compilation` for each project, and logs metrics.

## Metrics
- Number of projects loaded
- Number of compilations obtained
- Number of diagnostics
- Peak RAM
- Elapsed time

## Reference Projects
1. dotnet/roslyn (HEAD pinned)
2. dotnet/aspnetcore (HEAD pinned)
3. MonoGame

## Approach
Single console app accepting a solution path as CLI argument. `MSBuildLocator` registers the system MSBuild instance before `MSBuildWorkspace` opens the solution. Each project is loaded, compilation retrieved, and diagnostics counted. `GC.GetTotalMemory(true)` used for peak RAM approximation; `Stopwatch` for elapsed time.

## Acceptance Criteria
- Spike runs on the 3 reference projects without crashing
- README documents numerical results (LoC, RAM peak, time)
- Known problematic projects documented

## Out of Scope
- Actual cloning of repos in this session (documented in README instead)
- Permanent CI integration
