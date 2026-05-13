# Rule3001 Spike

Spike for detecting V3001: left and right operands of a binary expression are structurally identical.

## Design Notes

- Uses Roslyn IOperation tree for semantic-aware comparison.
- Only flags binary operators where both operands are structurally equal (e.g. `a == a`, `a + a`).
- Explicitly excludes assignment (`=`).
- Includes arithmetic, logical, bitwise, relational, and shift operators.
- Structural equality recursively compares operand kinds and symbols.

### Key Decisions

- **Identity conversions only**: Parentheses and identity conversions (`(int)a` where `a` is already `int`) are unwrapped. Non-identity conversions are preserved to avoid false positives like `(short)a == (int)a`.
- **Fail closed**: Unhandled IOperation kinds return `false` instead of falling back to syntax text comparison.
- **Symbol equality**: Invocations compare `TargetMethod` symbol equality; fields/properties compare `Name` + `ContainingType`.
- **Object creation**: `new Foo() == new Foo()` is handled by comparing `Constructor` and `Arguments`.

## Run Instructions

```bash
cd spikes/Rule3001
dotnet run -- <path-to-cs-file>
```

### Examples

```bash
# Detect positives
dotnet run -- test-files/positives.cs

# Verify no false positives on negatives
dotnet run -- test-files/negatives.cs
```

## Test Files

- `test-files/positives.cs` – 5 expected detections
- `test-files/negatives.cs` – 0 expected detections
