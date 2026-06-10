# SSA-нумерация для OpenVulScan.Core

- **Дата:** 2026-06-05
- **Beads issue:** `ovs-2qi.9`
- **Эпик:** `ovs-2qi` (Фаза 2. Внутрипроцедурный DFA + path-sensitive)
- **Статус:** Approved (design), pending implementation

## 1. Контекст и мотивация

Решётки `NullStateLattice` / `ConstantLattice` и `WorklistSolver<T>` уже реализованы. Существующий `NullStateMapTransfer` (а вслед за ним `ConstantMapTransfer`) трекает переменные по строковому ключу `ImmutableDictionary<string, NullState>`. Это даёт три проблемы:

1. **Shadowing.** Две локальные переменные с одинаковым именем в разных областях сливаются в одну запись.
2. **Reassignment.** После `x = expr1; if (cond) x = expr2;` мы не различаем «x до if» и «x после if» — теряется поточечная точность.
3. **Поля.** `this.field` сейчас вообще не трекается. Это нужно для V3105/V3142 и других правил из набора `ovs-2qi.15`.

SSA-нумерация решает все три:

- Каждое определение (`def`) переменной получает уникальную версию.
- На блоках с несколькими предшественниками вставляются явные φ-функции — `result_v3 = φ(x_v1, x_v2)`.
- `this.field` участвует в нумерации с прагматичной инвалидацией на вызовах методов.

SSA — фундамент для последующих NRE-правил (`ovs-2qi.14`, `ovs-2qi.15`) и Dispose-правил (`ovs-2qi.17`).

## 2. Решения дизайна

| Решение | Выбор | Обоснование |
|---|---|---|
| Scope нумеруемых | locals + parameters + `this.field` | Минимальная heap-модель без alias-анализа. Достаточно для большинства правил Phase 2. |
| Модель φ | Explicit φ в `SsaIndex` | Правила видят, из какой ветви какая версия пришла. Совместимо с уже существующим `IEdgeRefiner`. |
| Алгоритм размещения φ | Semi-pruned SSA | φ ставится на блок с ≥2 predecessors для переменных с ≥2 def-сайтами глобально. Без вычисления dominance frontiers. Лишние φ возможны, корректность сохраняется. |
| CaptureId | Unified `SsaId`, вид `Capture` | Один API для правил. ~~Roslyn `IFlowCaptureOperation` уже даёт SSA-нумерацию temps — оборачиваем без перенумерации.~~ **Superseded by ovs-tr6 S-2 (2026-06-10):** предпосылка «один def на capture» ложна для `??`/`?:` (один CaptureId в обеих ветках) — captures теперь обычные versioned defs с φ; см. ssa-precision-hardening-design §3.2. |
| Миграция transfers | Map-transfers переводятся на `SsaId` | `NullStateMapTransfer` и `ConstantMapTransfer` заменяются на SSA-aware версии. Тесты адаптируются. |
| `this.field` после method call | Инвалидация (новая версия = Top) | Sound over-approximation без summaries. |

## 3. Архитектура

```
ControlFlowGraph (Roslyn)
  │
  ▼
SsaBuilder.Build(cfg, semanticModel)           ◄── pre-pass
  │
  ▼
SsaIndex (immutable)
  ├ DefinitionAt(IOperation) → SsaId?
  ├ UseAt(IOperation, TrackedKey) → SsaId?
  ├ EntryVersions(BasicBlock) → IReadOnlyDictionary<TrackedKey, SsaId>
  ├ PhisAt(BasicBlock) → IReadOnlyList<Phi>
  └ AllVersions(TrackedKey) → IReadOnlyList<SsaId>
  │
  ▼
WorklistSolver<TLattice>  (без изменений; SSA скрыта в transfer-е)
  │
  ▼
DataFlowRuleDispatcher
  └ передаёт SsaIndex в DataFlowContext
```

`SsaBuilder` — чистый функциональный pre-pass без побочных эффектов. `SsaIndex` иммутабелен, безопасен для параллельного чтения.

## 4. Публичный API

```csharp
namespace OpenVulScan;

// Идентификатор отслеживаемого хранилища.
public abstract record TrackedKey
{
    public sealed record Symbol(ISymbol Variable) : TrackedKey;       // local или parameter
    public sealed record InstanceField(IFieldSymbol Field) : TrackedKey;  // this.x
    public sealed record Capture(CaptureId Id) : TrackedKey;
}

// ВАЖНО: Roslyn ISymbol.Equals по умолчанию — reference, а не семантическая.
// TrackedKey.Symbol и TrackedKey.InstanceField должны переопределить Equals/GetHashCode
// через SymbolEqualityComparer.Default. Иначе reused symbols в разных compilation
// instances будут давать разные ключи.

// Конкретная версия отслеживаемого хранилища.
public readonly record struct SsaId(TrackedKey Key, int Version);

// φ-функция: новая версия = join всех вариантов из predecessors.
public sealed record Phi(SsaId Result, ImmutableArray<PhiOperand> Operands);

public readonly record struct PhiOperand(BasicBlock PredecessorBlock, SsaId Version);

public sealed class SsaIndex
{
    // Возвращает версию, которую данная операция определяет (для assignments / declarators).
    // null — операция не является definition.
    public SsaId? DefinitionAt(IOperation op);

    // Возвращает действующую версию ключа в точке использования.
    // null — ключ не в скоупе или не отслеживается.
    public SsaId? UseAt(IOperation op, TrackedKey key);

    // Версии на entry в блок (после φ).
    public IReadOnlyDictionary<TrackedKey, SsaId> EntryVersions(BasicBlock block);

    // φ-узлы блока (пустой список, если их нет).
    public IReadOnlyList<Phi> PhisAt(BasicBlock block);

    // Все версии данного ключа, существующие в методе.
    public IReadOnlyList<SsaId> AllVersions(TrackedKey key);
}
```

## 5. Алгоритм построения (semi-pruned)

### Pass 1 — сбор def-сайтов

Обход всех операций каждого блока. Регистрируется `(IOperation def, TrackedKey)` для:

- `IVariableDeclaratorOperation` (с инициализатором или без) → `Symbol(ILocalSymbol)`.
- `ISimpleAssignmentOperation` / `ICompoundAssignmentOperation` с target `ILocalReferenceOperation` → `Symbol(ILocalSymbol)`.
- То же с target `IParameterReferenceOperation` → `Symbol(IParameterSymbol)`.
- То же с target `IFieldReferenceOperation` где `Instance is IInstanceReferenceOperation` (т.е. `this.f = ...`) → `InstanceField(IFieldSymbol)`.
- `IFlowCaptureOperation` → `Capture(CaptureId)`.

Параметры функции считаются def-сайтами на entry-блоке (версия 0).

После прохода вычисляется множество `globals` — ключи с ≥2 def-сайтами.

### Pass 2 — размещение φ и нумерация

Обход блоков в reverse postorder.

Для каждого блока:

1. **Entry.** Если у блока ≥2 predecessors и ключ ∈ `globals` → создаётся `Phi { Result = SsaId(key, NextVersion(key)), Operands = пусто пока }`. Размер `Operands` будет N predecessors после Pass 3.
   Иначе:
   - 0 predecessors → блок недостижим или entry; для entry — `version = 0` для всех параметров и Top для остальных при первом use.
   - 1 predecessor → entry-версия = out-версия единственного pred.

2. **Operations.** Для каждой операции в `block.Operations` и для `block.BranchValue`:
   - Если операция — def-сайт (см. Pass 1) → выдаётся новая версия, регистрируется в `DefinitionAt`.
   - Если операция — `IInvocationOperation` или `IObjectCreationOperation` с потенциальным side-effect на `this` → kill всех активных `InstanceField` версий (выдаётся новая версия каждому ключу). См. §6.1 ниже.
   - Если операция — `IFlowCaptureOperation` → версия 0 (Roslyn-уникальна).
   - Если операция — use (ссылка на отслеживаемый ключ) → регистрируется в `UseAt` с текущей версией.

3. **Exit.** Сохраняется `outVersions[block] : TrackedKey → SsaId`.

### Pass 3 — связывание операндов φ

Pass 3 запускается **только после полного завершения Pass 2 по всем блокам CFG** — к этому моменту `outVersions[block]` определены для каждого блока, включая back-edge predecessors. Это гарантирует, что версии всегда есть, и отдельная итерация до стабилизации не требуется.

Для каждого размещённого `Phi`:
- Для каждого predecessor `p` блока: `Operands` дополняется `PhiOperand(p, outVersions[p][key])`.
- Если у `p` ключ ни разу не определялся (use-before-def на этом пути) → operand-версия = entry-версия метода для этого ключа (для параметров — версия 0; для остальных — Top через явный sentinel или отсутствие в map).

Pass 3 однопроходный: версии — статические идентификаторы, lattice-значения вычислит solver.

### 5.1 Сложность

- Pass 1: O(|operations|).
- Pass 2: O(|operations| + |blocks| × |globals|).
- Pass 3: O(|phis| × |predecessors|).

В типичной функции `|operations| < 1000`, `|globals| < 50`, `|blocks| < 200`. Безопасный bound для intra-procedural.

## 6. Особые случаи

### 6.1 Инвалидация `this.field` на вызовах

При встрече `IInvocationOperation` или `IObjectCreationOperation`:

- Прагматичное правило: **любой нестатический invocation, имеющий доступ к `this`**, считается потенциально kills `this.field`. Сюда попадают:
  - `this.M(...)` — receiver `this` или `IInstanceReferenceOperation`.
  - `M(...)` без явного receiver внутри instance-метода — implicitly `this.M(...)`.
  - `someObj.M(this, ...)` — `this` передан аргументом.
- **Статические методы** (`Method.IsStatic`) без передачи `this` аргументом не считаются kills.
- «Активные `InstanceField`» = все ключи `TrackedKey.InstanceField`, для которых уже был def-сайт ранее в методе. Каждому такому ключу выдаётся новая версия; lattice-значение этой версии после kill — `Top` (для `NullState` это `Unknown`).
- Это sound over-approximation: возможны лишние kills для чистых методов (`Math.Sqrt`), но без summaries иначе невозможно гарантировать корректность.

Локальные переменные kills не подвергаются — они недоступны из вызываемого кода (для замыканий это допущение нарушается; lambda capture выносится за scope тикета).

### 6.2 Captures

`IFlowCaptureOperation` создаёт SSA-temp с `CaptureId`. ~~Версия всегда 0, поскольку Roslyn гарантирует SSA-форму для captures (одно def на capture).~~ **Superseded by ovs-tr6 S-2:** для `??`/`?:` один CaptureId получает def в обеих ветках — captures нумеруются как обычные defs и получают φ (см. ssa-precision-hardening-design §3.2). `IFlowCaptureReferenceOperation` — use этого capture.

### 6.3 Shadowing

`ILocalSymbol` различает локали с одним именем в разных скоупах — они получают разные `TrackedKey.Symbol` (Roslyn сам нумерует символы). Никакого extra handling не требуется.

### 6.4 Циклы

Back-edge на header цикла: Pass 3 связывает operand φ с out-версией predecessor'а (включая back-edge). Это и есть стандартный SSA loop pattern: φ на header'е имеет два operand'а — entry version и back-edge version.

### 6.5 Switch с N case

φ на merge-блоке после switch имеет N operand'ов — по одному на каждый достижимый case-блок плюс default. Никакой специальной обработки — обычный multi-predecessor merge.

## 7. Интеграция

### 7.1 NullStateSsaTransfer (заменяет NullStateMapTransfer)

```csharp
public sealed class NullStateSsaTransfer : ITransfer<ImmutableDictionary<SsaId, NullState>>
{
    private readonly SsaIndex _ssa;

    public NullStateSsaTransfer(SsaIndex ssa) { _ssa = ssa; }

    public ImmutableDictionary<SsaId, NullState> Apply(... state, IOperation op)
    {
        // если op = def-сайт → state.SetItem(_ssa.DefinitionAt(op).Value, evaluated)
        // если op = kill (метод-вызов с this) → invalidate this.field versions
        // иначе state без изменений
    }

    public ImmutableDictionary<SsaId, NullState> Apply(... state, BasicBlock block)
    {
        // применить φ на entry: для каждого φ в PhisAt(block) →
        //   newValue = Join всех operand-versions из state
        //   state = state.SetItem(phi.Result, newValue)
        // далее применить все operations блока
    }
}
```

### 7.2 ConstantSsaTransfer — симметрично

### 7.3 DataFlowRuleDispatcher

```csharp
foreach (var rule in _rules)
{
    var ssaIndex = SsaBuilder.Build(cfg, model);  // кешируется per (cfg, model) если потребуется
    var transfer = rule.CreateTransfer(ssaIndex); // правила знают, как обернуть свой transfer с SSA
    var solver = new WorklistSolver<TLattice>(rule.Lattice, transfer, rule.EdgeRefiner);
    var result = solver.Solve(cfg, cancellationToken);

    foreach (var block in cfg.Blocks)
    {
        var state = result.InStates[block];
        foreach (var op in GetAllOperations(block))
        {
            var context = new DataFlowContext(op, model, _compilation, ssaIndex, cancellationToken);
            rule.InvokeOnState(op, state, context);
            diagnostics.AddRange(context.Diagnostics);
            state = transfer.Apply(state, op);
        }
    }
}
```

### 7.3.1 Breaking change в DataFlowRule

`DataFlowRule<TLattice>` меняет API:

```csharp
// Было:
public abstract ITransfer<TLattice> Transfer { get; }

// Становится:
public abstract ITransfer<TLattice> CreateTransfer(SsaIndex ssaIndex);
public virtual IEdgeRefiner<TLattice>? CreateEdgeRefiner(SsaIndex ssaIndex) => EdgeRefiner;
// EdgeRefiner property сохраняется как удобная переменная-обёртка для правил, которым SSA не нужна.
```

Это breaking change для существующих рулз V3022 и V3063. План миграции:

1. V3022 (`AlwaysTrueFalse`) и V3063 (`PartialAlwaysTrueFalse`) — DataFlowRule<ConstantLatticeValue>. Они переопределяют `CreateTransfer(ssaIndex) => new ConstantSsaTransfer(ssaIndex)`.
2. Snapshot-тесты этих правил должны остаться зелёными — `ConstantSsaTransfer` на ключе `SsaId` даёт ту же propagation для не-shadowed/не-condition-reassigned случаев.
3. Если какой-то snapshot ломается — это сигнал, что старый transfer был некорректен в граничном случае; разбираемся точечно и обновляем snapshot после ручной верификации.

### 7.4 DataFlowContext

Добавляется свойство `SsaIndex SsaIndex { get; }`. Правила могут вызывать `context.SsaIndex.UseAt(op, key)` чтобы узнать конкретную версию переменной в точке использования.

## 8. Файлы

```
src/OpenVulScan.Core/Ssa/
  TrackedKey.cs
  SsaId.cs
  Phi.cs
  SsaIndex.cs
  SsaBuilder.cs

src/OpenVulScan.Core/Lattice/
  NullStateSsaTransfer.cs       (новый, заменяет NullStateMapTransfer)
  ConstantSsaTransfer.cs        (новый, заменяет ConstantMapTransfer)

src/OpenVulScan.RuleEngine/
  DataFlowContext.cs            (+ SsaIndex)
  DataFlowRule.cs               (+ CreateTransfer(SsaIndex), Transfer становится default-implementation)
  DataFlowRuleDispatcher.cs     (строит SsaIndex и пробрасывает)

tests/OpenVulScan.Core.Tests/Ssa/
  SsaBuilderIfElseTests.cs
  SsaBuilderLoopTests.cs
  SsaBuilderSwitchTests.cs
  SsaBuilderFieldKillTests.cs
  SsaBuilderCaptureTests.cs
  SsaBuilderShadowingTests.cs
  SsaBuilderEdgeCasesTests.cs   (unreachable, empty body, recursion, ...)
```

Старые файлы `NullStateMapTransfer.cs` и `ConstantMapTransfer.cs` удаляются вместе с их тестами. V3022 / V3063 snapshot-тесты должны оставаться зелёными.

## 9. Тесты

Минимум 8 unit-тестов на SSA (acceptance-criteria требует if-else/while/switch):

1. **if-else, простая reassignment.** `int x = 0; if (c) x = 1; else x = 2; use(x);` — две версии в branches, φ на merge.
2. **if без else, частичная reassignment.** `int x = 0; if (c) x = 1; use(x);` — φ на merge с двумя operand-версиями.
3. **while.** `int x = 0; while (c) { x++; }` — φ на header, операнды: entry-версия и back-edge версия.
4. **switch.** 3 case + default → φ с 4 operand'ами на merge.
5. **nested control-flow.** `if` внутри `while` — корректная цепочка φ на двух уровнях.
6. **this.field kill.** `this.f = a; this.M(); use(this.f);` — после `M()` у `this.f` новая версия.
7. **capture.** `(x ?? d).Foo()` — `IFlowCapture` обёрнут в `SsaId(Capture(id), 0)`.
8. **shadowing.** Два разных `ILocalSymbol` с одним именем не пересекаются.

Плюс интеграционные:

- **V3022/V3063 snapshot regression** — обновлённый transfer не ломает существующие правила.
- **Migration smoke** — `NullStateSsaTransfer` на простом if-else даёт те же diagnostics, что и старый `NullStateMapTransfer` (где переменная не shadowed и не reassigned условно — там семантика идентична).

## 10. Acceptance criteria (из beads)

- [ ] SSA-индексация per-block per-variable работает.
- [ ] φ-функции на merge-блоках материализованы как `Phi` записи.
- [ ] `CaptureId` интегрирован в `SsaId`.
- [ ] Unit-тесты для if-else, while, switch зелёные.
- [ ] V3022 / V3063 snapshot-тесты остаются зелёными после миграции на SSA-transfer.

## 11. Out of scope

- Heap-модель и alias-анализ (для `someObj.field`, массивов, indexed properties) — будущая фаза.
- Capture в lambdas / local functions — будущая фаза.
- Pruned SSA через live-variable analysis — оптимизация, не критична для intra-procedural < 1000 операций.
- Доминаторы (Lengauer-Tarjan) — semi-pruned обходится без них.
- Сериализация SsaIndex для incremental кеша — Phase 3+.

## 12. Риски

| Риск | Митигация |
|---|---|
| Лишние φ из-за semi-pruned | Lattice join делает их корректными; перформанс приемлем на методах < 1000 операций. |
| Чистые методы инвалидируют `this.field` | Sound over-approximation; в будущем заменяется на summaries. |
| Поломка V3022/V3063 при миграции transfer | Snapshot-тесты остаются обязательной частью PR; регрессии видны сразу. |
| Замыкания (lambdas) захватывают локальные — SSA для них вне scope | Документировано как out-of-scope; правила не пытаются делать выводы про захваченные переменные. |
