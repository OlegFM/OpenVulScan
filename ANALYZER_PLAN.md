# План работ по созданию статического анализатора C# с целевым паритетом с PVS-Studio

Документ описывает дорожную карту разработки open-source статического анализатора C# силами одного инженера. Целевая функциональная планка — PVS-Studio (релизы 7.4x, начало 2026 г.), 275 диагностических правил для C# плюс инфраструктура SAST/CI.

Источники данных (зафиксированы для воспроизводимости):
- `RulesMap.xml` (PVS-Studio 7.42) — официальный реестр правил, секция `<RuleSet lang="cs">`. Скачан локально, содержит 275 правил для C#.
- Манул PVS-Studio «Технологии, используемые в PVS-Studio (соответствует ГОСТ Р 71207—2024)» — https://pvs-studio.ru/ru/docs/manual/6521/. Описывает обязательные и вспомогательные методы анализа, перечисленные в ГОСТ Р 71207—2024.

---

## 1. Резюме

PVS-Studio для C# — это:
- 275 диагностических правил (4 служебных, 232 General Analysis V3xxx, 8 Unity-микрооптимизаций V40xx, 31 OWASP/SAST V56xx);
- 5 обязательных и 4 вспомогательных метода анализа из ГОСТ Р 71207—2024;
- крупная инфраструктура: SARIF, baseline-подавление, инкрементальный режим, JSON-аннотации API, плагины VS/Rider/CLion/VSCode/IDEA, интеграция MSBuild/CMake/Gradle/Maven, обвязка для всех популярных CI и систем сбора дефектов (SonarQube, DefectDojo, CodeChecker, Jira, …);
- внутренний труд команды ~50 инженеров на протяжении ~17 лет (история проекта с 2008 г.).

Реалистичная цель для одного senior .NET инженера:
- год 1: MVP с 30–50 правилами, AST-уровень, CLI + SARIF + baseline;
- год 2: внутри- и межпроцедурный data-flow, ~120 правил, инкрементальный режим, плагин Rider;
- год 3: path-sensitive анализ, taint-engine, ~25 OWASP правил, MSBuild target, SonarQube exporter;
- год 4: дотягивание до ~200 правил, FP-калибровка на open-source корпусе, alpha-релиз.

Полный паритет (275 правил с сопоставимым уровнем FP) силами одного инженера за разумные сроки недостижим. Документ описывает реалистичный путь к **функциональному паритету ~70–80 %** и сравнимому качеству на ключевых сценариях (NRE, dead code, taint, OWASP Top-10).

---

## 2. Целевая функциональность (scope)

### 2.1 Методы анализа (по ГОСТ Р 71207—2024 §7.4)

| Метод | Обязательность | Цель |
|---|---|---|
| Синтаксический анализ (AST + типизация) | Обязательно | Базовый фундамент. Через Roslyn получаем полную семантическую модель. |
| Внутрипроцедурный анализ потоков данных и управления | Обязательно | CFG, dataflow lattice, fixpoint. Покрывает большую часть V3xxx. |
| Межпроцедурный и межмодульный контекстно-чувствительный DFA | Обязательно | Function summaries, call graph, whole-program analysis. |
| Path-sensitive анализ | Обязательно | Symbolic execution с path conditions. По мануалу PVS-Studio реализован «в едином механизме» с межпроцедурным DFA. |
| Taint analysis (анализ помеченных данных) | Обязательно | Источники → стоки, sanitizers, lattice заражённости. Основа для V56xx OWASP. |
| Сигнатурный поиск | Вспомогательно | Pattern-match по сигнатурам API (например, `Process.Start`, `SqlCommand`). |
| Анализ псевдонимов (alias) | Вспомогательно | Points-to. Нужен для точности DFA в присутствии полей и параметров. |
| Анализ косвенных вызовов | Вспомогательно | Devirtualization для виртуальных вызовов и делегатов. |
| Статистический анализ | Вспомогательно | Эвристики «обычно вот так пишут» (паттерн «инициализация без использования»). |
| Анализ иерархии классов | Вспомогательно | Полная информация о подклассах в проекте + транзитивно по референсам. |

### 2.2 Инфраструктурные функции, ожидаемые на уровне PVS-Studio

- CLI (Linux/macOS/Windows), запуск headless через `dotnet` без Visual Studio.
- Анализ `.sln`/`.csproj` и проектов на `dotnet build`, поддержка SDK-style и legacy MSBuild проектов.
- Инкрементальный анализ (по изменённым файлам + зависимым по graph’у).
- Многопоточный анализ.
- Подавление: baseline-файл (suppression base), inline-комментарии (`// analyzer:disable=Vxxxx`), внешний XML-конфиг (.pvsconfig-аналог).
- Вывод: SARIF 2.1.0 (обязательно), JSON, plain text, HTML report, CSV.
- Mapping CWE / OWASP Top-10 / OWASP ASVS, что нужно по ГОСТ §8.
- JSON-аннотации внешних API (модель функций, источники и санитайзеры).
- Интеграции (по убыванию приоритета): dotnet CLI, MSBuild target, GitHub Actions, GitLab CI, JetBrains Rider 2026+ extension, Visual Studio 2026 extension, SonarQube exporter (Generic Issue Format + SARIF importer). Поддержка устаревших IDE (Rider до 2026, VS 2022 и ранее) явно вне scope.
- Blame-notifier (привязка предупреждения к git-блейму, опционально).
- Аналитика по предупреждениям (CSV/Parquet выгрузка для дашбордов).

### 2.3 Чего сознательно нет в первой версии

- Анализ C/C++/Java/Go/TS/JS (PVS поддерживает несколько ядер — у нас только C#).
- Сертификация ФСТЭК и формальное соответствие ГОСТ. Нацеливаемся на функциональное соответствие методам анализа, не на бумажное.
- Корпоративные web-дашборды (DefectDojo, CodeChecker, AppSec.Hub, Hexway, …). Достаточно SARIF exporter — все эти системы умеют его глотать.
- Плагины VS Code / Qt Creator / VS Mac / VS 2022 и более ранних / Rider до 2026 — целевая IDE-аудитория: только Rider 2026+ и Visual Studio 2026. Это избавляет от необходимости таргетить netstandard2.0 в правилах и multitargeting Roslyn 4.x.

---

## 3. Инвентарь диагностик PVS-Studio для C#

Распределение по группам (источник: `RulesMap.xml`, тег `<RuleSet lang="cs">`):

| Группа | Назначение | Количество | Диапазон кодов |
|---|---|---|---|
| `fails` | Служебные сообщения (не диагностики) | 4 | V009, V051–V053 |
| `ga` | General Analysis (общеязыковые дефекты) | 232 | V3001 … V3239 |
| `op` | Микрооптимизации Unity Engine | 8 | V4001–V4008 |
| `owasp` | SAST / OWASP / taint | 31 | V5601–V5631 |
| **Итого** | | **275** | |

Распределение по `defaultLevel` (1 — наиболее критичные):
- LEVEL-1: 120 правил
- LEVEL-2: 104 правила
- LEVEL-3: 45 правил
- LEVEL-0: 2 правила (с особой обработкой)

Топ-15 CWE, на которые маппятся правила PVS-Studio для C#:

| CWE | Правил | Описание (кратко) |
|---|---|---|
| CWE-476 | 13 | NULL Pointer Dereference |
| CWE-682 | 12 | Incorrect Calculation |
| CWE-691 | 10 | Insufficient Control Flow Management |
| CWE-670 | 10 | Always-Incorrect Control Flow Implementation |
| CWE-570 | 8 | Expression is Always False |
| CWE-571 | 7 | Expression is Always True |
| CWE-628 | 7 | Function Call with Incorrectly Specified Arguments |
| CWE-390 | 6 | Detection of Error Condition Without Action |
| CWE-697 | 5 | Incorrect Comparison |
| CWE-480 | 5 | Use of Incorrect Operator |
| CWE-563 | 5 | Assignment to Variable Without Use |
| CWE-783 | 4 | Operator Precedence Logic Error |
| CWE-821 | 4 | Incorrect Synchronization |
| CWE-686 | 4 | Function Call With Incorrect Argument Type |
| CWE-544 | 4 | Missing Standardized Error Handling |

Тематическая раскладка по 232 правилам General Analysis (cluster-by-keyword, грубая, правила могут попадать в несколько кластеров):

| Кластер | Прим. правил | Что туда уйдёт |
|---|---|---|
| Null-safety | ~28 | NRE при разыменовании, после возврата из метода, в pattern matching, в lambda capture |
| Concurrency / async / lock | ~16 | volatile, double-checked locking, lock на value type, `await` в lock |
| Exception handling | ~30 | пустые catch, swallow, throw без аргументов, ловля Exception без re-throw |
| Resource / IDisposable | ~21 | забытый Dispose, finalizer-suppress, using vs try/finally |
| Switch / Enum | ~11 | неполные switch, fallthrough, опечатки в case |
| Performance (boxing/LINQ/StringBuilder) | ~6 | hot-path аллокации, ToList в горячем цикле |
| Type casts / conversions | значимая часть | as → NRE, unsafe (T)x, потеря точности |
| Copy-paste duplicates | ~30 | identical sub-expressions, equal branches, повторённые условия |
| Arithmetic | ~12 | integer overflow, деление на ноль, неверный приоритет |
| Arrays / indexing | ~6 | out of bounds, off-by-one |
| String / format | ~12 | неверные индексы аргументов, format string, неэффективное .ToString |
| Equality / hashing | ~11 | Equals без GetHashCode, ReferenceEquals на value type |
| Properties / fields | ~15 | self-assignment, readonly violations, public mutable field |
| OOP / Inheritance | ~7 | override без virtual, скрытие members, неверное new() |
| Generics | ~2 | default(T), covariance/contravariance |
| Control flow | ~22 | unreachable, infinite loop, return пропущен |
| Logical conditions | ~24 | always true, always false, тавтологии, противоречия |
| Dead code / unused | ~42 | dead code, unused variable, dead store |
| API / parameters | ~23 | invalid argument, ignored result, missing param |
| Regex / Format | ~1 | некорректные паттерны |

Полный реестр всех 275 правил с кодами, CWE, OWASP и описаниями — в Приложении A.

---

## 4. Архитектура анализатора

### 4.1 Концептуальные слои

```
┌──────────────────────────────────────────────────────────────────┐
│  Drivers          CLI │ MSBuild target │ Rider plugin │ Server   │
├──────────────────────────────────────────────────────────────────┤
│  Result pipeline  Suppression │ Baseline │ Severity │ Rate-limit │
│                   SARIF/JSON/HTML/CSV emitters                   │
├──────────────────────────────────────────────────────────────────┤
│  Rule engine      Rule registry  Subscriptions  Scheduler        │
│                   AST rules │ Symbol rules │ DataFlow rules      │
│                   Taint rules │ Hierarchy rules                  │
├──────────────────────────────────────────────────────────────────┤
│  Analysis core    Class hierarchy │ Call graph │ Alias (Andersen)│
│                   Intra-DFA (lattice + fixpoint)                 │
│                   Inter-DFA (procedure summaries, IFDS/IDE-like) │
│                   Symbolic executor (bounded, path-sensitive)    │
│                   Taint engine (sources/sinks/sanitizers)        │
├──────────────────────────────────────────────────────────────────┤
│  IR & semantic    Roslyn Compilation │ SyntaxTree │ SemanticModel│
│                   IOperation tree │ ControlFlowGraph             │
│                   Custom IR slabs: SSA, Heap, Summary store      │
├──────────────────────────────────────────────────────────────────┤
│  Frontend         MSBuildWorkspace │ ProjectGraph │ SDK loader   │
│                   Source/Binary refs │ NuGet restore             │
├──────────────────────────────────────────────────────────────────┤
│  Persistence      Compilation cache │ Summary store │ Suppression│
│                   base │ Incremental state (Roaring bitmap)      │
└──────────────────────────────────────────────────────────────────┘
```

### 4.2 Frontend и IR

- **Roslyn (Microsoft.CodeAnalysis.CSharp 4.13+)** — единственный источник AST и семантической модели. Своего парсера не пишем (это работа на ~5 человеко-лет минимум; для C# Roslyn идеально подходит).
- Загрузка проектов через `MSBuildWorkspace`, а для сценариев без MSBuild — через `AdhocWorkspace` + ручной добавляющий `ProjectReference`.
- Используем `Microsoft.Build.Locator` для нахождения SDK.
- `Compilation.GetSemanticModel(tree)` даёт типы, символы, ссылки. Все правила работают только через семантическую модель — синтаксический pattern matching без семантики бесполезен (PVS об этом говорит явно).
- **`ControlFlowGraph.Create(IOperation)`** — у Roslyn уже есть встроенный CFG-builder поверх `IOperation`. Используем его как основу, поверх добавляем SSA нумерацию и lattice values.
- Поверх Roslyn-операций строим внутреннее представление (`OvsIr`):
  - `OvsMethod` — содержит CFG, SSA-граф, локальные переменные, метаданные.
  - `OvsHeapObject` — модель heap-объекта (поля, тип, состояние nullability).
  - `OvsSymbolFingerprint` — сериализуемая сигнатура символа (для incremental).

### 4.3 Внутрипроцедурный data-flow

- Каркас «monotone framework» на абстрактных решётках. Базовый интерфейс:
  ```csharp
  public interface ILattice<T>
  {
      T Bottom { get; }
      T Top    { get; }
      T Join(T a, T b);             // least upper bound
      bool LessOrEqual(T a, T b);
  }
  public interface ITransfer<T>
  {
      T Apply(BasicBlock block, T input);
      T Apply(IOperation op, T input);
  }
  ```
- Решётки, нужные на первом этапе:
  - **NullState**: `Unknown | NotNull | MaybeNull | DefinitelyNull`. Покрывает CWE-476.
  - **ConstantValue**: literal или диапазон. Для always-true/false.
  - **IntervalDomain**: для CWE-682, CWE-697, CWE-697 (out-of-bounds).
  - **InitializedState**: `Uninitialized | MaybeInit | Initialized`.
  - **DisposeState**: `Live | Disposed | DoubleDisposed`.
  - **TaintState**: `Clean | Tainted(sources) | Sanitized`. Поднимается на 5 этапе.
- Решатель — стандартный worklist по reverse postorder CFG.
- Path-sensitivity получаем «бесплатно» через расщепление состояний на edge’ах условных переходов (`if (x != null)` ⇒ на then-edge x.NullState = NotNull, иначе DefinitelyNull). PVS-Studio именно так и работает («все три анализа реализованы как единый механизм», цитата из мануала §«Чувствительный к путям выполнения анализ»).
- Для масштабирования path-sensitivity ограничиваем bounded path exploration: limit 64 ветвлений на метод, иначе откатываемся к flow-sensitive merge. Это распространённый компромисс (Coverity, Infer работают так же).

### 4.4 Межпроцедурный анализ

- **Call graph** строится отдельным проходом: для каждого `InvocationExpression` через семантическую модель достаём целевые `IMethodSymbol`. Виртуальные вызовы расширяются по `class hierarchy analysis` (CHA), для интерфейсов — `RTA` (Rapid Type Analysis).
- **Procedure summaries**. Под каждый метод вычисляем сводку:
  - влияние на `null`-состояние возвращаемого значения и `out`-параметров;
  - может ли метод вернуть null при данной комбинации nullability аргументов (relational summary);
  - множество throws-веток с исключениями;
  - может ли метод протолкнуть taint от входа `i` на выход `j` (IFDS-граф пропуска taint).
- Сводки храним сериализуемо (MessagePack) — это нужно для инкрементального анализа.
- Bottom-up обход по топологическому порядку SCC call graph’а. SCC обрабатываем итеративно до стабилизации (рекурсия).
- Реализация на основе IFDS/IDE (Reps-Horwitz-Sagiv) — это де-факто стандарт. Для C# IDE-подход даёт хорошее соотношение цена/качество.

### 4.5 Taint analysis

- Отдельная решётка `TaintState`, отдельный набор правил (`OvsTaintRule`).
- **Конфигурация источников/стоков** в YAML, по образцу Semgrep / CodeQL:
  ```yaml
  sources:
    - method: System.Web.HttpRequest::get_Form
    - method: Microsoft.AspNetCore.Http.HttpRequest::get_Query
    - method: System.Console::ReadLine
  sinks:
    sql-injection:
      cwe: CWE-89
      methods:
        - System.Data.SqlClient.SqlCommand::.ctor(string,*)
        - System.Data.Common.DbCommand::set_CommandText
    path-traversal:
      cwe: CWE-22
      methods:
        - System.IO.File::Open*(string,*)
        - System.IO.FileStream::.ctor(string,*)
  sanitizers:
    sql-injection:
      - System.Data.SqlClient.SqlParameter::.ctor
      - System.Web.HttpUtility::HtmlEncode
  ```
- Propagators для конкатенаций строк, `string.Format`, `StringBuilder.Append`, интерполяции и LINQ-выборок описываются отдельным разделом конфига.
- На первом релизе taint engine — 13 правил из V5601…V5631 (SQLi, path traversal, XSS, insecure deser, XXE, command injection, SSRF, log injection, LDAP, XPath, open redirect, Zip Slip, format string).

### 4.6 Rule engine

- Правило — класс, наследующий один из четырёх базовых классов:
  - `AstRule` — реагирует на узлы синтаксиса. Лёгкие диагностики (V3007 «лишняя точка с запятой»).
  - `SymbolRule` — обходит символы (методы, классы). Для V3009 «всегда возвращает одно значение».
  - `DataFlowRule<TLattice>` — подписывается на состояние решётки. Для V3022/V3027/V3142 и т.п.
  - `TaintRule` — получает граф потока taint, выпускает предупреждение когда tainted данные доходят до sink. Для V56xx.
- Регистрация — атрибутом, как у Roslyn analyzers. Сделать совместимый с `DiagnosticAnalyzer` shim не получится (Roslyn analyzer API не предоставляет полноценный CFG-fixpoint), но интерфейс правил по форме похож для пониженного барьера.
- Каждое правило декларирует:
  ```csharp
  [Rule(
      Code        = "V3022",
      DefaultLevel= Severity.Level1,
      Cwe         = "CWE-570",
      Category    = RuleCategory.GeneralAnalysis,
      Capabilities= AnalysisCapability.DataFlow | AnalysisCapability.PathSensitive
  )]
  ```
- Конфигурация серьёзности и включения/выключения — внешним JSON (по образцу `.pvsconfig` и `editorconfig`).

### 4.7 Подавление и baseline

- Inline-маркеры: `// ovs:disable=V3022` (до конца строки), `// ovs:disable-next-line=V3022,V3001`, `// ovs:disable-block` … `// ovs:enable-block`.
- Атрибутом: `[SuppressMessage("OpenVulScan", "V3022", Justification = "...")]`.
- Внешний baseline-файл (`openvulscan.suppress`) — содержит fingerprint (hash от снеппета + код правила + относительный путь). Нечувствителен к перенумерации строк (как у PVS-Studio `.suppress`).
- При сравнении baseline’а допускаем сдвиг ±N строк и fuzzy-сопоставление по нормализованному снеппету.

### 4.8 Инкрементальный анализ

- На каждом проходе сохраняем:
  - `compilation-hash` (по `MetadataReference.GetAssemblyIdentity().PublicKeyToken` + хешу source файлов).
  - `summary-store` (MessagePack) — сводки методов с их fingerprint’ом.
  - `dependency-graph` — какие методы и файлы влияют на какие.
- При повторном запуске:
  1. Загружаем кеш.
  2. Сравниваем хеши файлов с предыдущим прогоном.
  3. Переанализу подлежат: изменённые файлы + все методы, чьи сводки зависят от методов из изменённых файлов (транзитивно).
- Полностью пересобирать call graph не нужно — он перестраивается только по dirty-методам.

### 4.9 Параллелизм и память

- Roslyn `Compilation`, `SyntaxTree`, `SemanticModel` потокобезопасны (read-only после построения).
- Внутри одного method analysis работаем в одном потоке. Между методами — `Parallel.ForEachAsync` по worklist.
- Bottleneck — память: на крупном решении (>500k LoC) SemanticModel + IOperation tree держится сильно. На каждом потоке — отдельная partial compilation, общий compilation pinned один раз.
- Ограничители: `max-memory` флаг (по умолчанию 60% RAM), при переборе — graceful degradation: бросаем path-sensitive, скатываемся к flow-sensitive.

### 4.10 Эмиттеры результата

- Минимальный набор: `sarif` (2.1.0, через `Microsoft.CodeAnalysis.Sarif.Driver` или сами руками — формат стабильный), `json`, `text` (для CI логов), `csv` (для аналитики).
- Дополнительно: HTML с группировкой по файлу и правилу, статический сайт (как PVS-Studio `plog-converter` выдаёт).
- Mapping в SonarQube Generic Issue Format — отдельный экспортёр поверх SARIF.

---

## 5. Технологический стек

| Слой | Выбор | Альтернатива | Обоснование |
|---|---|---|---|
| Runtime | .NET 10 (GA, LTS) на всём фронте, включая правила | .NET 8 LTS | .NET 10 — текущий LTS, идёт с Roslyn 5.x и обновлённым JIT. Целевые IDE — Rider 2026+ и Visual Studio 2026, обе на .NET 10, поэтому от мультитаргетинга `netstandard2.0` и старого Roslyn 4.x отказываемся полностью. Это упрощает код, разрешает использовать новые языковые фичи (collection expressions, `field`, primary constructors в analyzer-коде) и снимает целый класс совместимостных багов |
| Frontend | `Microsoft.CodeAnalysis.CSharp` 4.13+, `…CSharp.Workspaces.MSBuild` | — | Только Roslyn. Свой парсер C# писать нельзя — нерентабельно. |
| MSBuild загрузчик | `Microsoft.Build.Locator` + `MSBuildWorkspace` | `dotnet-build-as-library` | Стабильно, поддерживает SDK и legacy. |
| CFG | Roslyn встроенный `ControlFlowGraph` + свои надстройки | Своя реализация поверх IOperation | Использовать готовое. |
| CLI | `System.CommandLine` 2.x | `Spectre.Console.Cli` | Microsoft mainline. |
| Serialization (кеш) | `MessagePack-CSharp` | Protobuf-net | Скорость и компактность. |
| SARIF | Своя имплементация против схемы 2.1.0 OASIS | `Microsoft.CodeAnalysis.Sarif.Driver` | Контроль над форматом; либа от Microsoft по факту слабо поддерживается. |
| Тесты | xUnit + `Verify` (snapshot) + `Roslyn.Testing` | NUnit | Verify хорошо ложится на детерминированные диагностики. |
| Конфигурация правил | YamlDotNet (sources/sinks), JSON (severity) | — | YAML удобнее для taint-конфига, JSON канонично для editor’ов. |
| Логирование | `Microsoft.Extensions.Logging` + Serilog backend | — | Стандарт .NET. |
| CI самой OvS | GitHub Actions (matrix: win/linux/osx, dotnet 10) | — | Бесплатно, нативно для open-source. |
| Дистрибуция | NuGet (`dotnet tool install --global OpenVulScan`), self-contained binaries для всех ОС | Docker image | dotnet-tool ставится одной командой. |
| Документация сайта | DocFX | Docusaurus | Нативно для .NET. |
| Бенчмарки | BenchmarkDotNet, корпус — Roslyn, ASP.NET Core, MonoGame, Avalonia, NodaTime, ImageSharp | — | Покрывает разные стили C#. |
| Лицензия | Apache 2.0 (для кода), CC-BY-4.0 (для базы правил) | MIT | Apache 2.0 даёт patent grant и удобнее для корпоративного использования. |

---

## 6. Структура решения и репозитория

```
openvulscan/
├── src/
│   ├── OpenVulScan.Cli/                 # dotnet tool entry point
│   ├── OpenVulScan.Core/                # IR, lattice infra, engine
│   │   ├── Ir/
│   │   ├── Lattice/
│   │   ├── Cfg/
│   │   ├── Summary/
│   │   ├── CallGraph/
│   │   └── Taint/
│   ├── OpenVulScan.Frontend/            # Roslyn workspace + project loader
│   ├── OpenVulScan.RuleEngine/          # Rule base classes, registry, scheduler
│   ├── OpenVulScan.Rules.Ast/           # лёгкие AST-правила (V3001–V3010 like)
│   ├── OpenVulScan.Rules.DataFlow/      # DFA-правила
│   ├── OpenVulScan.Rules.PathSensitive/ # path-sens правила
│   ├── OpenVulScan.Rules.Taint/         # OWASP V56xx
│   ├── OpenVulScan.Rules.Performance/   # Unity/perf
│   ├── OpenVulScan.Sarif/               # SARIF/JSON/HTML emitters
│   ├── OpenVulScan.Cache/               # incremental cache, summaries
│   └── OpenVulScan.Configuration/       # YAML/JSON config
├── tests/
│   ├── OpenVulScan.Core.Tests/
│   ├── OpenVulScan.Rules.Tests/         # один файл на правило, snapshot
│   ├── OpenVulScan.Integration.Tests/   # запуск на мини-репозиториях
│   └── OpenVulScan.CorpusBench/         # бенч на open-source корпусе
├── corpus/                              # подмодули с эталонными проектами
├── docs/                                # DocFX site
├── samples/                             # «как написать своё правило»
├── tools/
│   ├── update-cwe-mapping/
│   ├── corpus-runner/                   # снимает baseline + сравнивает
│   └── rule-coverage-report/            # отчёт «реализовано N / 275»
└── .github/workflows/
```

---

## 7. Дорожная карта по фазам

Оценки — для одного человека full-time. Если работа идёт по вечерам/выходным — множитель ×3 минимум. Все цифры — реалистичные, не «идеальные».

### Фаза 0. R&D и spike (1–2 мес)

Цель: убрать неопределённости в инструментарии.

- Roslyn 1-day deep dive: SyntaxTree, SemanticModel, IOperation, ControlFlowGraph.
- Spike: загрузка solution через MSBuildWorkspace на 3 эталонных проектах (Roslyn, ASP.NET Core, MonoGame).
- Spike: написать одно правило поверх IOperation CFG (например, V3001 «identical sub-expressions»). Понять API.
- Spike: построить простую решётку null-state и прогнать на 10-строчных примерах.
- Финализация архитектурного скелета этого документа.

Артефакт: репозиторий с CI, README, ADR-001 «Architecture overview».

### Фаза 1. MVP инфраструктура и первые 30 правил (3–4 мес)

Цель: рабочий CLI, который анализирует solution и выводит SARIF.

Технически:
- `OpenVulScan.Frontend`: MSBuildWorkspace loader, SDK-style + legacy.
- `OpenVulScan.RuleEngine`: реестр правил, диспетчер, базовые классы `AstRule`/`SymbolRule`.
- `OpenVulScan.Sarif`: SARIF 2.1.0 writer + plain text + JSON.
- `OpenVulScan.Cli`: команды `analyze`, `baseline create|update|diff`, `rules list`.
- Подавление: inline-маркеры, `SuppressMessage`-атрибут, baseline-файл с fuzzy-fingerprint.
- Snapshot-tests framework (Verify): каждый тест это пара `*.cs` + `*.expected.json`.

Правила (30 шт., только AST/Symbol — без DFA):
- V3001, V3003, V3004, V3005, V3007, V3009, V3013, V3014, V3016, V3025 (identical, dead, copy-paste);
- V3037, V3038, V3041 (suspicious null-checks без DFA, по синтаксису);
- V3081 (нет throw — `new Exception` теряется);
- V3084 (`is null` против `== null` инверсии);
- V3105, V3110 (ToString на string, Equals на разные типы);
- V3110 серия (Equals/HashCode симметрия);
- остальные подобные узор-зависимые;
- 4 правила группы `fails` (служебные: ошибки загрузки проекта, отсутствие SDK).

### Фаза 2. Внутрипроцедурный DFA + path-sensitive (4–6 мес)

Цель: фундамент для основной массы V3xxx.

Технически:
- `OpenVulScan.Core.Lattice`: `ILattice`, `IntervalLattice`, `NullStateLattice`, `ConstantLattice`, `InitializedLattice`, `DisposeLattice`, `MapLattice<TKey,TVal>` (product of lattices).
- Worklist-solver на reverse postorder.
- SSA-нумерация (через Roslyn `LocalSymbol` + `IOperation` index).
- Реализация edge-condition refinement (path-sensitive расщепление).
- `DataFlowRule<TLattice>` базовый класс.

Правила (40 шт., DFA-зависимые):
- V3022, V3027, V3056, V3063, V3095 (always true/false);
- V3080, V3105, V3110, V3142, V3151 (null deref);
- V3008, V3057, V3120 (duplicate assignment, dead store);
- V3120, V3142, V3148 (path-sensitive nullability — V3148 был в примере мануала);
- V3074, V3097, V3122 (forgotten Dispose, double Dispose);
- V3170 серии и т.п.

### Фаза 3. Межпроцедурный анализ и summaries (6–9 мес)

Цель: точность на реальных проектах. Без межпроцедурного анализа FP-rate неприемлем.

Технически:
- `OpenVulScan.Core.CallGraph`: построение CHA + RTA, обновляемое.
- `OpenVulScan.Core.Summary`: per-method summary, MessagePack-сериализация.
- IFDS/IDE-фреймворк для распространения dataflow-фактов через границы методов.
- Bottom-up обход SCC с итерацией.
- Инкрементальный режим: dependency graph + dirty propagation.
- Бенчмарки и оптимизация: cache, span-allocations, IOperation reuse.

Правила (30–50 шт.), требующие межпроцедурного контекста:
- V3080 точная версия (NRE через возврат метода);
- V3106 (out-of-bounds через возврат IndexOf);
- V3022/V3142 межпроцедурные;
- V3168 (await в неподходящем контексте);
- многие правила exception-handling (через анализ throws).

### Фаза 4. Taint engine и OWASP правила (4–6 мес)

Цель: SAST-набор, который реально находит дыры.

Технически:
- TaintLattice + специальная propagation для строк (concat, format, interpolate, StringBuilder, char[]).
- Конфигурация sources/sinks/sanitizers — YAML, версионируется в репозитории.
- Расширение IFDS для taint-graph.
- Built-in библиотека API-моделей: ASP.NET Core, WebForms, EF Core, Dapper, ADO.NET, System.IO, System.Diagnostics.Process, System.Xml, Newtonsoft.Json, System.Text.Json, System.Net.Http, MongoDB.Driver, LDAP, основные крипто-API.

Правила (все 31 V56xx, из них приоритетные 20):
- V5608 SQLi, V5609 path traversal, V5610 XSS, V5611 insecure deser, V5614 XXE, V5616 command injection, V5618 SSRF, V5619 log injection, V5620 LDAP, V5622 XPath, V5623 open redirect, V5627 NoSQL, V5628 Zip Slip, V5631 format string, V5601 hardcoded creds, V5612 outdated TLS, V5613 weak crypto, V5625 vulnerable NuGet, V5626 ReDoS, V5629 Trojan Source (invisible chars — token-level правило).

### Фаза 5. IDE и CI интеграция (3–6 мес)

Цель: пользователь видит результаты в Rider 2026+ / VS 2026 / CI без боли. Старые IDE сознательно не поддерживаем.

- MSBuild target (`OpenVulScan.targets`): автоматически подцепляется в `dotnet build`, выводит warnings в формат Microsoft, идёт в Error List Visual Studio 2026.
- JetBrains Rider 2026+ plugin (IntelliJ Platform SDK, Kotlin) — самый ценный для .NET-аудитории. Минимум: запуск из меню, отображение в Problems tab, переход к коду, quick-fixes через ReSharper SDK где применимо.
- Visual Studio 2026 extension (VSIX, на новой 64-bit платформе): встроенный analyzer-режим через Roslyn 5.x DiagnosticAnalyzer-shim для AST-правил, out-of-process invoke для тяжёлых DFA-правил.
- GitHub Action (`openvulscan/action@v1`): кешируется, выдаёт SARIF в Code Scanning.
- Azure DevOps task (publish SARIF в Scans).
- SonarQube exporter (Generic Issue Format).
- Документация на DocFX с примерами на каждую категорию правил.

### Фаза 6. Калибровка FP-rate и наращивание покрытия (постоянно, минимум 6 мес активной работы)

- Прогон на корпусе из 30–50 крупных open-source решений (Roslyn, ASP.NET Core, .NET runtime, Avalonia, MonoGame, NodaTime, ImageSharp, MediatR, FluentValidation, NUnit, MassTransit, Marten, EventStore, Akka.NET, Orleans, Cake, Polly, Refit, IdentityServer, etc.).
- Для каждого предупреждения — triage: TP / FP / borderline. FP запатчиваем уточнением правил.
- Целевая метрика: на корпусе FP-rate ≤ 30% по правилам Level-1, ≤ 50% по Level-2.
- Доводим количество правил до ~200 (паритет ~73 %). Не гонимся за 275 — лучше 200 качественных, чем 275 шумных.

### 7.x Сводка временной шкалы

| Фаза | Длительность full-time | Кумулятивно | Ключевой артефакт |
|---|---|---|---|
| 0. R&D | 1–2 мес | 2 мес | ADR-001 + spike |
| 1. MVP | 3–4 мес | 6 мес | CLI с 30 правилами, SARIF, baseline |
| 2. Intra DFA | 4–6 мес | 12 мес | + 40 правил, path-sensitive |
| 3. Inter DFA | 6–9 мес | 21 мес | + 30–50 правил, incremental |
| 4. Taint | 4–6 мес | 27 мес | OWASP набор |
| 5. IDE/CI | 3–6 мес | 33 мес | Rider 2026+ plugin, VS 2026 extension, MSBuild target |
| 6. Калибровка | 6+ мес | 40+ мес | FP ≤ целевого, ~200 правил |

Итого: **3–4 года full-time** до состояния «boring tech, можно ставить на свой проект и доверять предупреждениям».

---

## 8. Тестовый корпус и калибровка

### 8.1 Unit-тесты на правила (snapshot)

- На каждое правило — `*.cs` файл с истинными срабатываниями и отрицательными примерами, и `*.sarif` снеппет.
- 10–20 кейсов на правило в среднем. На 200 правил — 2000–4000 кейсов.
- Verify-framework сравнивает по нормализованному SARIF.

### 8.2 Интеграционные тесты на мини-репозиториях

- 5–10 синтетических solution’ов, имитирующих типичные структуры (web API, console, library, multi-target, source generators).
- Запускаем full pipeline, проверяем что воркэлоу `analyze → baseline → diff` стабильны.

### 8.3 Корпус open-source проектов

Зафиксированный набор репозиториев (как git submodules) с pinned commit hash:
- dotnet/roslyn (свыше 3 млн LoC C#, эталон сложности);
- dotnet/aspnetcore;
- dotnet/runtime (частично — managed части);
- AvaloniaUI/Avalonia;
- MonoGame/MonoGame;
- nodatime/nodatime;
- SixLabors/ImageSharp;
- akkadotnet/akka.net;
- StackExchange/Dapper;
- npgsql/efcore.pg.

Для каждого:
- baseline снимается;
- diff отслеживается в CI самой OvS;
- регрессии — авто-issue в репозиторий.

### 8.4 Differential testing против Roslyn analyzers / SonarAnalyzer

- На пересекающихся правилах (NRE, unreachable, dead code) сравниваем нашу выдачу с Roslyn warnings и SonarAnalyzer.CSharp.
- Расхождения = либо новый TP, либо FP. Используем как источник идей.

---

## 9. Метрики качества

| Метрика | Цель v1 | Цель v2 (год 3+) |
|---|---|---|
| Количество правил | 30 | 200 |
| FP-rate Level-1 | ≤ 50 % | ≤ 30 % |
| FP-rate Level-2 | ≤ 70 % | ≤ 50 % |
| Recall на эталонных багах (CVE C# выборка) | 50 % | 80 % |
| Время полного анализа Roslyn solution | < 15 мин | < 5 мин |
| Время инкрементального анализа после 1-файлового изменения | < 15 с | < 3 с |
| Peak RAM на Roslyn solution | < 8 ГБ | < 4 ГБ |
| Detекция OWASP Top-10 на эталонном WebGoat.NET-подобном корпусе | 6/10 | 9/10 |

FP-rate измеряется ручным triage’ом случайной выборки 100 предупреждений на корпусе.

---

## 10. Риски и mitigation

| Риск | Вероятность | Импакт | Mitigation |
|---|---|---|---|
| Roslyn API меняется между версиями (4.x → 5.x), наши adapter’ы ломаются | Средняя | Средний | Pin Roslyn version, обновлять контролируемо; абстракция Frontend позволяет менять backend |
| MSBuildWorkspace не загружает экзотические проекты (Unity, F#-mixed, legacy net461) | Высокая | Средний | Поддерживаем fallback — анализ по compile-команд из JSON (по аналогии с compile_commands.json у C++) |
| Память съедает SemanticModel на крупных решениях | Высокая | Высокий | Partial compilation per file, GC.Collect между batch’ами, лимит max-memory с graceful degradation |
| Высокий FP-rate отпугнёт первых пользователей | Очень высокая | Очень высокий | Фокус на Level-1 правилах, итеративная калибровка, baseline как первая команда в onboarding-гайде |
| Тoint engine требует постоянного апдейта моделей API | Высокая | Средний | Версионируем модели в репо, ежемесячный bump, community PR’ы как у Semgrep rules |
| Solo-инженер выгорит | Высокая | Очень высокий | Чёткое разрезание на 3–6-месячные milestones, релиз раз в 2–3 мес. как форсирующая функция |
| Юр.риски от копирования формулировок предупреждений PVS-Studio | Низкая | Средний | Не копировать дословно «Name» из RulesMap.xml; переформулировать с упоминанием эквивалентного PVS-кода |
| Roslyn CFG не покрывает все языковые конструкции (source generators, file-scoped types) | Средняя | Средний | Регрессионные тесты на свежие C# (12, 13) фичи, контрибутить багрепорты в Roslyn |
| NuGet vulnerable packages базы данных требует своего фида | Высокая | Низкий | Использовать GitHub Advisory Database через REST API + NVD как fallback |

---

## 11. Что не делаем (явный out-of-scope)

- Анализ языков, отличных от C# (Razor, F#, VB.NET — игнорируем).
- Поддержка .NET Framework и более старых runtime: проекты, не собирающиеся на .NET 10 SDK, считаются вне scope.
- Поддержка устаревших IDE: VS Code, VS Mac, VS 2022 и более ранних, Rider до 2026, IDEA, CLion, Qt Creator. Целевая аудитория — Rider 2026+ и Visual Studio 2026, обе на .NET 10. Это позволяет отказаться от мультитаргетинга на `netstandard2.0` и Roslyn 4.x.
- Свой парсер C# (Roslyn 5.x из .NET 10 SDK — единственный источник истины).
- SMT-решатель (Z3/CVC5) на первой версии. Path-sensitive делаем без SMT, через явное расщепление по edge’ам. Z3 — кандидат на v2 для более точных interval/string-constraints.
- Сертификация ФСТЭК / ГОСТ — отдельный длинный путь, требующий юр.лица и денег.
- Корпоративные web-дашборды (SonarQube exporter покрывает 80 % этой потребности).

---

## 12. Лицензирование и community

- Код: Apache 2.0.
- Реестр правил и API-модели: CC-BY-4.0 (форкабельно, с указанием авторства).
- Подавление reverse-engineering PVS-Studio: правила пишутся с нуля, тесты пишутся с нуля, формулировки сообщений — собственные. Нельзя использовать конфиденциальные материалы PVS-Studio (исходники, внутренние документы). Можно использовать публичную документацию и примеры из блога PVS-Studio как референс, что́ ожидается от правила.

---

## Приложение A. Полный реестр C# диагностик PVS-Studio (275 правил)

Источник: `https://files.pvs-studio.com/rules/RulesMap.xml`, секция `<RuleSet lang="cs">`, версия 7.42.

### Группа `fails` — служебные сообщения анализатора (не диагностики)
| Код | Уровень | Сообщение |
|---|---|---|
| `V009` | — | To use free version of PVS-Studio, source code files are required to start with a special comment. |
| `V051` | — | Some of the references in project are missing or incorrect. The analysis results could be incomplete. Consider making the project fully compilable and building it before analysis. |
| `V052` | — | A critical error had occurred. |
| `V053` | — | Failed to load .NET built-in types for a project. |

### Группа `owasp` — SAST/OWASP диагностики (V56xx)
| Код | Lvl | CWE | OWASP | Описание |
|---|---|---|---|---|
| `V5601` | L3 | CWE-798,CWE-259 | OWASP-2.10.4 | OWASP. Storing credentials inside source code can lead to security issues. |
| `V5602` | L1 | CWE-390 | OWASP-11.1.8 | OWASP. The object was created but it is not being used. The 'throw' keyword could be missing. |
| `V5603` | L2 | CWE-390 | OWASP-11.1.8 | OWASP. The original exception object was swallowed. Stack of original exception could be lost. |
| `V5604` | L2 | CWE-609 | OWASP-11.1.6,OWASP-1.11.3 | OWASP. Potentially unsafe double-checked locking. Use volatile variable(s) or synchronization primitives to avoid this. |
| `V5605` | L2 | CWE-367 | OWASP-1.11.3,OWASP-11.1.6 | OWASP. Unsafe invocation of event, NullReferenceException is possible. Consider assigning event to a local variable before invoking it. |
| `V5606` | L3 | CWE-1069,CWE-390,CWE-544 | OWASP-7.4.2 | OWASP. An exception handling block does not contain any code. |
| `V5607` | L3 | CWE-544 | OWASP-7.4.2 | OWASP. Exception classes should be publicly accessible. |
| `V5608` | L1 | CWE-89 | OWASP-5.3.4,OWASP-5.3.5 | OWASP. Possible SQL injection. Potentially tainted data is used to create SQL command. |
| `V5609` | L1 | CWE-22 | OWASP-12.3.1 | OWASP. Possible path traversal vulnerability. Potentially tainted data is used as a path. |
| `V5610` | L1 | CWE-79 | OWASP-5.3.3 | OWASP. Possible XSS vulnerability. Potentially tainted data might be used to execute a malicious script. |
| `V5611` | L1 | CWE-502 | OWASP-1.5.2,OWASP-5.5.3 | OWASP. Potential insecure deserialization vulnerability. Potentially tainted data is used to create an object using deserialization. |
| `V5612` | L2 | CWE-326 | OWASP-9.1.3 | OWASP. Do not use old versions of SSL/TLS protocols as it may cause security issues. |
| `V5613` | L2 | CWE-327,CWE-328 | OWASP-2.9.3,OWASP-8.3.7 | OWASP. Use of outdated cryptographic algorithm is not recommended. |
| `V5614` | L1 | CWE-611 | OWASP-5.5.2 | OWASP. Potential XXE vulnerability. Insecure XML parser is used to process potentially tainted data. |
| `V5615` | L1 | CWE-776 | - | OWASP. Potential XEE vulnerability. Insecure XML parser is used to process potentially tainted data. |
| `V5616` | L2 | CWE-77,CWE-78,CWE-88 | OWASP-5.3.8 | OWASP. Possible command injection. Potentially tainted data is used to create OS command. |
| `V5617` | L1 | CWE-613 | OWASP-3.3.2 | OWASP. Assigning potentially negative or large value as timeout of HTTP session can lead to excessive session expiration time. |
| `V5618` | L1 | CWE-918 | OWASP-5.2.6,OWASP-12.6.1 | OWASP. Possible server-side request forgery. Potentially tainted data is used in the URL. |
| `V5619` | L1 | CWE-117 | OWASP-7.3.1 | OWASP. Possible log injection. Potentially tainted data is written into logs. |
| `V5620` | L1 | CWE-90 | OWASP-5.3.7 | OWASP. Possible LDAP injection. Potentially tainted data is used in a search filter. |
| `V5621` | L2 | CWE-209 | OWASP-8.3.5 | OWASP. Error message contains potentially sensitive data that may be exposed. |
| `V5622` | L1 | CWE-643 | OWASP-5.3.10 | OWASP. Possible XPath injection. Potentially tainted data is used in the XPath expression. |
| `V5623` | L1 | CWE-601 | OWASP-5.1.5 | OWASP. Possible open redirect vulnerability. Potentially tainted data is used in the URL. |
| `V5624` | L1 | CWE-15 | - | OWASP. Use of potentially tainted data in configuration may lead to security issues. |
| `V5625` | L2 | CWE-1352,CWE-1035 | OWASP-9.1.3 | OWASP. Referenced package contains vulnerability. |
| `V5626` | L1 | CWE-1333 | - | OWASP. Possible ReDoS vulnerability. Potentially tainted data is processed by regular expression that contains an unsafe pattern. |
| `V5627` | L1 | CWE-943 | OWASP-5.3.4 | OWASP. Possible NoSQL injection. Potentially tainted data is used to create query. |
| `V5628` | L1 | CWE-641,CWE-22,CWE-99 | OWASP-5.1.4 | OWASP. Possible Zip Slip vulnerability. Potentially tainted data is used in the path to extract the file. |
| `V5629` | L1 | CWE-507 | OWASP-10.2.3 | OWASP. Code contains invisible characters that may alter its logic. Consider enabling the display of invisible characters in the code editor. |
| `V5630` | L3 | CWE-113 | - | OWASP. Possible cookie injection. Potentially tainted data is used to create a cookie. |
| `V5631` | L2 | CWE-134 | OWASP-1.3.10 | OWASP. Use of externally-controlled format string. Potentially tainted data is used as a format string. |

### Группа `op` — Микрооптимизации Unity Engine (V40xx)
| Код | Lvl | Описание |
|---|---|---|
| `V4001` | L3 | Unity Engine. Boxing inside a frequently called method may decrease performance. |
| `V4002` | L3 | Unity Engine. Avoid storing consecutive concatenations inside a single string in performance-sensitive context. Consider using StringBuilder to improve performance. |
| `V4003` | L3 | Unity Engine. Avoid capturing variable in performance-sensitive context. This can lead to decreased performance. |
| `V4004` | L3 | Unity Engine. New array object is returned from method or property. Using such member in performance-sensitive context can lead to decreased performance. |
| `V4005` | L3 | Unity Engine. The expensive operation is performed inside method or property. Using such member in performance-sensitive context can lead to decreased performance. |
| `V4006` | L3 | Unity Engine. Multiple operations between complex and numeric values. Prioritizing operations between numeric values can optimize execution time. |
| `V4007` | L3 | Unity Engine. Avoid creating and destroying UnityEngine objects in performance-sensitive context. Consider activating and deactivating them instead. |
| `V4008` | L3 | Unity Engine. Avoid using memory allocation Physics APIs in performance-sensitive context. |

### Группа `ga` — General Analysis (V3xxx), по уровням критичности

#### LEVEL-1 (102 правил)

| Код | CWE | Описание |
|---|---|---|
| `V3001` | CWE-570,CWE-571 | There are identical sub-expressions to the left and to the right of the 'foo' operator. |
| `V3003` | CWE-570 | The use of 'if (A) {...} else if (A) {...}' pattern was detected. There is a probability of logical error presence. |
| `V3004` | CWE-691 | The 'then' statement is equivalent to the 'else' statement. |
| `V3005` | - | The 'x' variable is assigned to itself. |
| `V3006` | CWE-390 | The object was created but it is not being used. The 'throw' keyword could be missing. |
| `V3007` | CWE-691 | Odd semicolon ';' after 'if/for/while' operator. |
| `V3010` | CWE-252 | The return value of function 'Foo' is required to be utilized. |
| `V3011` | CWE-570 | Two opposite conditions were encountered. The second condition is always false. |
| `V3012` | CWE-783 | The '?:' operator, regardless of its conditional expression, always returns one and the same value. |
| `V3014` | CWE-691 | It is likely that a wrong variable is being incremented inside the 'for' operator. Consider reviewing 'X'. |
| `V3015` | CWE-691 | It is likely that a wrong variable is being compared inside the 'for' operator. Consider reviewing 'X'. |
| `V3016` | CWE-691 | The variable 'X' is being used for this loop and for the outer loop. |
| `V3019` | CWE-697 | It is possible that an incorrect variable is compared with null after type conversion using 'as' keyword. |
| `V3020` | CWE-670 | An unconditional 'break/continue/return/goto' within a loop. |
| `V3021` | CWE-561 | There are two 'if' statements with identical conditional expressions. The first 'if' statement contains method return. This means that the second 'if' statement is senseless. |
| `V3022` | CWE-570,CWE-571 | Expression is always true/false. |
| `V3023` | CWE-571 | Consider inspecting this expression. The expression is excessive or contains a misprint. |
| `V3025` | CWE-685 | Incorrect format. Consider checking the N format items of the 'Foo' function. |
| `V3027` | CWE-476 | The variable was utilized in the logical expression before it was verified against null in the same logical expression. |
| `V3028` | CWE-691 | Consider inspecting the 'for' operator. Initial and final values of the iterator are the same. |
| `V3030` | CWE-571 | Recurring check. This condition was already verified in previous line. |
| `V3032` | CWE-835 | Waiting on this expression is unreliable, as compiler may optimize some of the variables. Use volatile variable(s) or synchronization primitives to avoid this. |
| `V3033` | CWE-670 | It is possible that this 'else' branch must apply to the previous 'if' statement. |
| `V3040` | CWE-682 | The expression contains a suspicious mix of integer and real types. |
| `V3041` | CWE-682 | The expression was implicitly cast from integer type to real type. Consider utilizing an explicit type cast to avoid the loss of a fractional part. |
| `V3042` | CWE-476 | Possible NullReferenceException. The '?.' and '.' operators are used for accessing members of the same object. |
| `V3043` | CWE-483 | The code's operational logic does not correspond with its formatting. |
| `V3044` | - | WPF: writing and reading are performed on a different Dependency Properties. |
| `V3045` | - | WPF: the names of the property registered for DependencyProperty, and of the property used to access it, do not correspond with each other. |
| `V3046` | - | WPF: the type registered for DependencyProperty does not correspond with the type of the property used to access it. |
| `V3047` | - | WPF: A class containing registered property does not correspond with a type that is passed as the ownerType.type. |
| `V3048` | - | WPF: several Dependency Properties are registered with a same name within the owner type. |
| `V3049` | - | WPF: readonly field of 'DependencyProperty' type is not initialized. |
| `V3053` | - | An excessive expression. Examine the substrings "abc" and "abcd". |
| `V3058` | CWE-462 | An item with the same key has already been added. |
| `V3061` | - | Parameter 'A' is always rewritten in method body before being used. |
| `V3064` | CWE-369 | Division or mod division by zero. |
| `V3067` | CWE-691 | It is possible that 'else' block was forgotten or commented out, thus altering the program's operation logics. |
| `V3069` | CWE-670 | It's possible that the line was commented out improperly, thus altering the program's operation logics. |
| `V3070` | CWE-457 | Uninitialized variables are used when initializing the 'A' variable. |
| `V3071` | - | The object is returned from inside 'using' block. 'Dispose' will be invoked before exiting method. |
| `V3076` | CWE-570,CWE-571 | Comparison with 'double.NaN' is meaningless. Use 'double.IsNaN()' method instead. |
| `V3080` | CWE-476 | Possible null dereference. |
| `V3084` | - | Anonymous function is used to unsubscribe from event. No handlers will be unsubscribed, as a separate delegate instance is created for each anonymous function declaration. |
| `V3089` | CWE-665 | Initializer of a field marked by [ThreadStatic] attribute will be called once on the first accessing thread. The field will have default value on different threads. |
| `V3092` | CWE-670 | Range intersections are possible within conditional expressions. |
| `V3094` | - | Possible exception when deserializing type. The Ctor(SerializationInfo, StreamingContext) constructor is missing. |
| `V3095` | CWE-476 | The object was used before it was verified against null. Check lines: N1, N2. |
| `V3096` | - | Possible exception when serializing type. [Serializable] attribute is missing. |
| `V3098` | CWE-670 | The 'continue' operator will terminate 'do { ... } while (false)' loop because the condition is always false. |
| `V3099` | CWE-684 | Not all the members of type are serialized inside 'GetObjectData' method. |
| `V3102` | - | Suspicious access to element by a constant index inside a loop. |
| `V3103` | - | A private Ctor(SerializationInfo, StreamingContext) constructor in unsealed type will not be accessible when deserializing derived types. |
| `V3105` | CWE-690 | The 'a' variable was used after it was assigned through null-conditional operator. NullReferenceException is possible. |
| `V3106` | CWE-787,CWE-125 | Possibly index is out of bound. |
| `V3110` | CWE-674 | Possible infinite recursion. |
| `V3120` | CWE-835 | Potentially infinite loop. The variable from the loop exit condition does not change its value between iterations. |
| `V3122` | CWE-570 | Uppercase (lowercase) string is compared with a different lowercase (uppercase) string. |
| `V3123` | CWE-783 | Perhaps the '??' operator works in a different way than it was expected. Its priority is lower than priority of other operators in its left part. |
| `V3129` | - | The value of the captured variable will be overwritten on the next iteration of the loop in each instance of anonymous function that captures it. |
| `V3131` | CWE-704 | The expression is checked for compatibility with the type 'A', but is casted to the 'B' type. |
| `V3133` | CWE-682 | Postfix increment/decrement is senseless because this variable is overwritten. |
| `V3144` | - | This file is marked with copyleft license, which requires you to open the derived source code. |
| `V3153` | CWE-476 | Dereferencing the result of null-conditional access operator can lead to NullReferenceException. |
| `V3160` | CWE-686 | Argument of incorrect type is passed to the 'Enum.HasFlag' method. |
| `V3161` | CWE-686 | Comparing value type variables with 'ReferenceEquals' is incorrect because compared values will be boxed. |
| `V3168` | CWE-476 | Awaiting on expression with potential null value can lead to throwing of 'NullReferenceException'. |
| `V3170` | CWE-670 | Both operands of the '??' operator are identical. |
| `V3172` | CWE-483 | The 'if/if-else/for/while/foreach' statement and code block after it are not related. Inspect the program's logic. |
| `V3174` | CWE-670 | Suspicious subexpression in a sequence of similar comparisons. |
| `V3175` | CWE-667 | Locking operations must be performed on the same thread. Using 'await' in a critical section may lead to a lock being released on a different thread. |
| `V3177` | CWE-783 | Logical literal belongs to second operator with a higher priority. It is possible literal was intended to belong to '??' operator instead. |
| `V3178` | CWE-672 | Calling method or accessing property of potentially disposed object may result in exception. |
| `V3179` | CWE-628 | Calling element access method for potentially empty collection may result in exception. |
| `V3180` | CWE-687 | The 'HasFlag' method always returns 'true' because the value '0' is passed as its argument. |
| `V3181` | CWE-682 | The result of '&amp;' operator is '0' because one of the operands is '0'. |
| `V3182` | CWE-480 | The result of '&amp;' operator is always '0'. |
| `V3184` | CWE-687 | The argument's value is greater than the size of the collection. Passing the value into the 'Foo' method will result in an exception. |
| `V3185` | CWE-628 | An argument containing a file path could be mixed up with another argument. The other function parameter expects a file path instead. |
| `V3186` | CWE-687 | The arguments violate the bounds of collection. Passing these values into the method will result in an exception. |
| `V3187` | - | Parts of an SQL query are not delimited by any separators or whitespaces. Executing this query may lead to an error. |
| `V3188` | - | Unity Engine. The value of an expression is a potentially destroyed Unity object or null. Member invocation on this value may lead to an exception. |
| `V3190` | CWE-821 | Concurrent modification of a variable may lead to errors. |
| `V3191` | - | Iteration through collection makes no sense because it is always empty. |
| `V3193` | CWE-366 | Data processing results are potentially used before asynchronous output reading is complete. Consider calling 'WaitForExit' overload with no arguments before using the data. |
| `V3194` | CWE-704 | Calling 'OfType' for collection will return an empty collection. It is not possible to cast collection elements to the type parameter. |
| `V3195` | CWE-476 | Collection initializer implicitly calls 'Add' method. Using it on member with default value of null will result in null dereference exception. |
| `V3199` | CWE-787,CWE-125 | The index from end operator is used with the value that is less than or equal to zero. Collection index will be out of bounds. |
| `V3204` | CWE-192 | The expression is always false due to implicit type conversion. Overflow check is incorrect. |
| `V3205` | - | Unity Engine. Improper creation of 'MonoBehaviour' or 'ScriptableObject' object using the 'new' operator. Use the special object creation method instead. |
| `V3206` | - | Unity Engine. A direct call to the coroutine-like method will not start it. Use the 'StartCoroutine' method instead. |
| `V3207` | CWE-670 | The 'not A or B' logical pattern may not work as expected. The 'not' pattern is matched only to the first expression from the 'or' pattern. |
| `V3208` | CWE-686 | Unity Engine. Using 'WeakReference' with 'UnityEngine.Object' is not supported. GC will not reclaim the object's memory because it is linked to a native object. |
| `V3210` | CWE-686 | Unity Engine. Unity does not allow removing the 'Transform' component using 'Destroy' or 'DestroyImmediate' methods. The method call will be ignored. |
| `V3213` | CWE-628 | Unity Engine. The 'GetComponent' method must be instantiated with a type that inherits from 'UnityEngine.Component'. |
| `V3217` | CWE-190,CWE-191 | Possible overflow as a result of an arithmetic operation. |
| `V3218` | CWE-193 | Loop condition may be incorrect due to an off-by-one error. |
| `V3219` | - | The variable was changed after it was captured in a LINQ method with deferred execution. The original value will not be used when the method is executed. |
| `V3220` | CWE-563 | The result of the LINQ method with deferred execution is never used. The method will not be executed. |
| `V3221` | - | Modifying a collection during its enumeration will lead to an exception. |
| `V3225` | CWE-253 | A data reading method returns the number of bytes that were read and cannot return the value of -1. |
| `V3230` | - | Comparison with 'typeof(Nullable&lt;T&gt;)' is meaningless. Calling 'GetType()' on a nullable variable never returns 'Nullable&lt;T&gt;'. |

#### LEVEL-2 (95 правил)

| Код | CWE | Описание |
|---|---|---|
| `V3008` | CWE-563 | The 'x' variable is assigned values twice successively. Perhaps this is a mistake. |
| `V3009` | CWE-393 | It's odd that this method always returns one and the same value of NN. |
| `V3017` | - | A pattern was detected: A \|\| (A &amp;&amp; ...). The expression is excessive or contains a logical error. |
| `V3018` | CWE-670 | Consider inspecting the application's logic. It's possible that 'else' keyword is missing. |
| `V3029` | - | The conditional expressions of the 'if' statements situated alongside each other are identical. |
| `V3031` | - | An excessive check can be simplified. The operator '\|\|' operator is surrounded by opposite expressions 'x' and '!x'. |
| `V3034` | CWE-480 | Consider inspecting the expression. Probably the '!=' should be used here. |
| `V3035` | CWE-480 | Consider inspecting the expression. Probably the '+=' should be used here. |
| `V3036` | CWE-480 | Consider inspecting the expression. Probably the '-=' should be used here. |
| `V3037` | - | An odd sequence of assignments of this kind: A = B; B = A; |
| `V3038` | CWE-687 | The argument was passed to method several times. It is possible that another argument should be passed instead. |
| `V3050` | - | Possibly an incorrect HTML. The &lt;/XX&gt; closing tag was encountered, while the &lt;/YY&gt; tag was expected. |
| `V3051` | CWE-704 | An excessive type cast or check. The object is already of the same type. |
| `V3052` | CWE-390 | The original exception object was swallowed. Stack of original exception could be lost. |
| `V3054` | CWE-609 | Potentially unsafe double-checked locking. Use volatile variable(s) or synchronization primitives to avoid this. |
| `V3057` | CWE-628 | Function receives an odd argument. |
| `V3062` | - | An object is used as an argument to its own method. Consider checking the first actual argument of the 'Foo' method. |
| `V3063` | CWE-570,CWE-571 | A part of conditional expression is always true/false if it is evaluated. |
| `V3065` | - | Parameter is not utilized inside method's body. |
| `V3066` | CWE-683 | Possible incorrect order of arguments passed to method. |
| `V3068` | - | Calling overrideable class member from constructor is dangerous. |
| `V3075` | - | The operation is executed 2 or more times in succession. |
| `V3077` | - | Property setter / event accessor does not utilize its 'value' parameter. |
| `V3078` | - | Sorting keys priority will be reversed relative to the order of 'OrderBy' method calls. Perhaps, 'ThenBy' should be used instead. |
| `V3079` | CWE-821 | The 'ThreadStatic' attribute is applied to a non-static 'A' field and will be ignored. |
| `V3081` | - | The 'X' counter is not used inside a nested loop. Consider inspecting usage of 'Y' counter. |
| `V3082` | CWE-563 | The 'Thread' object is created but is not started. It is possible that a call to 'Start' method is missing. |
| `V3083` | CWE-367 | Unsafe invocation of event, NullReferenceException is possible. Consider assigning event to a local variable before invoking it. |
| `V3085` | - | The name of 'X' field/property in a nested type is ambiguous. The outer type contains static field/property with identical name. |
| `V3086` | - | Variables are initialized through the call to the same function. It's probably an error or un-optimized code. |
| `V3088` | - | The expression was enclosed by parentheses twice: ((expression)). One pair of parentheses is unnecessary or misprint is present. |
| `V3090` | CWE-833,CWE-662 | Unsafe locking on an object. |
| `V3091` | - | Empirical analysis. It is possible that a typo is present inside the string literal. The 'foo' word is suspicious. |
| `V3093` | CWE-480 | The operator evaluates both operands. Perhaps a short-circuit operator should be used instead. |
| `V3101` | - | Potential resurrection of 'this' object instance from destructor. Without re-registering for finalization, destructor will not be called a second time on resurrected object. |
| `V3107` | - | Identical expression to the left and to the right of compound assignment. |
| `V3109` | - | The same sub-expression is present on both sides of the operator. The expression is incorrect or it can be simplified. |
| `V3112` | CWE-697 | An abnormality within similar comparisons. It is possible that a typo is present inside the expression. |
| `V3113` | CWE-190 | Consider inspecting the loop expression. It is possible that different variables are used inside initializer and iterator. |
| `V3114` | CWE-404 | IDisposable object is not disposed before method returns. |
| `V3115` | CWE-684 | It is not recommended to throw exceptions from 'Equals(object obj)' method. |
| `V3116` | CWE-835 | Consider inspecting the 'for' operator. It's possible that the loop will be executed incorrectly or won't be executed at all. |
| `V3117` | - | Constructor parameter is not used. |
| `V3118` | - | A component of TimeSpan is used, which does not represent full time interval. Possibly 'Total*' value was intended instead. |
| `V3119` | - | Calling a virtual (overridden) event may lead to unpredictable behavior. Consider implementing event accessors explicitly or use 'sealed' keyword. |
| `V3121` | - | An enumeration was declared with 'Flags' attribute, but does not set any initializers to override default values. |
| `V3124` | - | Appending an element and checking for key uniqueness is performed on two different variables. |
| `V3125` | CWE-476 | The object was used after it was verified against null. Check lines: N1, N2. |
| `V3126` | - | Type implementing IEquatable&lt;T&gt; interface does not override 'GetHashCode' method. |
| `V3127` | CWE-682 | Two similar code fragments were found. Perhaps, this is a typo and 'X' variable should be used instead of 'Y'. |
| `V3128` | CWE-665 | The field (property) is used before it is initialized in constructor. |
| `V3130` | - | Priority of the '&amp;&amp;' operator is higher than that of the '\|\|' operator. Possible missing parentheses. |
| `V3132` | CWE-665 | A terminal null is present inside a string. The '\0xNN' characters were encountered. Probably meant: '\xNN'. |
| `V3134` | CWE-128 | Shift by N bits is greater than the size of type. |
| `V3136` | CWE-691 | Constant expression in switch statement. |
| `V3137` | CWE-563 | The variable is assigned but is not used by the end of the function. |
| `V3139` | - | Two or more case-branches perform the same actions. |
| `V3140` | - | Property accessors use different backing fields. |
| `V3141` | - | Expression under 'throw' is a potential null, which can lead to NullReferenceException. |
| `V3142` | CWE-561 | Unreachable code detected. It is possible that an error is present. |
| `V3143` | - | The 'value' parameter is rewritten inside a property setter, and is not used after that. |
| `V3145` | CWE-476 | Unsafe dereference of a WeakReference target. The object could have been garbage collected before the 'Target' property was accessed. |
| `V3146` | CWE-476 | Possible null dereference. A method can return default null value. |
| `V3147` | CWE-567 | Non-atomic modification of volatile variable. |
| `V3148` | CWE-476 | Casting potential 'null' value to a value type can lead to NullReferenceException. |
| `V3150` | - | Loop break conditions do not depend on the number of iterations. |
| `V3151` | CWE-369 | Potential division by zero. Variable was used as a divisor before it was compared to zero. Check lines: N1, N2. |
| `V3152` | CWE-369 | Potential division by zero. Variable was compared to zero before it was used as a divisor. Check lines: N1, N2. |
| `V3154` | CWE-682 | The 'a % b' expression always evaluates to 0. |
| `V3156` | CWE-628 | The argument of the method is not expected to be null. |
| `V3157` | CWE-682 | Suspicious division. Absolute value of the left operand is less than the right operand. |
| `V3158` | CWE-682 | Suspicious division. Absolute values of both operands are equal. |
| `V3159` | CWE-563 | Modified value of the operand is not used after the increment/decrement operation. |
| `V3162` | - | Suspicious return of an always empty collection. |
| `V3165` | CWE-628 | The expression of the 'char' type is passed as an argument of the 'A' type whereas similar overload with the string parameter exists. |
| `V3166` | CWE-628 | Calling the 'SingleOrDefault' method may lead to 'InvalidOperationException'. |
| `V3169` | - | Suspicious return of a local reference variable which always equals null. |
| `V3171` | CWE-839 | Potentially negative value is used as the size of an array. |
| `V3173` | - | Possible incorrect initialization of variable. Consider verifying the initializer. |
| `V3176` | CWE-570,CWE-571 | The '&amp;=' or '\|=' operator is redundant because the right operand is always true/false. |
| `V3183` | CWE-670 | Code formatting implies that the statement should not be a part of the 'then' branch that belongs to the preceding 'if' statement. |
| `V3189` | - | The assignment to a member of the readonly field will have no effect when the field is of a value type. Consider restricting the type parameter to reference types. |
| `V3192` | CWE-697 | Type member is used in the 'GetHashCode' method but is missing from the 'Equals' method. |
| `V3196` | - | Parameter is not utilized inside the method body, but an identifier with a similar name is used inside the same method. |
| `V3197` | CWE-697 | The compared value inside the 'Object.Equals' override is converted to a different type that does not contain the override. |
| `V3200` | CWE-190 | Possible overflow. The expression will be evaluated before casting. Consider casting one of the operands instead. |
| `V3202` | CWE-561 | Unreachable code detected. The 'case' value is out of the range of the match expression. |
| `V3209` | - | Unity Engine. Using await on 'Awaitable' object more than once can lead to exception or deadlock, as such objects are returned to the pool after being awaited. |
| `V3214` | CWE-691 | Unity Engine. Using Unity API in the background thread may result in an error. |
| `V3216` | CWE-754 | Unity Engine. Checking a field with a specific Unity Engine type for null may not work correctly due to implicit field initialization by the engine. |
| `V3223` | CWE-821 | Inconsistent use of a potentially shared variable with and without a lock can lead to a data race. |
| `V3224` | CWE-685 | Consider using an overload with 'IEqualityComparer', as it is present in similar cases for the same collection element type. |
| `V3228` | CWE-697 | It is possible that an assigned variable should be used in the next condition. Consider checking for misprints. |
| `V3231` | CWE-248 | Async method returning 'void' throws an exception. Callers will not be able to catch this exception. Consider replacing the return type with 'Task'. |
| `V3232` | CWE-134 | Use of externally-controlled format string. Potentially tainted data is used as a format string. |

#### LEVEL-3 (33 правил)

| Код | CWE | Описание |
|---|---|---|
| `V3002` | - | The switch statement does not cover all values of the enum. |
| `V3013` | - | It is odd that the body of 'Foo_1' function is fully equivalent to the body of 'Foo_2' function. |
| `V3024` | CWE-682 | An odd precise comparison. Consider using a comparison with defined precision: Math.Abs(A - B) &lt; Epsilon or Math.Abs(A - B) &gt; Epsilon. |
| `V3026` | CWE-1339 | The constant NN is being utilized. The resulting value could be inaccurate. Consider using the KK constant. |
| `V3039` | CWE-39 | Consider inspecting the 'Foo' function call. Defining an absolute path to the file or directory is considered a poor style. |
| `V3055` | CWE-481 | Suspicious assignment inside the condition expression of 'if/while/for' operator. |
| `V3056` | CWE-682 | Consider reviewing the correctness of 'X' item's usage. |
| `V3059` | - | Consider adding '[Flags]' attribute to the enum. |
| `V3060` | CWE-682 | A value of variable is not modified. Consider inspecting the expression. It is possible that other value should be present instead of '0'. |
| `V3074` | - | The 'A' class contains 'Dispose' method. Consider making it implement 'IDisposable' interface. |
| `V3087` | - | Type of variable enumerated in 'foreach' is not guaranteed to be castable to the type of collection's elements. |
| `V3097` | - | Possible exception: type marked by [Serializable] contains non-serializable members not marked by [NonSerialized]. |
| `V3100` | CWE-476 | NullReferenceException is possible. Unhandled exceptions in destructor lead to termination of runtime. |
| `V3104` | - | The 'GetObjectData' implementation in unsealed type is not virtual, incorrect serialization of derived type is possible. |
| `V3108` | CWE-684 | It is not recommended to return null or throw exceptions from 'ToString()' method. |
| `V3111` | - | Checking value for null will always return false when generic type is instantiated with a value type. |
| `V3135` | CWE-691 | The initial value of the index in the nested loop equals 'i'. Consider using 'i + 1' instead. |
| `V3138` | - | String literal contains potential interpolated expression. |
| `V3149` | CWE-476 | Dereferencing the result of 'as' operator can lead to NullReferenceException. |
| `V3155` | CWE-682 | The expression is incorrect or it can be simplified. |
| `V3163` | CWE-1069,CWE-390,CWE-544 | An exception handling block does not contain any code. |
| `V3164` | CWE-544 | Exception classes should be publicly accessible. |
| `V3167` | CWE-821 | Parameter of 'CancellationToken' type is not used inside function's body. |
| `V3198` | CWE-1164 | The variable is assigned the same value that it already holds. |
| `V3201` | - | Return value is not always used. Consider inspecting the 'foo' method. |
| `V3203` | - | Method parameter is not used. |
| `V3211` | - | Unity Engine. The operators '?.', '??' and '??=' do not correctly handle destroyed objects derived from 'UnityEngine.Object'. |
| `V3212` | - | Unity Engine. Pattern matching does not correctly handle destroyed objects derived from 'UnityEngine.Object'. |
| `V3215` | CWE-1106 | Unity Engine. Passing a method name as a string literal into the 'StartCoroutine' is unreliable. |
| `V3222` | CWE-772 | Potential resource leak. An inner IDisposable object might remain non-disposed if the constructor of the outer object throws an exception. |
| `V3226` | CWE-404 | Potential resource leak. The disposing method will not be called if an exception occurs in the 'try' block. Consider calling it in the 'finally' block. |
| `V3227` | CWE-783 | The precedence of the arithmetic operator is higher than that of the shift operator. Consider using parentheses in the expression. |
| `V3229` | - | The 'GetHashCode' method may return different hash codes for equal objects. It uses an object reference to generate a hash for a variable. Check the implementation of the 'Equals' method. |

#### LEVEL-0 (2 правил)

| Код | CWE | Описание |
|---|---|---|
| `V3072` | - | The 'A' class containing IDisposable members does not itself implement IDisposable. |
| `V3073` | - | Not all IDisposable members are properly disposed. Call 'Dispose' when disposing 'A' class. |
