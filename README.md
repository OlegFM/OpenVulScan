# OpenVulScan

Open-source статический анализатор C# с целевым паритетом с PVS-Studio.

## Цель

Создать современный SAST-инструмент для C# (.NET 10+), который находит реальные дефекты безопасности и надёжности кода: null-dereference, dead code, taint-уязвимости (SQLi, XSS, path traversal) и др.

Целевой функциональный паритет: **~70–80 % диагностик PVS-Studio** (275 правил для C#) с сопоставимым уровнем ложных срабатываний.

## Быстрый старт

```bash
# Сборка
dotnet build

# Запуск тестов
dotnet test

# Анализ solution (в будущем)
dotnet run --project src/OpenVulScan.Cli -- analyze MyApp.sln
```

## Дорожная карта

| Фаза | Срок | Ключевой артефакт |
|---|---|---|
| **0. R&D** | 1–2 мес | ADR-001 + spikes (Roslyn CFG, MSBuildWorkspace, lattice) |
| **1. MVP** | 3–4 мес | CLI с 30 правилами, SARIF, baseline |
| **2. Intra DFA** | 4–6 мес | +40 правил, path-sensitive анализ |
| **3. Inter DFA** | 6–9 мес | +30–50 правил, межпроцедурный анализ, инкрементальный режим |
| **4. Taint** | 4–6 мес | OWASP-правила (SQLi, XSS, SSRF и др.) |
| **5. IDE/CI** | 3–6 мес | Rider 2026+ plugin, VS 2026 extension, MSBuild target, GitHub Action |
| **6. Калибровка** | 6+ мес | FP-rate ≤ 30 % (Level-1), ~200 правил |

## Структура репозитория

```
openvulscan/
├── src/                          # Исходный код
│   ├── OpenVulScan.Cli/          # dotnet tool entry point
│   ├── OpenVulScan.Core/         # IR, lattice, CFG, summaries
│   ├── OpenVulScan.Frontend/     # Roslyn workspace loader
│   ├── OpenVulScan.RuleEngine/   # Rule registry, scheduler
│   ├── OpenVulScan.Rules.Ast/    # AST-правила
│   ├── OpenVulScan.Rules.DataFlow/   # Data-flow правила
│   ├── OpenVulScan.Rules.PathSensitive/  # Path-sensitive правила
│   ├── OpenVulScan.Rules.Taint/  # OWASP / taint правила
│   ├── OpenVulScan.Rules.Performance/    # Unity/perf правила
│   ├── OpenVulScan.Sarif/        # SARIF/JSON/HTML emitters
│   ├── OpenVulScan.Cache/        # Инкрементальный кеш
│   └── OpenVulScan.Configuration/    # YAML/JSON конфигурация
├── tests/                        # Тесты
│   ├── OpenVulScan.Core.Tests/
│   ├── OpenVulScan.Rules.Tests/  # Snapshot-тесты правил
│   ├── OpenVulScan.Integration.Tests/
│   └── OpenVulScan.CorpusBench/  # Бенчмарки на корпусе
├── corpus/                       # Эталонные open-source проекты (git submodules)
├── docs/                         # Документация (DocFX)
├── samples/                      # Примеры написания правил
├── tools/                        # Вспомогательные утилиты
│   ├── update-cwe-mapping/
│   ├── corpus-runner/
│   └── rule-coverage-report/
└── .github/workflows/            # CI/CD
```

## Технологический стек

- **Runtime**: .NET 10 (LTS)
- **Frontend**: Roslyn 4.14+ (`Microsoft.CodeAnalysis.CSharp`)
- **CLI**: `System.CommandLine`
- **Тесты**: xUnit + Verify (snapshot)
- **CI**: GitHub Actions (matrix: Windows / Linux / macOS)

## Spikes

### V3001 – Identical Sub-expressions

Реализация в `spikes/Rule3001/`. Ходит по `IOperation`, на бинарных операторах сравнивает левое и правое поддерево по структурному равенству (`OperationKind` рекурсивно, затем значения/символы операндов).

**Проверяемые операторы:** `==`, `!=`, `+`, `-`, `*`, `/`, `%`, `&&`, `||`, `&`, `|`, `^`

**Исключены:** `=` (присваивание)

**Результаты:**
- 5 позитивных кейсов — все детектируются
- 3 негативных кейса — 0 ложных срабатываний

## CI/CD

Проект использует GitHub Actions с матричной сборкой (Ubuntu / Windows / macOS) на .NET 10.

### Настройка защиты ветки `main`

Чтобы блокировать слияние PR до успешного прохождения CI:

1. Откройте **Settings → Branches** в репозитории GitHub.
2. Нажмите **Add branch protection rule**.
3. В поле **Branch name pattern** введите `main`.
4. Включите опции:
   - **Require a pull request before merging**
   - **Require status checks to pass before merging**
   - В блоке статус-чеков отметьте:
     - `Build & Test (ubuntu-latest)`
     - `Build & Test (windows-latest)`
     - `Build & Test (macos-latest)`
5. Сохраните правило (**Create**).

Теперь PR в `main` будет заблокирован до тех пор, пока все задания workflow не завершатся успешно.

## Лицензия

- Код: [Apache 2.0](LICENSE)
- Реестр правил и API-модели: [CC-BY-4.0](LICENSE-RULES)
