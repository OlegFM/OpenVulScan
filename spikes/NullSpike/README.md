# NullSpike

Spike для исследования решётки NullState и naive transfer functions на основе Roslyn CFG.

## Запуск

```bash
cd spikes/NullSpike
dotnet run -- test-files/null-cases.cs
```

## Архитектура

Консольное приложение:
1. Парсит `.cs` файл через `CSharpSyntaxTree.ParseText`
2. Создает `CSharpCompilation` и получает `SemanticModel`
3. Находит все `MethodDeclarationSyntax`
4. Для каждого метода:
   - Получает `IMethodBodyOperation`
   - Строит `ControlFlowGraph`
   - Запускает forward dataflow analysis (`NullStateAnalysis`)
   - Выводит финальное состояние локальных переменных

## Решётка NullState

```
          MaybeNull (⊤)
           /       \
   DefinitelyNull  NotNull
           \       /
          Unknown (?)
```

- `Join(Unknown, x) = x`
- `Join(DefinitelyNull, NotNull) = MaybeNull`
- `Join(x, x) = x`

## Transfer Functions (naive)

| Операция | Transfer |
|---|---|
| `x = null` | `x` → `DefinitelyNull` |
| `x = "lit"` | `x` → `NotNull` |
| `x = y` | `x` → `state(y)` или `Unknown` |
| `x = y?.Member` | `x` → `MaybeNull` |
| `x = new T()` | `x` → `NotNull` |

## Тестовые файлы

| Метод | Паттерн | Ожидаемый результат |
|---|---|---|
| `NullLiteral` | Присваивание null | `x`: `DefinitelyNull` |
| `NonNullLiteral` | Присваивание строкового литерала | `x`: `NotNull` |
| `VariableAssignment` | Присваивание параметра | `x`: `Unknown` |
| `ConditionalAccess` | Conditional access `?.` | `x`: `Unknown` (Roslyn lowers to FlowCapture) |
| `BranchingJoin` | If/else с разными ветвями | `x`: `MaybeNull` |

## Ограничения (spike)

- Не отслеживает параметры (считает Unknown)
- Не анализирует сложные выражения (возвращает Unknown или MaybeNull)
- Не обрабатывает циклы корректно (требуется fixed-point iteration)
- Не отслеживает поля — только локальные переменные
- Цель: нащупать API, не точность
