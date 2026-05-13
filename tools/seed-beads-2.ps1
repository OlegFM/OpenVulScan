#requires -Version 7
<#
.SYNOPSIS
    Засев beads-БД задачами для Phase 2..6.
.DESCRIPTION
    Запускать после tools/seed-beads.ps1 (он создал эпики и сохранил ID в .beads-epics.json).
#>

$ErrorActionPreference = 'Stop'

$idsPath = Join-Path $PSScriptRoot '.beads-epics.json'
if (-not (Test-Path $idsPath)) { throw "Сначала запусти tools/seed-beads.ps1 — нет $idsPath" }
$ids = Get-Content $idsPath -Raw | ConvertFrom-Json
$EPIC2 = $ids.EPIC2
$EPIC3 = $ids.EPIC3
$EPIC4 = $ids.EPIC4
$EPIC5 = $ids.EPIC5
$EPIC6 = $ids.EPIC6

function New-Bead {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][int]$Priority,
        [string]$Description = '',
        [string]$Acceptance = '',
        [int]$EstimateMinutes = 0,
        [string[]]$Labels = @(),
        [string]$Parent = ''
    )
    $args = @('create', $Title, '-t', $Type, '-p', "$Priority", '--silent')
    if ($Description)      { $args += @('-d', $Description) }
    if ($Acceptance)       { $args += @('--acceptance', $Acceptance) }
    if ($EstimateMinutes -gt 0) { $args += @('-e', "$EstimateMinutes") }
    if ($Labels.Count -gt 0) { $args += @('-l', ($Labels -join ',')) }
    if ($Parent)           { $args += @('--parent', $Parent) }
    $id = (& bd @args).Trim()
    if (-not $id) { throw "Не получили ID для '$Title'" }
    Write-Host "  + $id  $Title"
    return $id
}

Write-Host '=== Phase 2. Intra-procedural DFA + path-sensitive ==='

$null = New-Bead -Title 'Lattice: интерфейсы ILattice<T> и ITransfer<T>' -Type task -Priority 0 -Parent $EPIC2 -EstimateMinutes 180 `
    -Labels @('phase-2','area-core') `
    -Description "OpenVulScan.Core.Lattice: интерфейсы ILattice<T> (Bottom, Top, Join, LessOrEqual) и ITransfer<T> (Apply на BasicBlock и IOperation). Документация и тесты." `
    -Acceptance "Юнит-тесты на сигнатуру и базовый стабильный пример (MapLattice + bool flat) — Join/LessOrEqual работают по аксиомам решётки."

$null = New-Bead -Title 'Lattice: NullStateLattice (Unknown/NotNull/MaybeNull/DefinitelyNull)' -Type task -Priority 0 -Parent $EPIC2 -EstimateMinutes 240 `
    -Labels @('phase-2','area-core') `
    -Description "Конкретная решётка NullState с правильной решёткой и transfer-функциями для assignment, member access, ?. , ??, is null. Документация edge cases (nullable value types, generics)." `
    -Acceptance "Юнит-тесты 20+ кейсов; решётка ассоциативная/коммутативная/идемпотентная по property tests."

$null = New-Bead -Title 'Lattice: ConstantLattice (Bottom | Const(value) | Top)' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 180 `
    -Labels @('phase-2','area-core') `
    -Description "Простая константная решётка для int/string/bool/enum литералов. Использует value equality." `
    -Acceptance "10+ юнит-тестов."

$null = New-Bead -Title 'Lattice: IntervalLattice для int/long' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 360 `
    -Labels @('phase-2','area-core') `
    -Description "Решётка интервалов [min..max] с widening, чтобы не зацикливаться. Поддерживает + - * деление и битовые операции." `
    -Acceptance "Юнит-тесты 15+ кейсов; widening работает на циклах."

$null = New-Bead -Title 'Lattice: InitializedLattice (Uninit/MaybeInit/Init)' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 120 `
    -Labels @('phase-2','area-core') -Description "Простая решётка для отслеживания инициализации локальных переменных и полей." `
    -Acceptance "Юнит-тесты 8+ кейсов."

$null = New-Bead -Title 'Lattice: DisposeLattice (Live/Disposed/DoubleDisposed)' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 120 `
    -Labels @('phase-2','area-core') -Description "Для отслеживания IDisposable-объектов." `
    -Acceptance "Юнит-тесты 8+ кейсов."

$null = New-Bead -Title 'Lattice: MapLattice<TKey,TVal> (product of lattices)' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 180 `
    -Labels @('phase-2','area-core') -Description "Универсальный произвольный product: per-variable, per-symbol state. Эффективная реализация через ImmutableDictionary + structural sharing." `
    -Acceptance "Юнит-тесты + микробенчмарк Join на 1k ключей."

$null = New-Bead -Title 'Solver: worklist solver на reverse postorder' -Type task -Priority 0 -Parent $EPIC2 -EstimateMinutes 360 `
    -Labels @('phase-2','area-core') `
    -Description "OpenVulScan.Core.Cfg.WorklistSolver принимает CFG (от Roslyn ControlFlowGraph), ILattice, ITransfer; вычисляет fixpoint. Итерации в reverse postorder. Limit max-iterations с graceful exit." `
    -Acceptance "Стабильный fixpoint на тестах с пятью паттернами (linear, if-else, while, switch, try/catch)."

$null = New-Bead -Title 'SSA-нумерация над Roslyn IOperation/локалями' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 360 `
    -Labels @('phase-2','area-core') -Description "SSA-индексация per-block per-variable. Учитывать φ-функции на merge-блоках. Использовать CaptureId от Roslyn там, где он уже даёт нумерацию." `
    -Acceptance "Юнит-тесты на if-else, while, switch."

$null = New-Bead -Title 'Path-sensitivity: edge-condition refinement' -Type task -Priority 1 -Parent $EPIC2 -EstimateMinutes 360 `
    -Labels @('phase-2','area-core') `
    -Description "На условных edge'ах CFG (ConditionalBranch) уточнять состояние решётки: `if (x != null)` ⇒ then-edge: x = NotNull, else-edge: x = DefinitelyNull. Поддержать !, &&, ||, is null, is not null." `
    -Acceptance "Юнит-тесты на 10+ типичных условий."

$null = New-Bead -Title 'Path-sensitivity: bounded path exploration (limit 64 ветвлений)' -Type task -Priority 2 -Parent $EPIC2 -EstimateMinutes 240 `
    -Labels @('phase-2','area-core') -Description "Counter на split-операциях; при превышении — graceful fallback к flow-sensitive merge. Логируется warning." `
    -Acceptance "Тест: метод на 70 if-блоков не падает, fallback логируется."

$null = New-Bead -Title 'RuleEngine: базовый класс DataFlowRule<TLattice>' -Type task -Priority 0 -Parent $EPIC2 -EstimateMinutes 240 `
    -Labels @('phase-2','area-engine') -Description "Подписки на состояния lattice, OnState(IOperation, TLattice state) колбэк." `
    -Acceptance "Юнит-тест: dummy DataFlowRule на NullState ловит NRE на синтетическом методе."

$null = New-Bead -Title 'Rules P2: V3022 + V3056 + V3063 + V3095 (always true/false группа)' -Type feature -Priority 1 -Parent $EPIC2 -EstimateMinutes 480 `
    -Labels @('phase-2','area-rules','rule-V3022','rule-V3056','rule-V3063','rule-V3095') `
    -Description "DataFlowRule<ConstantLattice> + path-sensitive: при выходе из ветви условие принимает константное значение, репортим. V3056 — часть условия excessive; V3063 — partial always; V3095 — variable used before null-check in same expression." `
    -Acceptance "Snapshot 5+5+5+5 кейсов, FP=0 на synthetic.cs."

$null = New-Bead -Title 'Rules P2: V3027 (null deref in same logical expression)' -Type feature -Priority 1 -Parent $EPIC2 -EstimateMinutes 240 `
    -Labels @('phase-2','area-rules','rule-V3027') `
    -Description "DataFlowRule<NullStateLattice>: переменная использована в логическом выражении ДО проверки на null в этом же выражении." `
    -Acceptance "Snapshot 10+ кейсов."

$null = New-Bead -Title 'Rules P2: V3080 + V3105 (intra) + V3142 + V3151 + V3153 + V3168 (intra) — null deref набор' -Type feature -Priority 1 -Parent $EPIC2 -EstimateMinutes 600 `
    -Labels @('phase-2','area-rules','rule-V3080','rule-V3105','rule-V3142','rule-V3151','rule-V3153','rule-V3168') `
    -Description "Семейство NRE-правил через NullStateLattice. Точечные различия (await null, dereferencing после ?., uninitialized field) разнесены в отдельные subscribers, но единый базовый pipeline." `
    -Acceptance "Suite snapshot >=30 кейсов суммарно; FP на synthetic.cs = 0; coverage сам по себе ~25% от Phase-2 цели."

$null = New-Bead -Title 'Rules P2: V3008 + V3057 + V3120 (dead store / повторное присвоение)' -Type feature -Priority 2 -Parent $EPIC2 -EstimateMinutes 300 `
    -Labels @('phase-2','area-rules','rule-V3008','rule-V3057','rule-V3120') `
    -Description "DataFlowRule<MapLattice<ISymbol, AssignedState>>; обнаружить assignment, перезаписанный без чтения." `
    -Acceptance "Snapshot 5+5+5 кейсов."

$null = New-Bead -Title 'Rules P2: V3074 + V3097 + V3122 (Dispose: пропущенный/двойной/в неправильной ветке)' -Type feature -Priority 1 -Parent $EPIC2 -EstimateMinutes 360 `
    -Labels @('phase-2','area-rules','rule-V3074','rule-V3097','rule-V3122') `
    -Description "DataFlowRule<DisposeLattice>: на IDisposable-локальной переменной проверять, что все exit-пути вызывают Dispose / using." `
    -Acceptance "Snapshot 7+7+7 кейсов; учтены try/finally, using, async."

$null = New-Bead -Title 'Rules P2: V3148 (path-sensitive nullability на примере из мануала PVS)' -Type feature -Priority 2 -Parent $EPIC2 -EstimateMinutes 240 `
    -Labels @('phase-2','area-rules','rule-V3148') `
    -Description "Реализация V3148 как контрольная точка для path-sensitivity. Пример из мануала PVS §«Чувствительный к путям выполнения анализ»." `
    -Acceptance "Snapshot 5+ кейсов; точно ловит пример из мануала."

$null = New-Bead -Title 'ADR-004: path-sensitivity без SMT' -Type decision -Priority 2 -Parent $EPIC2 -EstimateMinutes 90 `
    -Labels @('phase-2','area-docs','adr') -Description "Зафиксировать решение делать path-sensitive через расщепление на edge-conditions без Z3." `
    -Acceptance "docs/adr/004-path-sensitive-no-smt.md Accepted."

Write-Host ''
Write-Host '=== Phase 3. Inter-procedural + summaries ==='

$null = New-Bead -Title 'CallGraph: Class Hierarchy Analysis (CHA)' -Type task -Priority 0 -Parent $EPIC3 -EstimateMinutes 360 `
    -Labels @('phase-3','area-core') `
    -Description "OpenVulScan.Core.CallGraph.ChaBuilder: для каждой InvocationExpression через семантическую модель получить candidate set исходя из иерархии типов (все override-ы)." `
    -Acceptance "На Roslyn solution CHA строится за < 60 c; покрытие edge'ов сравнивается с моки."

$null = New-Bead -Title 'CallGraph: Rapid Type Analysis (RTA) для интерфейсов' -Type task -Priority 1 -Parent $EPIC3 -EstimateMinutes 240 `
    -Labels @('phase-3','area-core') `
    -Description "Дополнение к CHA: при разрешении интерфейсного вызова рассматривать только типы, реально создаваемые в графе вызовов." `
    -Acceptance "Юнит-тест: CHA даёт 5 candidates, RTA сужает до 2."

$null = New-Bead -Title 'Summaries: per-method summary + сериализация (MessagePack-CSharp)' -Type task -Priority 0 -Parent $EPIC3 -EstimateMinutes 480 `
    -Labels @('phase-3','area-core') `
    -Description "MethodSummary: nullability возвращаемого значения, out-параметры, throws-set, side-effects (purity-флаг), taint-pass-through (заполняется в Phase 4). Сериализация в MessagePack через [MessagePackObject] контракты." `
    -Acceptance "Roundtrip-тест на 5 шаблонных методах; формат документирован в docs/cache-format.md."

$null = New-Bead -Title 'IFDS/IDE-фреймворк для распространения dataflow-фактов' -Type task -Priority 1 -Parent $EPIC3 -EstimateMinutes 720 `
    -Labels @('phase-3','area-core') `
    -Description "Reps-Horwitz-Sagiv: построить summary edges для каждого метода (per-fact reachability), потом подставлять при call/return. Сначала без call-context (k=0)." `
    -Acceptance "Прохождение классических примеров из учебника (constant propagation через метод). 10+ юнит-тестов."

$null = New-Bead -Title 'Bottom-up обход SCC call graph с итерацией до стабилизации' -Type task -Priority 1 -Parent $EPIC3 -EstimateMinutes 300 `
    -Labels @('phase-3','area-core') -Description "Tarjan SCC, обработка SCC fixed-point per group, обновление summaries." `
    -Acceptance "Тест на graph с циклом из 3 методов: суммаризация стабилизируется."

$null = New-Bead -Title 'Incremental: dependency graph файл↔метод↔summary' -Type task -Priority 1 -Parent $EPIC3 -EstimateMinutes 360 `
    -Labels @('phase-3','area-core') -Description "Cache формат: для каждого summary храним хеш source-snippet, MetadataReferences. После изменения файла — пересчитываются только dirty-методы." `
    -Acceptance "Сценарий: change one file, incremental rebuild touches < 5% методов на Roslyn solution."

$null = New-Bead -Title 'Incremental: dirty propagation + invalidation cache' -Type task -Priority 1 -Parent $EPIC3 -EstimateMinutes 300 `
    -Labels @('phase-3','area-core') -Description "После сравнения хешей: транзитивно invalidate downstream summaries и rules." `
    -Acceptance "Юнит-тесты на 3 случая: callee changed, caller changed, transitively affected."

$null = New-Bead -Title 'Cache: compilation-hash через MetadataReference.GetAssemblyIdentity()' -Type task -Priority 2 -Parent $EPIC3 -EstimateMinutes 180 `
    -Labels @('phase-3','area-cache') -Description "Стабильный hash от Compilation = hash(source files) + hash(all references + their PublicKeyToken). Хранение в .openvulscan/cache." `
    -Acceptance "Юнит-тест: смена версии NuGet → новый hash; пересортировка ссылок → тот же hash."

$null = New-Bead -Title 'Parallelism: Parallel.ForEachAsync по worklist между методами' -Type task -Priority 2 -Parent $EPIC3 -EstimateMinutes 240 `
    -Labels @('phase-3','area-core') -Description "Между методами параллельная обработка; внутри метода — однопоточно. CancellationToken пробрасывается." `
    -Acceptance "На Roslyn solution scale-up 3x при 4 ядрах; нет race condition (детектируется в stress-тестах через xUnit Theory)."

$null = New-Bead -Title 'Resource limits: --max-memory + graceful degradation на path-sensitive' -Type task -Priority 2 -Parent $EPIC3 -EstimateMinutes 180 `
    -Labels @('phase-3','area-cli','area-core') -Description "Флаг --max-memory (default 60% RAM). При превышении — переключение path-sensitive→flow-sensitive, без падения." `
    -Acceptance "Стресс-тест: при 1 GiB лимите анализ Roslyn проходит, в логах есть warning об overcommit."

$null = New-Bead -Title 'Bench: BenchmarkDotNet прогон на Roslyn solution' -Type task -Priority 2 -Parent $EPIC3 -EstimateMinutes 240 `
    -Labels @('phase-3','area-tests') -Description "tests/OpenVulScan.CorpusBench: benchmark цикла full-rebuild и incremental-rebuild. Время + RAM. Артефакты заливаются в actions cache." `
    -Acceptance "Baseline-цифры зафиксированы в docs/perf-baseline.md."

$null = New-Bead -Title 'Rules P3: inter-procedural NRE (V3080 точная версия)' -Type feature -Priority 1 -Parent $EPIC3 -EstimateMinutes 360 `
    -Labels @('phase-3','area-rules','rule-V3080') `
    -Description "Использовать IFDS на NullState lattice — пробросить null-info от call site через summary."  `
    -Acceptance "Snapshot 10+ inter-procedural кейсов; FP-rate < 30% на synthetic.cs."

$null = New-Bead -Title 'Rules P3: V3106 (out-of-bounds через возврат IndexOf), V3022/V3142 inter, V3168 (await null)' -Type feature -Priority 1 -Parent $EPIC3 -EstimateMinutes 480 `
    -Labels @('phase-3','area-rules','rule-V3106','rule-V3022','rule-V3142','rule-V3168') `
    -Description "Inter-procedural версии правил. Используют summaries про возвращаемые значения и nullable contracts." `
    -Acceptance "Snapshot 5+5+5+5 кейсов."

$null = New-Bead -Title 'Rules P3: exception-handling серия через throws-анализ' -Type feature -Priority 2 -Parent $EPIC3 -EstimateMinutes 480 `
    -Labels @('phase-3','area-rules') -Description "Группа правил, требующих знания, какие исключения может выкинуть метод: V3115 (throw из Equals), V3052/V5603 (swallowed exception), и подобные. Использовать summaries throws-set." `
    -Acceptance "Реализованы 5+ правил, snapshot покрытие >=50 кейсов."

Write-Host ''
Write-Host '=== Phase 4. Taint engine + OWASP ==='

$null = New-Bead -Title 'Taint: TaintLattice (Clean / Tainted(sources) / Sanitized)' -Type task -Priority 0 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-core') `
    -Description "Решётка TaintState с tracking множества источников (HashSet<TaintSource>). Join — union sources. Sanitized poisoning одного из путей не сбрасывает все источники." `
    -Acceptance "Юнит-тесты 15+ кейсов."

$null = New-Bead -Title 'Taint: YAML конфигурация sources/sinks/sanitizers (YamlDotNet)' -Type task -Priority 0 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-config') `
    -Description "OpenVulScan.Configuration.TaintConfig: модель + парсер YamlDotNet. Поддержка glob-паттернов в method signature (System.IO.File::Open*(string,*))." `
    -Acceptance "Юнит-тест: загрузка config/taint.builtin.yaml; неверный YAML даёт человекочитаемую ошибку."

$null = New-Bead -Title 'Taint: propagators для string concat / format / interpolate' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-core') -Description 'Transfer-функции taint для +, string.Format, интерполированные строки ($"..."), string.Concat.' `
    -Acceptance "Юнит-тесты: tainted фрагмент пропагируется через все 4 паттерна."

$null = New-Bead -Title 'Taint: propagators для StringBuilder / char[] / Span<char> / LINQ' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-core') -Description "Transfer-функции для StringBuilder.Append/Append, char[]→string, ReadOnlySpan, Select/Where/Aggregate." `
    -Acceptance "Юнит-тесты для всех 4 семейств."

$null = New-Bead -Title 'Taint: расширение IFDS для taint-графа (k-CFA, k=1)' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 480 `
    -Labels @('phase-4','area-core') -Description "Per-parameter, per-return taint-fact пропуск через summaries. K=1 контекст для basics." `
    -Acceptance "На синтетическом наборе из 5 цепочек (Source → 2-3 method calls → Sink) taint доезжает."

$null = New-Bead -Title 'API-models: ASP.NET Core / Web (HttpRequest, FromQuery, FromBody)' -Type task -Priority 0 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-config') -Description "config/taint/aspnetcore.yaml: sources HttpRequest.Form/Query/Headers/Body, FromQuery/FromBody/FromRoute атрибуты, IConfiguration." `
    -Acceptance "Юнит-тест: source detected на минимальной web app."

$null = New-Bead -Title 'API-models: EF Core / Dapper / ADO.NET' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 300 `
    -Labels @('phase-4','area-config') -Description "Sinks: DbCommand.CommandText, FromSqlRaw/SqlInterpolated, DbConnection.Execute*, Dapper Execute/Query." `
    -Acceptance "Юнит-тест: V5608 ловит SQLi через Dapper и EF Core."

$null = New-Bead -Title 'API-models: System.IO + System.Diagnostics.Process' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-config') -Description "Sinks для path traversal (File.Open*, FileStream..ctor, Directory.Get*) и command injection (Process.Start)." `
    -Acceptance "Юнит-тесты V5609 + V5616."

$null = New-Bead -Title 'API-models: System.Xml + Newtonsoft.Json + System.Text.Json' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-config') -Description "Sinks для XXE (XmlDocument.LoadXml/XmlReader), insecure deser (BinaryFormatter, JsonConvert.DeserializeObject<TypeName>)." `
    -Acceptance "Юнит-тесты V5611 + V5614."

$null = New-Bead -Title 'API-models: System.Net.Http + LDAP + крипто' -Type task -Priority 2 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-config') -Description "Sinks: HttpClient.GetAsync (SSRF), DirectoryEntry/DirectorySearcher (LDAP), DES/MD5 (weak crypto), SslProtocols.Tls/Ssl3 (outdated TLS)." `
    -Acceptance "Юнит-тесты V5618 + V5620 + V5612 + V5613."

$null = New-Bead -Title 'Rules P4 taint: V5608 SQL injection' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-rules','rule-V5608') -Description "TaintRule: tainted строка достигает SqlCommand/DbCommand sink без sanitizer." `
    -Acceptance "Snapshot 7+ кейсов (raw SqlCommand, EF FromSqlRaw, Dapper)."

$null = New-Bead -Title 'Rules P4 taint: V5609 path traversal' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5609') -Acceptance "Snapshot 5+ кейсов." `
    -Description "TaintRule на File/Directory sinks с tainted path."

$null = New-Bead -Title 'Rules P4 taint: V5610 XSS' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5610') -Acceptance "Snapshot 5+ кейсов." `
    -Description "TaintRule на HtmlString/Razor/Response.Write sinks."

$null = New-Bead -Title 'Rules P4 taint: V5611 insecure deserialization' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5611') -Acceptance "Snapshot 5+ кейсов." `
    -Description "TaintRule на BinaryFormatter.Deserialize, JsonConvert.DeserializeObject<TypeName>."

$null = New-Bead -Title 'Rules P4 taint: V5614 XXE' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5614') -Acceptance "Snapshot 5+ кейсов." `
    -Description "TaintRule на XmlDocument.LoadXml без DtdProcessing=Prohibit."

$null = New-Bead -Title 'Rules P4 taint: V5616 command injection' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5616') -Acceptance "Snapshot 5+ кейсов." `
    -Description "TaintRule: tainted строка в Process.Start arguments или /bin/sh -c."

$null = New-Bead -Title 'Rules P4 taint: V5618 SSRF' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5618') -Acceptance "Snapshot 5+ кейсов." `
    -Description "TaintRule: tainted URL в HttpClient/WebRequest."

$null = New-Bead -Title 'Rules P4 taint: V5619 log injection, V5620 LDAP, V5622 XPath' -Type feature -Priority 2 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-rules','rule-V5619','rule-V5620','rule-V5622') -Description "Tainted string в Logger.Info, DirectorySearcher.Filter, XPathExpression. 3 правила, общий шаблон." `
    -Acceptance "Snapshot 4+4+4 кейсов."

$null = New-Bead -Title 'Rules P4 taint: V5623 open redirect, V5627 NoSQL, V5628 Zip Slip, V5631 format string' -Type feature -Priority 2 -Parent $EPIC4 -EstimateMinutes 480 `
    -Labels @('phase-4','area-rules','rule-V5623','rule-V5627','rule-V5628','rule-V5631') `
    -Description "4 taint-правила. Zip Slip требует проверки на Path.Combine + ZipArchiveEntry.FullName." `
    -Acceptance "Snapshot 4 на каждое правило."

$null = New-Bead -Title 'Rules P4 non-taint: V5601 (hardcoded creds), V5612 (outdated TLS), V5613 (weak crypto)' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-rules','rule-V5601','rule-V5612','rule-V5613') `
    -Description "AstRule + SymbolRule: V5601 — pattern match по строкам password/pwd/secret + literal; V5612 — SslProtocols.Tls/Ssl3; V5613 — DES/MD5/RC4." `
    -Acceptance "Snapshot 5+5+5 кейсов."

$null = New-Bead -Title 'Rules P4 non-taint: V5625 (vulnerable NuGet) + V5626 (ReDoS) + V5629 (Trojan Source)' -Type feature -Priority 2 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-rules','rule-V5625','rule-V5626','rule-V5629') `
    -Description "V5625 — обращение к GitHub Advisory DB / NVD по ID NuGet (offline кэш + REST fallback). V5626 — анализ regex pattern на полиномиальную BACKtrack (через grep). V5629 — token-level: invisible characters U+202A..U+202E, U+2066..U+2069 в строковых литералах и комментариях." `
    -Acceptance "Snapshot 5+5+5 кейсов; V5625 проверяет известный pinned CVE."

Write-Host ''
Write-Host '=== Phase 5. IDE/CI integration ==='

$null = New-Bead -Title 'MSBuild target: OpenVulScan.targets подцепляется в dotnet build' -Type task -Priority 0 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') -Description "Создать пакет OpenVulScan.MSBuild, который через targets/props автоматически вызывает CLI после CoreCompile и парсит SARIF в формат MSBuild warnings/errors. Поддержка suppression, severity-mapping." `
    -Acceptance "На demo solution: PackageReference добавляется, dotnet build печатает diagnostics в стандартный Error List формате."

$null = New-Bead -Title 'Rider plugin: skeleton (Gradle + IntelliJ Platform SDK + Kotlin)' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') -Description "Plugin загружается в Rider 2026+, manifest, run configuration, базовые actions." `
    -Acceptance "Plugin .jar собирается, Rider sandbox loads без ошибок."

$null = New-Bead -Title 'Rider plugin: запуск анализа из меню и через action' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') -Description "AnAction'ы Tools > OpenVulScan > Analyze Solution/Current File. Streaming SARIF из CLI." `
    -Acceptance "Из меню запускается, прогресс в notification bar."

$null = New-Bead -Title 'Rider plugin: отображение в Problems tab + jump to code' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') -Description "Конвертация SARIF result → ProblemsView item. Поддержка group by file/severity." `
    -Acceptance "Пример с 10 diagnostic'ами визуально корректен, клик ведёт к коду."

$null = New-Bead -Title 'Rider plugin: quick-fixes через ReSharper SDK (там, где применимо)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') -Description "Для 5-10 простых правил (V3007, V3005, V3022 на always true) добавить quick-fix." `
    -Acceptance "5+ quick-fixes работают на demo проекте."

$null = New-Bead -Title 'VS 2026 extension: VSIX skeleton (на новой 64-bit платформе)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') -Description "VSIX manifest, Source.extension.vsixmanifest, ToolsCommand. Установка в Experimental Hive." `
    -Acceptance "VSIX подгружается без ошибок."

$null = New-Bead -Title 'VS 2026: DiagnosticAnalyzer-shim для AST правил' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') -Description "Адаптер AstRule → Roslyn DiagnosticAnalyzer (для лёгких правил, чтобы они работали в live-режиме редактора)." `
    -Acceptance "В VS 2026 squiggle для V3007 появляется на лету."

$null = New-Bead -Title 'VS 2026: out-of-process invoke для тяжёлых DFA-правил' -Type task -Priority 3 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') -Description "Для DataFlow/Path-Sensitive/Taint правил — фоновый запуск CLI в side-car процессе, инкрементальный режим, чтение SARIF." `
    -Acceptance "Demo: при сохранении файла DFA-предупреждения обновляются < 5 с."

$null = New-Bead -Title 'GitHub Action: openvulscan/action@v1 (composite)' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 240 `
    -Labels @('phase-5','area-ci') -Description "Composite action: setup-dotnet, restore, run openvulscan analyze --format sarif, upload-sarif в Code Scanning." `
    -Acceptance "На demo репозитории action отрабатывает и SARIF появляется в Security > Code scanning."

$null = New-Bead -Title 'Azure DevOps task: publish SARIF в Scans' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 180 `
    -Labels @('phase-5','area-ci') -Description "Azure DevOps task.json + ts-скрипт, который оборачивает CLI." `
    -Acceptance "Pipeline на demo репозитории корректно публикует SARIF."

$null = New-Bead -Title 'SonarQube exporter: Generic Issue Format (+ SARIF importer)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 240 `
    -Labels @('phase-5','area-cli') -Description "Команда `ovs export --format sonar` берёт SARIF и превращает в Sonar Generic Issue JSON." `
    -Acceptance "SonarQube Community 10+ корректно поглощает на demo проекте."

$null = New-Bead -Title 'Docs: DocFX-сайт + структура (Reference, Rules, Guides, ADR)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-docs') -Description "docs/site/: DocFX-проект, генерация HTML на CI. Reference авто из xml docs, rules — генерируется из RuleRegistry, ADR — markdown." `
    -Acceptance "На CI собирается статический сайт; деплой на gh-pages."

$null = New-Bead -Title 'Docs: Quickstart + примеры на каждую категорию правил' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 240 `
    -Labels @('phase-5','area-docs') -Description "samples/: per-category папки с тестовыми проектами; в docs — пошаговое описание." `
    -Acceptance "Samples собираются в CI, ссылки из docs работают."

Write-Host ''
Write-Host '=== Phase 6. Калибровка FP-rate и наращивание правил ==='

$null = New-Bead -Title 'Corpus: git submodule add dotnet/roslyn (pinned commit)' -Type task -Priority 1 -Parent $EPIC6 -EstimateMinutes 60 `
    -Labels @('phase-6','area-corpus') -Description "corpus/roslyn: submodule на конкретный SHA, который собирается на dotnet 10." `
    -Acceptance "git submodule update --init работает, openvulscan analyze corpus/roslyn запускается."

$null = New-Bead -Title 'Corpus: остальные 9 эталонных проектов (aspnetcore, runtime, Avalonia, MonoGame, NodaTime, ImageSharp, akka.net, Dapper, efcore.pg)' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 180 `
    -Labels @('phase-6','area-corpus') -Description "Каждый — git submodule с pinned commit. README со списком + что заскрапали." `
    -Acceptance "Все 10 submodules инициализируются, прогон openvulscan analyze не падает."

$null = New-Bead -Title 'Triage tool: TP/FP/borderline UI (web или CLI prompt)' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 600 `
    -Labels @('phase-6','area-tools') -Description "tools/triage: интерфейс показывает diagnostic + сниппет кода, кнопки TP/FP/Borderline. Сохранение CSV." `
    -Acceptance "Тестовый прогон на 100 diagnostic'ах: разметка занимает < 30 мин."

$null = New-Bead -Title 'Run: прогон на корпусе + baseline для всех 10 репозиториев' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 240 `
    -Labels @('phase-6','area-corpus') -Description "tools/corpus-runner: orchestrates прогон + сохранение baseline под corpus/*/.openvulscan.suppress + diff в CI." `
    -Acceptance "Зелёный CI corpus job. На PR diff лежит в artifacts."

$null = New-Bead -Title 'Metrics: FP-rate отчёт по уровням (sample 100, точность ±10%)' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 240 `
    -Labels @('phase-6','area-tools') -Description "Скрипт: случайно отобрать 100 diagnostic'ов на корпусе, дать triage, вычислить FP-rate и confidence interval. Публиковать в docs/quality-report.md." `
    -Acceptance "Отчёт сгенерирован на тестовом запуске, цифры разумные."

$null = New-Bead -Title 'Differential: vs Roslyn analyzers (intersect на пересекающихся правилах)' -Type task -Priority 3 -Parent $EPIC6 -EstimateMinutes 360 `
    -Labels @('phase-6','area-tools') -Description "tools/diff-roslyn: запустить наш + стандартные Roslyn warnings (CS, IDE) на corpus/*; вычислить overlap, only-us, only-them." `
    -Acceptance "Отчёт docs/diff-roslyn.md."

$null = New-Bead -Title 'Differential: vs SonarAnalyzer.CSharp' -Type task -Priority 3 -Parent $EPIC6 -EstimateMinutes 360 `
    -Labels @('phase-6','area-tools') -Description "tools/diff-sonar: запустить SonarAnalyzer.CSharp на corpus/*; сопоставить с нашими правилами." `
    -Acceptance "Отчёт docs/diff-sonar.md."

$null = New-Bead -Title 'Rules growth: наращивание реестра до ~200 правил (план поэтапной добавки)' -Type epic -Priority 3 -Parent $EPIC6 -EstimateMinutes 0 `
    -Labels @('phase-6','area-rules') -Description "Отдельный эпик с серией micro-task'ов: каждые 10-15 правил отдельная итерация с triage и FP-калибровкой. Сериализуется ANALYZER_PLAN.md §7.6." `
    -Acceptance "В реестре >=200 правил, FP-rate на корпусе соответствует целям из §9."

Write-Host ''
Write-Host '✓ Phase 2..6 готовы.'
