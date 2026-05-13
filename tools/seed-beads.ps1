#requires -Version 7
<#
.SYNOPSIS
    Засев beads-БД задачами по ANALYZER_PLAN.md.

.DESCRIPTION
    Идемпотентный (повторно НЕ запускать без 'bd init' с чистой БД).
    Создаёт 7 эпиков (Фазы 0..6) и подзадачи под каждый эпик,
    плюс межэпиковые блокирующие зависимости.

    Перед запуском:
        bd init --prefix ovs --non-interactive
    Затем:
        pwsh tools/seed-beads.ps1
#>

$ErrorActionPreference = 'Stop'

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
    if (-not $id) { throw "Не получили ID для задачи '$Title'" }
    Write-Host "  + $id  $Title"
    return $id
}

function Add-Dep {
    param([Parameter(Mandatory)][string]$Blocked, [Parameter(Mandatory)][string]$Blocker)
    & bd dep add $Blocked $Blocker | Out-Null
}

Write-Host '=== Создаю эпики (Фазы 0..6) ==='
$EPIC0 = New-Bead -Title 'Фаза 0. R&D и spike' -Type epic -Priority 0 `
    -Description "Цель: убрать неопределённости в инструментарии (Roslyn, MSBuildWorkspace, IOperation CFG, базовая решётка). Длительность 1-2 мес full-time." `
    -Acceptance 'Закрыты все подзадачи Phase 0; написан ADR-001 Architecture overview; CI зелёный; репозиторий содержит spike-проекты.' `
    -EstimateMinutes 0 -Labels @('phase-0')

$EPIC1 = New-Bead -Title 'Фаза 1. MVP CLI + 30 правил (AST/Symbol)' -Type epic -Priority 0 `
    -Description "Рабочий CLI, анализирует solution и выводит SARIF/JSON/text. Реализованы 30 простых AST/Symbol правил + 4 fails. Подавление через inline-маркеры, атрибут SuppressMessage, baseline-файл с fuzzy-fingerprint. Snapshot tests на Verify." `
    -Acceptance 'Реализованы 30 AST/Symbol правил, есть CLI команды analyze/baseline/rules list, SARIF 2.1.0 проходит OASIS schema validator, snapshot-тесты зелёные на эталонных примерах.' `
    -EstimateMinutes 0 -Labels @('phase-1')

$EPIC2 = New-Bead -Title 'Фаза 2. Внутрипроцедурный DFA + path-sensitive' -Type epic -Priority 1 `
    -Description "Фундамент для основной массы V3xxx: решётки (Null/Const/Interval/Init/Dispose/Map), worklist-solver на RPO, SSA-нумерация, edge-condition refinement, bounded path exploration (limit 64). +40 правил DFA-зависимых." `
    -Acceptance 'Реализованы 6 решёток + worklist-solver + SSA + path-refinement; в реестре +40 правил из V3022/V3027/V3056/V3063/V3095/V3080/V3105/V3110/V3142/V3148/V3074/V3097/V3122 и подобных; snapshot-тесты зелёные.' `
    -EstimateMinutes 0 -Labels @('phase-2')

$EPIC3 = New-Bead -Title 'Фаза 3. Межпроцедурный анализ + summaries' -Type epic -Priority 1 `
    -Description "Точность на реальных проектах: CHA+RTA call graph, per-method procedure summaries (MessagePack), IFDS/IDE-фреймворк, bottom-up SCC обход, инкрементальный режим (dependency graph + dirty propagation), параллелизм, ограничители памяти." `
    -Acceptance 'Builds call graph for Roslyn solution; summaries persist between runs; incremental rerun после 1-файлового изменения < 15 c; +30-50 inter-procedural правил.' `
    -EstimateMinutes 0 -Labels @('phase-3')

$EPIC4 = New-Bead -Title 'Фаза 4. Taint engine + OWASP правила' -Type epic -Priority 1 `
    -Description "TaintLattice, конфигурация YAML sources/sinks/sanitizers, propagators для string/StringBuilder/LINQ, расширение IFDS для taint-графа, библиотека API-моделей (ASP.NET Core, EF Core, Dapper, ADO.NET, System.IO, System.Xml, Net.Http, LDAP, крипто). 20 приоритетных + 11 остальных правил V56xx." `
    -Acceptance 'YAML-конфиг загружается из репозитория; taint-engine ловит SQLi, XSS, path-traversal, command-injection, SSRF на синтетических примерах; реализованы 14 taint правил и 6 не-taint правил из группы owasp.' `
    -EstimateMinutes 0 -Labels @('phase-4')

$EPIC5 = New-Bead -Title 'Фаза 5. IDE и CI интеграция (Rider 2026+/VS 2026)' -Type epic -Priority 2 `
    -Description "MSBuild target, JetBrains Rider 2026+ plugin (IntelliJ Platform SDK + ReSharper SDK), VS 2026 VSIX (DiagnosticAnalyzer shim + out-of-process для DFA), GitHub Action, Azure DevOps task, SonarQube Generic Issue Format exporter, DocFX-сайт." `
    -Acceptance 'OpenVulScan.targets автоматически подцепляется на dotnet build, Rider plugin показывает diagnostics в Problems tab, VSIX подгружается в VS 2026, GitHub Action публикует SARIF в Code Scanning.' `
    -EstimateMinutes 0 -Labels @('phase-5')

$EPIC6 = New-Bead -Title 'Фаза 6. Калибровка FP-rate и наращивание правил до ~200' -Type epic -Priority 2 `
    -Description "Корпус из 10 эталонных open-source решений; triage tool TP/FP/borderline; differential testing против Roslyn analyzers и SonarAnalyzer.CSharp; FP-rate <=30% (L1) и <=50% (L2); рост до ~200 правил." `
    -Acceptance 'На корпусе зафиксированы baseline-файлы; FP-rate в выборке 100 диагностик L1<=30% и L2<=50%; реестр доведён до ~200 правил.' `
    -EstimateMinutes 0 -Labels @('phase-6')

# Межэпиковые блокирующие зависимости
Write-Host '=== Связываю эпики (blocks) ==='
Add-Dep $EPIC1 $EPIC0
Add-Dep $EPIC2 $EPIC1
Add-Dep $EPIC3 $EPIC2
Add-Dep $EPIC4 $EPIC3      # taint требует межпроцедурного фреймворка
Add-Dep $EPIC5 $EPIC1      # для IDE/CI нужно как минимум MVP
Add-Dep $EPIC6 $EPIC4      # калибровка после основных правил

# Записываем ID эпиков на диск, чтобы вторую половину можно было запускать отдельно
$idsPath = Join-Path $PSScriptRoot '.beads-epics.json'
@{
    EPIC0 = $EPIC0; EPIC1 = $EPIC1; EPIC2 = $EPIC2;
    EPIC3 = $EPIC3; EPIC4 = $EPIC4; EPIC5 = $EPIC5; EPIC6 = $EPIC6
} | ConvertTo-Json | Set-Content -Path $idsPath -Encoding UTF8
Write-Host "Эпики сохранены в $idsPath"

Write-Host ''
Write-Host '=== Phase 0. R&D и spike ==='
$null = New-Bead -Title 'Repo: инициализировать solution OpenVulScan.sln и Directory.Build.props' `
    -Type task -Priority 0 -Parent $EPIC0 -EstimateMinutes 120 `
    -Labels @('phase-0','area-repo') `
    -Description "Создать пустой solution OpenVulScan.sln, файлы Directory.Build.props (TargetFramework=net10.0, Nullable=enable, TreatWarningsAsErrors=true, AnalysisLevel=latest-all), Directory.Packages.props (Central Package Management), .editorconfig, .gitignore от Microsoft .NET. Проверить, что dotnet build из пустого solution проходит." `
    -Acceptance "OpenVulScan.sln создан и собирается командой `dotnet build` без предупреждений. Установлен .NET 10 SDK. В корне репозитория есть Directory.Build.props, Directory.Packages.props, .editorconfig, .gitignore."

$null = New-Bead -Title 'CI: настроить GitHub Actions baseline (build + test matrix win/linux/osx)' `
    -Type task -Priority 1 -Parent $EPIC0 -EstimateMinutes 180 `
    -Labels @('phase-0','area-ci') `
    -Description "В .github/workflows/build.yml собирается матрица ubuntu-latest/windows-latest/macos-latest на dotnet 10. Кэширование NuGet. Шаги: restore, build (Release), test. На PR обязателен прогон. Защита ветки main по чек-листу: build, test." `
    -Acceptance "PR в main блокируется до зелёного workflow. Workflow выполняется < 8 мин на пустом репозитории."

$null = New-Bead -Title 'ADR-001: Architecture overview' `
    -Type decision -Priority 0 -Parent $EPIC0 -EstimateMinutes 240 `
    -Labels @('phase-0','area-docs','adr') `
    -Description "Записать docs/adr/001-architecture-overview.md по шаблону: Context / Decision / Consequences. Зафиксировать выбор Roslyn 5.x на .NET 10, MSBuildWorkspace + Build.Locator, отказ от netstandard2.0, формат правил через [Rule]-атрибут, своя SARIF, MessagePack для кеша. Ссылки на разделы 4 и 5 из ANALYZER_PLAN.md." `
    -Acceptance "Файл docs/adr/001-architecture-overview.md существует, прошёл peer-review, помечен Status: Accepted."

$null = New-Bead -Title 'Spike: загрузка solution через MSBuildWorkspace на 3 эталонных проектах' `
    -Type task -Priority 1 -Parent $EPIC0 -EstimateMinutes 480 `
    -Labels @('phase-0','area-frontend','spike') `
    -Description "В spikes/MsbuildLoader: загрузить dotnet/roslyn (HEAD pinned), dotnet/aspnetcore (HEAD pinned), MonoGame через MSBuildWorkspace + MSBuildLocator. Логировать число загруженных Project, Compilation, Diagnostic. Замерить пиковую RAM и время." `
    -Acceptance "Запуск spikes/MsbuildLoader на 3 эталонных проектах не падает; в README spike приведены численные результаты (LoC, RAM peak, время). Известные проблемные проекты задокументированы."

$null = New-Bead -Title 'Spike: построить ControlFlowGraph для одного метода через IOperation' `
    -Type task -Priority 1 -Parent $EPIC0 -EstimateMinutes 240 `
    -Labels @('phase-0','area-core','spike') `
    -Description "spikes/CfgPlay: для произвольного *.cs файла напечатать ControlFlowGraph.Create(IOperation) для каждого метода: блоки, edges, заголовки IOperation. Понять API ConditionalEdge/FallThrough, OperationKind.FlowCapture/Reference, ImplicitInstance." `
    -Acceptance "spikes/CfgPlay печатает CFG для 5 разных методов из тестовых файлов; в README spike описаны главные сущности (BasicBlock, ControlFlowBranch, CaptureId)."

$null = New-Bead -Title 'Spike: реализовать прототип V3001 (identical sub-expressions) на IOperation' `
    -Type task -Priority 1 -Parent $EPIC0 -EstimateMinutes 240 `
    -Labels @('phase-0','area-rules','spike') `
    -Description "spikes/Rule3001: ходить по IOperation, на бинарных операторах сравнивать левый и правый поддерево по структурному равенству (типы и членам). При совпадении печатать file:line. Реализовать 5 позитивных и 3 негативных кейса." `
    -Acceptance "На 5 тестовых файлах прототип выдаёт ожидаемые срабатывания и не выдаёт ложных на 3 отрицательных."

$null = New-Bead -Title 'Spike: NullStateLattice на 10-строчных примерах' `
    -Type task -Priority 1 -Parent $EPIC0 -EstimateMinutes 360 `
    -Labels @('phase-0','area-core','spike') `
    -Description "spikes/NullSpike: примитивная решётка NullState {Unknown, NotNull, MaybeNull, DefinitelyNull} с операцией Join. Naive transfer для assignment, null-literal, member access. Прогон на 10-строчных примерах. Цель — нащупать API, не точность." `
    -Acceptance "Spike работает на 5 примерах из docs/spikes/null-cases.md и определяет состояние локальных переменных корректно."

$null = New-Bead -Title 'README + дерево папок репозитория (src/, tests/, docs/, corpus/, samples/, tools/, .github/)' `
    -Type task -Priority 2 -Parent $EPIC0 -EstimateMinutes 60 `
    -Labels @('phase-0','area-docs') `
    -Description "Создать README.md с целью проекта, быстрым стартом и таблицей фаз. Структуру папок зафиксировать пустыми .gitkeep, чтобы дерево из раздела 6 плана появилось в репо." `
    -Acceptance "git ls-files показывает дерево из ANALYZER_PLAN.md §6. README отображается на главной странице репозитория."

Write-Host ''
Write-Host '=== Phase 1. MVP ==='
$P1_SKEL = New-Bead -Title 'Skeleton: завести все проекты OpenVulScan.* и tests/* по структуре §6' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-repo') `
    -Description "Создать csproj-ы: OpenVulScan.Core, .Frontend, .RuleEngine, .Rules.Ast, .Rules.DataFlow (заглушка), .Rules.PathSensitive (заглушка), .Rules.Taint (заглушка), .Rules.Performance (заглушка), .Sarif, .Cache, .Configuration, .Cli. Тестовые: .Core.Tests, .Rules.Tests, .Integration.Tests, .CorpusBench. Все ссылаются друг на друга по правилам слоистости из §4.1. Все собираются в одном dotnet build." `
    -Acceptance "dotnet build OpenVulScan.sln зелёный; все 12 src/ и 4 tests/ проектов есть; ссылки совпадают с §4.1."

$P1_LOADER_SDK = New-Bead -Title 'Frontend: MSBuildWorkspace loader для SDK-style проектов' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 480 `
    -Labels @('phase-1','area-frontend') `
    -Description "В OpenVulScan.Frontend реализовать ProjectLoader, который через MSBuildLocator.RegisterDefaults() + MSBuildWorkspace.Create() открывает .sln/.csproj. Поддерживается NuGet restore. Возвращает Solution + список Project. Хорошие ошибки на отсутствие SDK, missing references, циклические зависимости." `
    -Acceptance "На 3 эталонных проектах из spike 0 loader возвращает >=1 Compilation без unrecoverable diagnostic. Покрытие unit-тестами >=70%."

$P1_LOADER_LEGACY = New-Bead -Title 'Frontend: поддержка legacy MSBuild + fallback compile_commands.json' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 360 `
    -Labels @('phase-1','area-frontend') `
    -Description "Дополнить ProjectLoader fallback-режимом: если MSBuildWorkspace не справился, читать compile-commands JSON (схема из docs/refs.md) и собирать AdhocWorkspace вручную. Документировать формат." `
    -Acceptance "На искусственно сломанном csproj loader корректно переключается в fallback и собирает Compilation. Юнит-тесты с моками."

$P1_REG = New-Bead -Title 'RuleEngine: реестр правил и [Rule]-атрибут' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-engine') `
    -Description "Создать атрибут [Rule(Code, DefaultLevel, Cwe, Category, Capabilities)] и регистратор RuleRegistry, который через reflection собирает все типы с этим атрибутом. Сериализация в JSON для CLI команды rules list." `
    -Acceptance "RuleRegistry.GetAll() возвращает зарегистрированные правила; покрытие unit-тестами >=80%."

$P1_AST = New-Bead -Title 'RuleEngine: базовый класс AstRule' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 180 `
    -Labels @('phase-1','area-engine') `
    -Description "Абстрактный AstRule с виртуальными On<NodeKind>(SyntaxNodeContext context) методами, диспетчер по SyntaxKind. Регистрация подписок ленивая." `
    -Acceptance "Юнит-тест: dummy-правило получает Visit'ы для нужных node kinds."

$P1_SYM = New-Bead -Title 'RuleEngine: базовый класс SymbolRule' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 180 `
    -Labels @('phase-1','area-engine') `
    -Description "Абстрактный SymbolRule, обходит ISymbol-ы (методы, классы) с предоставлением Compilation + SemanticModel." `
    -Acceptance "Юнит-тест: dummy-правило вызывается на нужных symbol kinds."

$P1_SCHED = New-Bead -Title 'RuleEngine: scheduler/диспетчер правил по Compilation' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 300 `
    -Labels @('phase-1','area-engine') `
    -Description "RuleScheduler принимает Compilation, выбирает применимые правила, последовательно обходит SyntaxTrees и собирает Diagnostic'и. Многопоточность откладываем до §4.9, но API уже асинхронный (Task)." `
    -Acceptance "End-to-end тест: реестр + 1 dummy AstRule на synthetic.cs выдаёт ожидаемые diagnostic'и."

$P1_SARIF = New-Bead -Title 'Sarif: SARIF 2.1.0 writer (схема OASIS)' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 480 `
    -Labels @('phase-1','area-sarif') `
    -Description "OpenVulScan.Sarif: писать SARIF 2.1.0 с полями run, tool.driver, results, locations, ruleId, level (note/warning/error). Mapping CWE/OWASP через properties bag. Файл проходит OASIS schema." `
    -Acceptance "Юнит-тест сравнения SARIF-выхода со snapshot; xtreme.sarif валидируется online OASIS schema validator."

$P1_JSON = New-Bead -Title 'Sarif: дополнительные emitters JSON и plain text' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 180 `
    -Labels @('phase-1','area-sarif') `
    -Description "OpenVulScan.Sarif.JsonEmitter (компактный JSON для downstream tooling) и TextEmitter (CI-friendly формат `path(line,col): warning Vxxxx: message`)." `
    -Acceptance "Snapshot-тесты для обоих emitters."

$P1_CLI_ANALYZE = New-Bead -Title 'CLI: команда `ovs analyze`' `
    -Type task -Priority 0 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-cli') `
    -Description "System.CommandLine v2: команда `ovs analyze <path>` с флагами --format sarif|json|text, --output <file>, --suppress <baseline>, --include/--exclude. Возвращает exit code 0 при отсутствии Level-1 диагностик, 1 иначе." `
    -Acceptance "На синтетическом solution команда работает за <5 сек, формирует sarif/json/text без stacktraces."

$P1_CLI_RULES = New-Bead -Title 'CLI: команда `ovs rules list`' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 120 `
    -Labels @('phase-1','area-cli') `
    -Description "Печатает таблицу Code/Level/Category/Cwe/Capabilities/ShortDescription для всех зарегистрированных правил. Флаги --format json|text, --enabled-only." `
    -Acceptance "Юнит-тесты snap. Команда работает <500 мс."

$P1_CLI_BASELINE = New-Bead -Title 'CLI: команды `ovs baseline create|update|diff`' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 360 `
    -Labels @('phase-1','area-cli') `
    -Description "Подкоманды генерируют, сравнивают и обновляют baseline-файл openvulscan.suppress. create — снимает все текущие diagnostic'и; update — добавляет новые без удаления старых; diff — печатает delta. Файл сериализуется в простом ASCII формате, см. §4.7." `
    -Acceptance "Сценарий create→update→diff на тестовом проекте отрабатывает корректно; baseline стабилен между запусками."

$P1_SUPP_INLINE = New-Bead -Title 'Suppression: inline-маркеры (// ovs:disable, // ovs:disable-next-line, // ovs:disable-block)' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 300 `
    -Labels @('phase-1','area-engine') `
    -Description "Регистратор подавлений обрабатывает три формата комментариев; работает по диапазонам строк; разделение по rule code (V3001,V3022 в одной директиве). Проверка корректного раскрытия `disable-block`/`enable-block`." `
    -Acceptance "Юнит-тесты + интеграционный тест: после inline-маркера соответствующий diagnostic не появляется в SARIF."

$P1_SUPP_ATTR = New-Bead -Title 'Suppression: атрибут [SuppressMessage(\"OpenVulScan\", \"Vxxxx\")]' `
    -Type task -Priority 2 -Parent $EPIC1 -EstimateMinutes 120 `
    -Labels @('phase-1','area-engine') `
    -Description "Учитывать атрибут SuppressMessageAttribute (System.Diagnostics.CodeAnalysis) с категорией OpenVulScan." `
    -Acceptance "Юнит-тест на класс/метод/локальную переменную."

$P1_SUPP_BASELINE = New-Bead -Title 'Suppression: baseline-файл с fuzzy-fingerprint (±N строк)' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 360 `
    -Labels @('phase-1','area-engine') `
    -Description "Сериализуемый baseline: для каждого подавленного diagnostic хранится {hash(snippet нормализованного), rule code, relative path}. Сравнение допускает сдвиг ±N строк и нормализует whitespace. Поведение как у PVS .suppress." `
    -Acceptance "Сценарий: переместили подавленную строку на ±5 — baseline по-прежнему подавляет."

$P1_TESTS = New-Bead -Title 'Tests: snapshot-фреймворк на Verify для правил' `
    -Type task -Priority 1 -Parent $EPIC1 -EstimateMinutes 180 `
    -Labels @('phase-1','area-tests') `
    -Description "tests/OpenVulScan.Rules.Tests: harness, который компилит *.cs, прогоняет реестр и сравнивает SARIF со *.verified.json через Verify. Каждый тест = 1 правило × N кейсов." `
    -Acceptance "Один пример (V3001) проходит через harness."

# Группы правил Phase 1 — 30 шт., 9 батчей по 3-4 правила
$null = New-Bead -Title 'Rules P1: V3001 — Identical sub-expressions on the both sides of operator' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-rules','rule-V3001') `
    -Description "Реализовать AstRule (BinaryExpressionSyntax). Сравнение поддеревьев через нормализатор (System.Linq SyntaxNodeEqualityComparer). Исключения: floating-point NaN-сравнения, side-effect-вызовы." `
    -Acceptance "Snapshot >=10 позитивных + >=5 негативных кейсов; FP на synthetic.cs = 0."

$null = New-Bead -Title 'Rules P1: V3003 + V3004 (if(A) else if(A); then==else)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-rules','rule-V3003','rule-V3004') `
    -Description "AstRule по IfStatementSyntax. V3003 — структурное сравнение условия if и else-if. V3004 — структурное сравнение блока then и else." `
    -Acceptance "Snapshot 5+5 кейсов на каждое правило."

$null = New-Bead -Title 'Rules P1: V3005 + V3007 (self-assignment; odd semicolon после if/for/while)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 180 `
    -Labels @('phase-1','area-rules','rule-V3005','rule-V3007') `
    -Description "V3005: AstRule на AssignmentExpressionSyntax с проверкой equality LHS и RHS. V3007: AstRule на if/for/while statements, у которых тело — EmptyStatementSyntax." `
    -Acceptance "Snapshot 5+5 кейсов."

$null = New-Bead -Title 'Rules P1: V3009 + V3013 + V3014 (always same return; loop вариации)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 300 `
    -Labels @('phase-1','area-rules','rule-V3009','rule-V3013','rule-V3014') `
    -Description "V3009: SymbolRule по IMethodSymbol — собрать все ReturnStatement, если все литералы и совпадают — diagnostic. V3013: AstRule — switch-fallthrough без default. V3014: AstRule — wrong variable incremented in for(...)." `
    -Acceptance "Snapshot 3..5 кейсов на правило."

$null = New-Bead -Title 'Rules P1: V3016 + V3025 (loop var = outer var; format mismatch count)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-rules','rule-V3016','rule-V3025') `
    -Description "V3016: AstRule, проверяет совпадение induction variable вложенного цикла с внешним. V3025: AstRule на InvocationExpressionSyntax string.Format/Console.WriteLine — посчитать number of arguments vs placeholders." `
    -Acceptance "Snapshot 5+5 кейсов."

$null = New-Bead -Title 'Rules P1: V3037 + V3038 + V3041 (A=B;B=A; повтор аргументов; int->real loss)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-rules','rule-V3037','rule-V3038','rule-V3041') `
    -Description "V3037: AstRule — последовательные assignment A=B;B=A. V3038: AstRule — invocation с одинаковым аргументом, переданным в 2+ позиции (heuristic на ArgumentList). V3041: SymbolRule — выражение неявно cast int→double, через SemanticModel.GetConversion." `
    -Acceptance "Snapshot 5/5/5 кейсов."

$null = New-Bead -Title 'Rules P1: V3081 + V3084 (нестартующий нестируемый counter; anon unsubscribe)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-rules','rule-V3081','rule-V3084') `
    -Description "V3081: AstRule на ObjectCreationExpression Exception без последующего throw в том же blocke. V3084: AstRule -=  ленточно проверять анонимные функции в -= операторах event." `
    -Acceptance "Snapshot 4+4 кейса."

$null = New-Bead -Title 'Rules P1: V3105 + V3110 (NRE после ?., Equals symmetry)' `
    -Type feature -Priority 1 -Parent $EPIC1 -EstimateMinutes 240 `
    -Labels @('phase-1','area-rules','rule-V3105','rule-V3110') `
    -Description "V3105 (AST-вариант, без DFA): на цепочке `a?.b.c` обнаружить дальнейшее использование без null-проверки. V3110: SymbolRule на типы, переопределяющие Equals(object) без GetHashCode и наоборот." `
    -Acceptance "Snapshot 5+5 кейсов. Полная DFA-версия V3105 будет в Phase 2."

$null = New-Bead -Title 'Rules P1: группа fails (V009/V051/V052/V053)' `
    -Type feature -Priority 2 -Parent $EPIC1 -EstimateMinutes 120 `
    -Labels @('phase-1','area-rules','rule-V009','rule-V051','rule-V052','rule-V053') `
    -Description "Эти 4 — не диагностики, а служебные сообщения. Реализовать как Event'ы из RuleEngine, печатающиеся в SARIF/text при ошибках загрузки (V051 missing references, V053 .NET types failed) и при критических ошибках (V052)." `
    -Acceptance "Юнит-тест: при искусственно сломанном csproj выдаётся V051. На отсутствие .NET — V053."

$null = New-Bead -Title 'ADR-002: Зачем своя SARIF имплементация (а не Microsoft.CodeAnalysis.Sarif.Driver)' `
    -Type decision -Priority 2 -Parent $EPIC1 -EstimateMinutes 90 `
    -Labels @('phase-1','area-docs','adr') `
    -Description "Зафиксировать решение писать SARIF руками: формат стабилен (OASIS 2.1.0), Microsoft.CodeAnalysis.Sarif.Driver поддерживается слабо." `
    -Acceptance "docs/adr/002-own-sarif.md в статусе Accepted."

$null = New-Bead -Title 'ADR-003: Целевая платформа .NET 10 и отказ от netstandard2.0/Roslyn 4.x' `
    -Type decision -Priority 2 -Parent $EPIC1 -EstimateMinutes 90 `
    -Labels @('phase-1','area-docs','adr') `
    -Description "Зафиксировать: целевые IDE — Rider 2026+ и VS 2026, оба на .NET 10. Старые Roslyn-shim не пишем." `
    -Acceptance "docs/adr/003-net10-target.md в статусе Accepted."

Write-Host ''
Write-Host '✓ Phase 0 и Phase 1 готовы. Дальше — seed-phase2-6.ps1.'
