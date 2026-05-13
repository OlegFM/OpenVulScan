# CfgPlay Spike

Spike для исследования API Roslyn `ControlFlowGraph` через `IOperation`.

## Запуск

```bash
cd spikes/CfgPlay
dotnet run -- <path-to-cs-file>
```

Пример:
```bash
dotnet run -- test-files/01-if-else.cs
dotnet run -- test-files/02-loops.cs
dotnet run -- test-files/03-try-catch.cs
dotnet run -- test-files/04-switch.cs
dotnet run -- test-files/05-nullables.cs
```

## Архитектура

Консольное приложение:
1. Парсит `.cs` файл через `CSharpSyntaxTree.ParseText`
2. Создает `CSharpCompilation` и получает `SemanticModel`
3. Находит все `MethodDeclarationSyntax`
4. Для каждого метода вызывает `semanticModel.GetOperation(method)`
5. Если результат — `IMethodBodyOperation`, создает `ControlFlowGraph.Create(operation)`
6. Печатает блоки, ребра и операции

## Тестовые файлы

| Файл | Паттерн | Методы |
|---|---|---|
| `01-if-else.cs` | Простое if/else | `SimpleIfElse`, `InstanceMethod` |
| `02-loops.cs` | Циклы for/while | `ForLoop`, `WhileLoop` |
| `03-try-catch.cs` | Try/catch/finally | `TryCatchFinally` |
| `04-switch.cs` | Switch expression / pattern matching | `SwitchExpression`, `SwitchPattern` |
| `05-nullables.cs` | Nullable и conditional access | `ConditionalAccess`, `NullCoalescing`, `NullConditionalAssignment` |

## Ключевые сущности

### BasicBlock

Блок управления потоком. Каждый метод имеет, как минимум, Entry и Exit блоки.

- `Ordinal` — порядковый номер блока в графе
- `Kind` — `Entry`, `Exit` или `Block`
- `Operations` — список операций в блоке (выполняются последовательно)
- `BranchValue` — условие, определяющее ветвление (если есть `ConditionalSuccessor`)
- `FallThroughSuccessor` — ребро, по которому управление переходит по умолчанию
- `ConditionalSuccessor` — ребро, по которому управление переходит при истинности `BranchValue`
- `EnclosingRegion` — регион, в котором находится блок (Try, Catch, Finally, LocalLifetime и т.д.)

### ControlFlowBranch

Ребро графа потока управления.

- `Destination` — целевой блок (может быть `null`, например, при `Throw`)
- `Semantics` — семантика перехода:
  - `Regular` — обычный переход
  - `Return` — возврат из метода
  - `Throw` — выброс исключения
  - `StructuredExceptionHandling` — переход из finally

### CaptureId

Внутренний идентификатор Roslyn для `FlowCapture`. Используется для связывания `FlowCaptureOperation` (захват значения) и `FlowCaptureReferenceOperation` (использование захваченного значения).

В API Roslyn 5.0 `CaptureId` — `internal readonly struct` с приватным полем `_id`. Для отображения используем reflection.

## Наблюдения

### ConditionalEdge vs FallThrough

В простом if/else (`01-if-else.cs`):

```
Block [1]: Block
  BranchValue:
    Binary Type=Boolean
      ParameterReference Type=Int32
      Literal Type=Int32
  FallThroughSuccessor -> [2]     // then branch (x > 0)
    Semantics: Regular
  ConditionalSuccessor -> [3]     // else branch
    Semantics: Regular
```

**Важно:** в Roslyn `FallThroughSuccessor` — это путь при истинности условия, а `ConditionalSuccessor` — при ложности. Это противоположно интуитивному ожиданию для `if`, но соответствует логике "fall through to next block if condition is true".

### FlowCapture / FlowCaptureReference

FlowCapture используется для вычисления выражения один раз и повторного использования результата. Особенно заметен в:

1. **Switch expression** (`04-switch.cs`) — захват значения switch-аргумента, затем многократное использование через `FlowCaptureReference` в каждом `IsPattern`:

```
Operations:
  FlowCapture Id=0
    ParameterReference Type=Int32
BranchValue:
  IsPattern Type=Boolean
    FlowCaptureReference Id=0 Type=Int32
    ConstantPattern
      Literal Type=Int32
```

2. **Null-conditional операторы** (`05-nullables.cs`) — каждый ш conditional access создает `FlowCapture` для результата предыдущего шага:

```
Operations:
  FlowCapture Id=0
    ParameterReference Type=String
BranchValue:
  IsNull Type=Boolean
    FlowCaptureReference Id=0 Type=String
```

### ImplicitInstance

При доступе к полю/методу экземпляра появляется `InstanceReference` с `ReferenceKind=ContainingTypeInstance`:

```
FieldReference Type=Int32
  InstanceReference ReferenceKind=ContainingTypeInstance Type=TestIfElse
```

Это представляет неявный `this` в вызовах instance-членов.

### Try/Catch/Finally

В `03-try-catch.cs` видны регионы:
- `Try` — блок тела try
- `Catch` — блоки обработчиков
- `Finally` — блок finally

Блок finally имеет `FallThroughSuccessor -> []` с `Semantics: StructuredExceptionHandling`, что означает "выйти из структуры обработки исключений" (не обычный переход).

### Циклы

В `02-loops.cs` видна структура цикла:
- Блок инициализации
- Блок условия с `ConditionalSuccessor` на выход и `FallThroughSuccessor` на тело
- Тело цикла с `FallThroughSuccessor` обратно на условие

## Примеры вывода

Полные выводы для всех тестовых файлов сохранены в `test-output/`.

Краткий пример — if/else:

```
=== CFG for method: SimpleIfElse ===
Blocks: 5

Block [0]: Entry
  Operations: (none)
  FallThroughSuccessor -> [1]
    Semantics: Regular

Block [1]: Block
  BranchValue:
    Binary Type=Boolean
      ParameterReference Type=Int32
      Literal Type=Int32
  FallThroughSuccessor -> [2]
    Semantics: Regular
  ConditionalSuccessor -> [3]
    Semantics: Regular

Block [2]: Block
  BranchValue:
    Literal Type=Int32
  FallThroughSuccessor -> [4]
    Semantics: Return

Block [3]: Block
  BranchValue:
    Unary Type=Int32
      Literal Type=Int32
  FallThroughSuccessor -> [4]
    Semantics: Return

Block [4]: Exit
  Operations: (none)
```

## Выводы

- `ControlFlowGraph` строится из `IMethodBodyOperation` (не из `IBlockOperation`, у которого есть родитель)
- `FlowCapture` — ключевой механизм для оптимизации повторных вычислений
- `InstanceReference` с `ReferenceKind=ContainingTypeInstance` представляет неявный `this`
- Регионы (`EnclosingRegion`) отражают структурные конструкции языка (try/catch/finally, using, локальные переменные)
- `BranchSemantics` позволяет различать обычные переходы, возвраты, throw и обработку исключений
