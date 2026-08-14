# Summary Cache Format

Binary format for persisted per-method summaries (`MethodSummary`), produced and consumed
by `SummarySerializer` in `OpenVulScan.Cache`. Used by Phase 3+ inter-procedural analyses
to reuse callee facts across runs without re-analyzing method bodies.

## Encoding

- **Serializer:** [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp),
  `StandardResolver` semantics with `Lz4BlockArray` compression.
- **Layout:** int-keyed `[MessagePackObject]` DTOs (arrays, not string-keyed maps) — compact
  and order-stable.

## Envelope

The file is a single MessagePack object:

| Key | Field           | Type                | Notes                                   |
|-----|-----------------|---------------------|-----------------------------------------|
| 0   | `FormatVersion` | `int`               | Currently **1**. See versioning policy. |
| 1   | `Summaries`     | `MethodSummary[]`   | May be empty.                           |

## MethodSummary record

| Key | Field               | Type                     | Meaning                                                                 |
|-----|---------------------|--------------------------|-------------------------------------------------------------------------|
| 0   | `MethodId`          | `string`                 | Roslyn documentation-comment ID (`M:Ns.Type.Method(...)`).              |
| 1   | `ReturnNullability` | `byte` (`NullState`)     | Nullability of the return value.                                        |
| 2   | `OutParameters`     | `ParameterNullability[]` | Nullability of `out`/`ref` parameters on return.                        |
| 3   | `Throws`            | `string[]`               | Fully-qualified metadata names of exception types the method may throw. |
| 4   | `IsPure`            | `bool`                   | No observable side effects.                                             |
| 5   | `TaintPassThrough`  | `int[]`                  | Argument ordinals whose value flows to the return value (Phase 4).      |

## ParameterNullability record

| Key | Field      | Type                 | Meaning                                  |
|-----|------------|----------------------|------------------------------------------|
| 0   | `Position` | `int`                | Zero-based ordinal in the parameter list. |
| 1   | `State`    | `byte` (`NullState`) | Nullability on method return.             |

`NullState` values: `0 Unknown`, `1 DefinitelyNull`, `2 NotNull`, `3 MaybeNull`
(`OpenVulScan.Core`, `Lattice/NullStateLattice.cs`).

## Method identity

Methods are keyed by documentation-comment ID — the only symbol identity stable across
compilations. It survives rebuilds, is unambiguous for overloads, and can be resolved back
to a symbol with `DocumentationCommentId.GetFirstSymbolForDeclarationId`.

## Versioning policy

- `FormatVersion` is bumped on **any** change to field meaning, numbering, or the envelope.
- Readers reject a file whose version differs from the one they were built for by throwing
  `InvalidDataException` — a stale cache is discarded and rebuilt, never partially read.
- New fields must take **new** keys; keys are never reused or renumbered within a version's
  lifetime.
