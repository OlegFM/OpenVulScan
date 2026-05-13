# Null Spike Test Cases

This document describes the 5 test cases used to validate the NullStateLattice spike.

## Test Cases

### 1. NullLiteral

```csharp
public void NullLiteral()
{
    string x = null;
}
```

**Expected:** `x` → `DefinitelyNull`

### 2. NonNullLiteral

```csharp
public void NonNullLiteral()
{
    string x = "hello";
}
```

**Expected:** `x` → `NotNull`

### 3. VariableAssignment

```csharp
public void VariableAssignment(string? input)
{
    string x = input;
}
```

**Expected:** `x` → `Unknown` (parameter state is unknown)

### 4. ConditionalAccess

```csharp
public void ConditionalAccess(string? input)
{
    var x = input?.Length;
}
```

**Expected:** `x` → `Unknown` (naive: Roslyn lowers conditional access into FlowCapture operations)

### 5. BranchingJoin

```csharp
public void BranchingJoin(bool flag)
{
    string x;
    if (flag)
    {
        x = null;
    }
    else
    {
        x = "hello";
    }
}
```

**Expected:** `x` → `MaybeNull` (join of DefinitelyNull and NotNull)

## Lattice Definition

```
          MaybeNull (⊤)
           /       \
   DefinitelyNull  NotNull (!)
           \       /
          Unknown (?)
```

- **Join(⊥, !)** = ⊤
- **Join(?, x)** = x
- **Join(x, x)** = x
