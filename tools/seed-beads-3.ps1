#requires -Version 7
<#
.SYNOPSIS
    Продолжение засева — Phase 4 (с задачи propagators string), Phase 5, Phase 6.
#>

$ErrorActionPreference = 'Stop'

$idsPath = Join-Path $PSScriptRoot '.beads-epics.json'
$ids = Get-Content $idsPath -Raw | ConvertFrom-Json
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

Write-Host '=== Phase 4 (продолжение) ==='

$null = New-Bead -Title 'Taint: propagators для string concat / format / interpolate' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-core') `
    -Description 'Transfer-функции taint для +, string.Format, интерполированные строки ($"..."), string.Concat.' `
    -Acceptance 'Юнит-тесты: tainted фрагмент пропагируется через все 4 паттерна.'

$null = New-Bead -Title 'Taint: propagators для StringBuilder / char[] / Span<char> / LINQ' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-core') `
    -Description 'Transfer-функции для StringBuilder.Append/Insert, char[] -> string, ReadOnlySpan, LINQ Select/Where/Aggregate.' `
    -Acceptance 'Юнит-тесты для всех 4 семейств.'

$null = New-Bead -Title 'Taint: расширение IFDS для taint-графа (k-CFA, k=1)' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 480 `
    -Labels @('phase-4','area-core') `
    -Description 'Per-parameter, per-return taint-fact пропуск через summaries. K=1 контекст для basics.' `
    -Acceptance 'На синтетическом наборе из 5 цепочек (Source -> 2-3 method calls -> Sink) taint доезжает.'

$null = New-Bead -Title 'API-models: ASP.NET Core / Web (HttpRequest, FromQuery, FromBody)' -Type task -Priority 0 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-config') `
    -Description 'config/taint/aspnetcore.yaml: sources HttpRequest.Form/Query/Headers/Body, FromQuery/FromBody/FromRoute атрибуты, IConfiguration.' `
    -Acceptance 'Юнит-тест: source detected на минимальной web app.'

$null = New-Bead -Title 'API-models: EF Core / Dapper / ADO.NET' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 300 `
    -Labels @('phase-4','area-config') `
    -Description 'Sinks: DbCommand.CommandText, FromSqlRaw/SqlInterpolated, DbConnection.Execute*, Dapper Execute/Query.' `
    -Acceptance 'Юнит-тест: V5608 ловит SQLi через Dapper и EF Core.'

$null = New-Bead -Title 'API-models: System.IO + System.Diagnostics.Process' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-config') `
    -Description 'Sinks для path traversal (File.Open*, FileStream..ctor, Directory.Get*) и command injection (Process.Start).' `
    -Acceptance 'Юнит-тесты V5609 + V5616.'

$null = New-Bead -Title 'API-models: System.Xml + Newtonsoft.Json + System.Text.Json' -Type task -Priority 1 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-config') `
    -Description 'Sinks для XXE (XmlDocument.LoadXml/XmlReader без Prohibit DTD), insecure deser (BinaryFormatter, JsonConvert.DeserializeObject<TypeName>).' `
    -Acceptance 'Юнит-тесты V5611 + V5614.'

$null = New-Bead -Title 'API-models: System.Net.Http + LDAP + крипто' -Type task -Priority 2 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-config') `
    -Description 'Sinks: HttpClient.GetAsync (SSRF), DirectoryEntry/DirectorySearcher (LDAP), DES/MD5 (weak crypto), SslProtocols.Tls/Ssl3 (outdated TLS).' `
    -Acceptance 'Юнит-тесты V5618 + V5620 + V5612 + V5613.'

$null = New-Bead -Title 'Rules P4 taint: V5608 SQL injection' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 240 `
    -Labels @('phase-4','area-rules','rule-V5608') `
    -Description 'TaintRule: tainted строка достигает SqlCommand/DbCommand sink без sanitizer.' `
    -Acceptance 'Snapshot 7+ кейсов (raw SqlCommand, EF FromSqlRaw, Dapper).'

$null = New-Bead -Title 'Rules P4 taint: V5609 path traversal' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5609') `
    -Description 'TaintRule на File/Directory sinks с tainted path.' `
    -Acceptance 'Snapshot 5+ кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5610 XSS' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5610') `
    -Description 'TaintRule на HtmlString/Razor/Response.Write sinks.' `
    -Acceptance 'Snapshot 5+ кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5611 insecure deserialization' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5611') `
    -Description 'TaintRule на BinaryFormatter.Deserialize, JsonConvert.DeserializeObject<TypeName>.' `
    -Acceptance 'Snapshot 5+ кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5614 XXE' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5614') `
    -Description 'TaintRule на XmlDocument.LoadXml без DtdProcessing=Prohibit.' `
    -Acceptance 'Snapshot 5+ кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5616 command injection' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5616') `
    -Description 'TaintRule: tainted строка в Process.Start arguments или /bin/sh -c.' `
    -Acceptance 'Snapshot 5+ кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5618 SSRF' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 180 `
    -Labels @('phase-4','area-rules','rule-V5618') `
    -Description 'TaintRule: tainted URL в HttpClient/WebRequest.' `
    -Acceptance 'Snapshot 5+ кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5619 log injection, V5620 LDAP, V5622 XPath' -Type feature -Priority 2 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-rules','rule-V5619','rule-V5620','rule-V5622') `
    -Description 'Tainted string в Logger.Info, DirectorySearcher.Filter, XPathExpression. 3 правила, общий шаблон.' `
    -Acceptance 'Snapshot 4+4+4 кейсов.'

$null = New-Bead -Title 'Rules P4 taint: V5623 open redirect, V5627 NoSQL, V5628 Zip Slip, V5631 format string' -Type feature -Priority 2 -Parent $EPIC4 -EstimateMinutes 480 `
    -Labels @('phase-4','area-rules','rule-V5623','rule-V5627','rule-V5628','rule-V5631') `
    -Description '4 taint-правила. Zip Slip требует проверки на Path.Combine + ZipArchiveEntry.FullName.' `
    -Acceptance 'Snapshot 4 на каждое правило.'

$null = New-Bead -Title 'Rules P4 non-taint: V5601 hardcoded creds, V5612 outdated TLS, V5613 weak crypto' -Type feature -Priority 1 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-rules','rule-V5601','rule-V5612','rule-V5613') `
    -Description 'AstRule + SymbolRule: V5601 - pattern match по строкам password/pwd/secret + literal; V5612 - SslProtocols.Tls/Ssl3; V5613 - DES/MD5/RC4.' `
    -Acceptance 'Snapshot 5+5+5 кейсов.'

$null = New-Bead -Title 'Rules P4 non-taint: V5625 vulnerable NuGet, V5626 ReDoS, V5629 Trojan Source' -Type feature -Priority 2 -Parent $EPIC4 -EstimateMinutes 360 `
    -Labels @('phase-4','area-rules','rule-V5625','rule-V5626','rule-V5629') `
    -Description 'V5625 - обращение к GitHub Advisory DB / NVD по ID NuGet (offline кэш + REST fallback). V5626 - анализ regex pattern на полиномиальный backtrack. V5629 - token-level: invisible characters U+202A..U+202E, U+2066..U+2069 в строковых литералах и комментариях.' `
    -Acceptance 'Snapshot 5+5+5 кейсов; V5625 проверяет известный pinned CVE.'

Write-Host ''
Write-Host '=== Phase 5. IDE/CI integration ==='

$null = New-Bead -Title 'MSBuild target: OpenVulScan.targets подцепляется в dotnet build' -Type task -Priority 0 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') `
    -Description 'Создать пакет OpenVulScan.MSBuild, который через targets/props автоматически вызывает CLI после CoreCompile и парсит SARIF в формат MSBuild warnings/errors. Поддержка suppression, severity-mapping.' `
    -Acceptance 'На demo solution: PackageReference добавляется, dotnet build печатает diagnostics в стандартный Error List формате.'

$null = New-Bead -Title 'Rider plugin: skeleton (Gradle + IntelliJ Platform SDK + Kotlin)' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') `
    -Description 'Plugin загружается в Rider 2026+, manifest, run configuration, базовые actions.' `
    -Acceptance 'Plugin .jar собирается, Rider sandbox loads без ошибок.'

$null = New-Bead -Title 'Rider plugin: запуск анализа из меню и через action' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') `
    -Description 'AnAction-ы Tools > OpenVulScan > Analyze Solution/Current File. Streaming SARIF из CLI.' `
    -Acceptance 'Из меню запускается, прогресс в notification bar.'

$null = New-Bead -Title 'Rider plugin: отображение в Problems tab + jump to code' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') `
    -Description 'Конвертация SARIF result -> ProblemsView item. Поддержка group by file/severity.' `
    -Acceptance 'Пример с 10 diagnostic-ами визуально корректен, клик ведёт к коду.'

$null = New-Bead -Title 'Rider plugin: quick-fixes через ReSharper SDK (там, где применимо)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') `
    -Description 'Для 5-10 простых правил (V3007, V3005, V3022 на always true) добавить quick-fix.' `
    -Acceptance '5+ quick-fixes работают на demo проекте.'

$null = New-Bead -Title 'VS 2026 extension: VSIX skeleton (на новой 64-bit платформе)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') `
    -Description 'VSIX manifest, Source.extension.vsixmanifest, ToolsCommand. Установка в Experimental Hive.' `
    -Acceptance 'VSIX подгружается без ошибок.'

$null = New-Bead -Title 'VS 2026: DiagnosticAnalyzer-shim для AST правил' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 480 `
    -Labels @('phase-5','area-ide') `
    -Description 'Адаптер AstRule -> Roslyn DiagnosticAnalyzer (для лёгких правил, чтобы они работали в live-режиме редактора).' `
    -Acceptance 'В VS 2026 squiggle для V3007 появляется на лету.'

$null = New-Bead -Title 'VS 2026: out-of-process invoke для тяжёлых DFA-правил' -Type task -Priority 3 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-ide') `
    -Description 'Для DataFlow/Path-Sensitive/Taint правил - фоновый запуск CLI в side-car процессе, инкрементальный режим, чтение SARIF.' `
    -Acceptance 'Demo: при сохранении файла DFA-предупреждения обновляются < 5 c.'

$null = New-Bead -Title 'GitHub Action: openvulscan/action@v1 (composite)' -Type task -Priority 1 -Parent $EPIC5 -EstimateMinutes 240 `
    -Labels @('phase-5','area-ci') `
    -Description 'Composite action: setup-dotnet, restore, run openvulscan analyze --format sarif, upload-sarif в Code Scanning.' `
    -Acceptance 'На demo репозитории action отрабатывает и SARIF появляется в Security > Code scanning.'

$null = New-Bead -Title 'Azure DevOps task: publish SARIF в Scans' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 180 `
    -Labels @('phase-5','area-ci') `
    -Description 'Azure DevOps task.json + ts-скрипт, который оборачивает CLI.' `
    -Acceptance 'Pipeline на demo репозитории корректно публикует SARIF.'

$null = New-Bead -Title 'SonarQube exporter: Generic Issue Format (+ SARIF importer)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 240 `
    -Labels @('phase-5','area-cli') `
    -Description 'Команда ovs export --format sonar берёт SARIF и превращает в Sonar Generic Issue JSON.' `
    -Acceptance 'SonarQube Community 10+ корректно поглощает на demo проекте.'

$null = New-Bead -Title 'Docs: DocFX-сайт + структура (Reference, Rules, Guides, ADR)' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 360 `
    -Labels @('phase-5','area-docs') `
    -Description 'docs/site/: DocFX-проект, генерация HTML на CI. Reference авто из xml docs, rules - генерируется из RuleRegistry, ADR - markdown.' `
    -Acceptance 'На CI собирается статический сайт; деплой на gh-pages.'

$null = New-Bead -Title 'Docs: Quickstart + примеры на каждую категорию правил' -Type task -Priority 2 -Parent $EPIC5 -EstimateMinutes 240 `
    -Labels @('phase-5','area-docs') `
    -Description 'samples/: per-category папки с тестовыми проектами; в docs - пошаговое описание.' `
    -Acceptance 'Samples собираются в CI, ссылки из docs работают.'

Write-Host ''
Write-Host '=== Phase 6. Калибровка FP-rate и наращивание правил ==='

$null = New-Bead -Title 'Corpus: git submodule add dotnet/roslyn (pinned commit)' -Type task -Priority 1 -Parent $EPIC6 -EstimateMinutes 60 `
    -Labels @('phase-6','area-corpus') `
    -Description 'corpus/roslyn: submodule на конкретный SHA, который собирается на dotnet 10.' `
    -Acceptance 'git submodule update --init работает, openvulscan analyze corpus/roslyn запускается.'

$null = New-Bead -Title 'Corpus: остальные 9 эталонных проектов (aspnetcore, runtime, Avalonia, MonoGame, NodaTime, ImageSharp, akka.net, Dapper, efcore.pg)' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 180 `
    -Labels @('phase-6','area-corpus') `
    -Description 'Каждый - git submodule с pinned commit. README со списком + что зафризили.' `
    -Acceptance 'Все 10 submodules инициализируются, прогон openvulscan analyze не падает.'

$null = New-Bead -Title 'Triage tool: TP/FP/borderline UI (web или CLI prompt)' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 600 `
    -Labels @('phase-6','area-tools') `
    -Description 'tools/triage: интерфейс показывает diagnostic + сниппет кода, кнопки TP/FP/Borderline. Сохранение CSV.' `
    -Acceptance 'Тестовый прогон на 100 diagnostic-ах: разметка занимает < 30 мин.'

$null = New-Bead -Title 'Run: прогон на корпусе + baseline для всех 10 репозиториев' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 240 `
    -Labels @('phase-6','area-corpus') `
    -Description 'tools/corpus-runner: orchestrates прогон + сохранение baseline под corpus/*/.openvulscan.suppress + diff в CI.' `
    -Acceptance 'Зелёный CI corpus job. На PR diff лежит в artifacts.'

$null = New-Bead -Title 'Metrics: FP-rate отчёт по уровням (sample 100, точность +-10%)' -Type task -Priority 2 -Parent $EPIC6 -EstimateMinutes 240 `
    -Labels @('phase-6','area-tools') `
    -Description 'Скрипт: случайно отобрать 100 diagnostic-ов на корпусе, дать triage, вычислить FP-rate и confidence interval. Публиковать в docs/quality-report.md.' `
    -Acceptance 'Отчёт сгенерирован на тестовом запуске, цифры разумные.'

$null = New-Bead -Title 'Differential: vs Roslyn analyzers (intersect на пересекающихся правилах)' -Type task -Priority 3 -Parent $EPIC6 -EstimateMinutes 360 `
    -Labels @('phase-6','area-tools') `
    -Description 'tools/diff-roslyn: запустить наш + стандартные Roslyn warnings (CS, IDE) на corpus/*; вычислить overlap, only-us, only-them.' `
    -Acceptance 'Отчёт docs/diff-roslyn.md.'

$null = New-Bead -Title 'Differential: vs SonarAnalyzer.CSharp' -Type task -Priority 3 -Parent $EPIC6 -EstimateMinutes 360 `
    -Labels @('phase-6','area-tools') `
    -Description 'tools/diff-sonar: запустить SonarAnalyzer.CSharp на corpus/*; сопоставить с нашими правилами.' `
    -Acceptance 'Отчёт docs/diff-sonar.md.'

$null = New-Bead -Title 'Rules growth: наращивание реестра до ~200 правил (план поэтапной добавки)' -Type epic -Priority 3 -Parent $EPIC6 -EstimateMinutes 0 `
    -Labels @('phase-6','area-rules') `
    -Description 'Отдельный эпик с серией micro-task-ов: каждые 10-15 правил отдельная итерация с triage и FP-калибровкой. Сериализуется ANALYZER_PLAN.md §7.6.' `
    -Acceptance 'В реестре >=200 правил, FP-rate на корпусе соответствует целям из §9.'

Write-Host ''
Write-Host '✓ Готово.'
