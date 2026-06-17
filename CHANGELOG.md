# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **V3114** — flags an `IDisposable` local created with `new` that is not disposed on all paths
  before the method returns (full leak and partial dispose). Excludes `using` variables and
  resources whose ownership escapes (returned / stored to a field or property / passed as an
  argument / captured by a lambda or local function). CWE-404, Level 2.
- **V3073** — flags `IDisposable` instance fields that a class's `Dispose` method does not dispose
  on all paths (including disposed-on-one-branch-only). CWE-404, Level 0.
- **V3178** — flags invoking a member of, accessing a property/field of, or re-disposing an object
  that is potentially disposed on a reaching path (MAY semantics, "potentially disposed").
  CWE-672, Level 1.
- **ResourceOwnershipLattice** — a three-element chain lattice (`Untracked ⊏ Disposed ⊏ Open`) with
  the leak-dangerous `Open` state as top (⊤), so a partial dispose survives control-flow joins and
  can be reported.

### Notes

- The leak rules count only explicit developer `Dispose()` calls; `using` is treated as disposed by
  construction. Roslyn lowers `using`/`try-finally` into exception (`StructuredExceptionHandling`)
  edges that the forward worklist solver does not traverse, so `DisposeFlow` compensates with a
  finally-dispose pre-filter. Known v1 limitations: a conditional dispose inside a `finally`, the
  virtual `Dispose(bool)`/`base.Dispose()` pattern, factory-created disposables, and `await using`
  are tracked as follow-ups.
