# ExecPlan: Computer Use for Windows deferred product work

> **Для agentic workers:** обязательные sub-skills при исполнении: использовать `superpowers:test-driven-development` для поведенческих и кодовых изменений, `superpowers:requesting-code-review` перед каждым commit, `superpowers:receiving-code-review` для каждого review response и `superpowers:systematic-debugging` для каждого подтверждённого дефекта или failed check. План исполняет один implementer-agent, который одновременно оркестрирует review-субагентов. Шаги используют checkbox-синтаксис (`- [ ]`) для durable progress tracking.

**Цель:** выполнить deferred `computer-use-win` work как последовательную серию маленьких, проверяемых commit-пакетов без расширения public surface случайными путями и без сохранения legacy-компромиссов ради инерции.

**Архитектура:** целевая форма — explicit application boundary поверх `WinBridge.Server` transport layer: tool handlers bind requests and call application use cases; policy/result/state semantics живут в явных owner-слоях; runtime capability slices остаются transport-agnostic. DDD применяется только там, где есть настоящие доменные инварианты продукта: discovery identity, action readiness, result semantics, runtime state transitions, MCP publication contract. TDD применяется для поведения, boundary contracts, failure paths, publication matrix и migration deltas; чисто документальные решения фиксируются review/checklist gates без production-code TDD.

**Стек:** C#/.NET 8, MCP C# SDK, xUnit integration/unit tests, PowerShell harness scripts, generated contract docs, local `STDIO` MCP runtime, `ComputerUseWin` public plugin surface.

---

## Контекст

Ветка `codex/computer-use-win` закрыла hardening public `computer-use-win` surface: request boundary, action lifecycle, failure taxonomy, activation cause, install/publish recovery и runtime bundle integrity. В ходе review loop часть замечаний была подтверждена как bugfix текущей ветки и уже закрыта, но несколько тем были сознательно вынесены за пределы ветки.

Этот документ фиксирует deferred work, чтобы следующие волны не возвращались к тем же finding-ам как к локальным bugfix-ам. Здесь описаны темы, которые требуют отдельного product/API design или новой execution model, а не точечного hardening текущего shipped contract.

## Цель

Подготовить следующий пакет работ после текущей ветки:

1. сделать public discovery согласованным с window-level execution;
2. при необходимости разделить pure observation и action-session preparation для `get_app_state`;
3. отдельно пересмотреть policy для advisory/provider failures, если команда решит изменить уже принятый truthful failure invariant.

## Границы

- Входит: описание найденных deferred классов, rationale, desired target model, task decomposition, acceptance criteria и verification contour.
- Не входит: изменение runtime кода в текущей ветке.
- Не входит: срочный bugfix уже подтверждённых contract defects; они закрыты отдельными commits в текущей ветке.
- Не входит: автоматическая миграция клиентов на новый discovery surface.

## Как исполнять этот план

Этот документ является единственным рабочим состоянием для implementer-а при выполнении deferred ветки. Перед переходом к следующему этапу агент обязан отметить чекбоксы текущего этапа, заполнить отчёт этапа и сделать отдельный commit после review/re-review цикла.

### Глобальные правила выполнения

- [ ] Работать строго последовательно: `Stage 0` -> `Stage 1` -> `Stage 2` -> `Stage 3` -> `Stage 4` -> optional `Stage 5/6` -> `Stage 7`.
- [ ] Не начинать реализацию следующего stage, пока текущий stage не имеет: green verification, review approval, re-review approval после исправлений, commit SHA и заполненный stage report.
- [ ] Не совмещать product redesign и protocol migration в одном commit-пакете.
- [ ] Не добавлять новый public tool, пока stage прямо не требует этого и acceptance не фиксирует publication matrix.
- [ ] Не сохранять legacy path ради совместимости, если plan stage явно переводит систему на новую модель; удалять старый путь в том же commit-пакете, где появляется replacement и tests.
- [ ] Перед каждым stage перечитать current source of truth: этот файл, `AGENTS.md`, `docs/architecture/computer-use-win-surface.md`, `src/WinBridge.Runtime.Tooling/ToolNames.cs`, `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`, релевантные tests и generated docs.
- [ ] После каждого stage обновить этот файл: отметить выполненные пункты, заполнить `Отчёт этапа`, записать commit SHA и оставшиеся follow-up только если они не входят в acceptance текущего stage.

### DDD и TDD policy

DDD использовать только для owner-моделей, которые удерживают продуктовые инварианты:

- discovery identity: `appId` как approval/policy key и concrete window identity как execution target;
- action readiness: state token, approval, activation, observation freshness;
- result semantics: `ok`, `failed`, `blocked`, `approval_required`, `verify_needed`, `unsupported`;
- runtime state transitions: `attached`, `observed`, `approved`, `stale`, `blocked`;
- MCP publication contract: profile, registration, manifest, generated docs, install surface.

DDD не применять к:

- простым DTO shape-правкам без поведения;
- PowerShell harness plumbing;
- composition root cleanup, если достаточно явной DI-регистрации;
- generated docs refresh.

TDD обязателен там, где меняется поведение:

- publication/profile visibility;
- request binding and validation;
- result/failure materialization;
- protocol baseline and MCP handshake expectations;
- discovery ambiguity and multi-window selection;
- state transition and token admissibility;
- audit/evidence redaction and warning semantics.

TDD не нужен для чистого prose-doc update, но такой update должен иметь review checklist и статическую проверку ссылок/упоминаний.

### Обязательный TDD cycle для выбранных задач

- [ ] `RED`: написать один минимальный failing test на конкретный behavior или contract.
- [ ] `RED proof`: запустить только этот targeted test и убедиться, что он падает по ожидаемой причине.
- [ ] `GREEN`: реализовать минимальный production change, который закрывает root cause.
- [ ] `GREEN proof`: запустить targeted test и ближайший affected test slice.
- [ ] `REFACTOR`: убрать дубли/случайные abstractions только после green.
- [ ] `FINAL GREEN`: повторить targeted + stage verification перед review.

### Review gate перед каждым commit

Перед каждым commit implementer запускает минимум двух review-субагентов через `spawn_agent` с `model: "gpt-5.5"`. Первый review-фокус: architecture/contract. Второй review-фокус: tests/failure paths/docs/generated surface. В prompt каждому агенту нужно передать stage scope, changed files, base/head diff context and acceptance criteria.

Шаблон prompt для review-субагента:

```md
Текст должен быть на русском языке.

Ты review-субагент для stage `<STAGE_ID>` плана `docs/exec-plans/active/computer-use-win-deferred-work.md`.

Scope этапа:
- `<STAGE_SCOPE>`

Acceptance criteria:
- `<ACCEPTANCE_CRITERIA>`

Изменённые файлы:
- `<CHANGED_FILES>`

Проверь только изменения текущего stage и соседние failure-path'ы того же класса. Не предлагай расширение product scope за пределы stage.

После каждого пункта добавь отдельный раздел с обоснованием:
— каким образом была выполнена проверка;
— почему это классифицировано как баг;
— является ли это корневой причиной или лишь симптомом.

Если это только симптом, определи и укажи предполагаемую корневую причину проблемы.

Также добавь рекомендации по корректному исправлению без временных или архитектурно слабых решений. При необходимости укажи, где оправдан точечный рефакторинг, если текущая реализация выглядит архитектурно неконсистентной.

Дополнительно, если проблема связана с API, перепроверь официальные источники и внутренние файлы окружения/системы/контура/библиотек, чтобы подкрепить замечание и ссылаться в рекомендациях на источники.

Для каждого замечания обязательно укажи:
— это локальный единичный дефект или представитель более широкого класса дефектов;
— какие соседние пути, ветки или failure-path'ы относятся к тому же классу и должны быть перепроверены implementer-агентом;
— закрывает ли предлагаемое исправление только данный симптом или весь класс проблем.

Если несколько замечаний принадлежат одному классу проблем, явно сформулируй:
— общую модель или инвариант, который должен быть восстановлен;
— какой минимальный архитектурный сдвиг или рефакторинг закрывает весь класс проблем;
— какие следующие локальные замечания перестанут появляться, если implementer исправит именно этот корень.

Формат результата:
1. Verdict: `approve` / `approve_with_minor_notes` / `changes_requested`.
2. Findings by severity.
3. Проверенные соседние paths.
4. Остаточные риски.
5. Рекомендация: можно ли commit-ить stage.
```

### Обработка review feedback

- [ ] Прочитать все feedback items полностью.
- [ ] Для каждого item записать в stage report статус: `confirmed`, `rejected`, `needs_more_evidence`.
- [ ] Для каждого `confirmed` item применить `systematic-debugging`: root cause investigation -> pattern analysis -> hypothesis -> TDD fix -> verification.
- [ ] Не чинить внешний симптом, если item представляет класс дефектов, который можно закрыть общим инвариантом без несоразмерного усложнения.
- [ ] Для каждого `rejected` item записать доказательство: какие файлы/тесты/официальные docs подтверждают отклонение.
- [ ] После исправлений отправить тем же review-субагентам re-review с тем же scope и списком изменений.
- [ ] Повторять fix/re-review до `approve` или `approve_with_minor_notes` без blocking issues.

### Commit gate

Commit допускается только после:

- [ ] stage acceptance criteria выполнены;
- [ ] stage verification commands выполнены и результат записан;
- [ ] review-субагенты вернули approval для текущего stage;
- [ ] все confirmed findings либо исправлены, либо явно вынесены из scope с обоснованием;
- [ ] `docs/CHANGELOG.md` и generated docs обновлены, если stage менял контракт, docs, generated surface или operating model;
- [ ] этот файл обновлён stage report-ом и commit checklist.

Рекомендуемый commit format:

```text
<type>: <stage-id> <short outcome>
```

Примеры:

```text
test: c0 freeze computer-use-win public surface
refactor: a1 extract computer-use-win use cases
feat: m1 upgrade MCP protocol baseline
feat: d1 expose instance-addressable discovery
```

### Шаблон отчёта этапа

Каждый stage ниже содержит собственный report block. Формат заполнения:

```md
#### Отчёт этапа

- Статус этапа: `not_started` / `in_progress` / `blocked` / `ready_for_review` / `approved` / `committed`
- Branch:
- Commit SHA:
- TDD применялся:
- Проверки:
- Review agents:
- Подтверждённые замечания:
- Отклонённые замечания:
- Исправленные root causes:
- Проверенные соседние paths:
- Остаточные риски:
- Разблокировка следующего этапа:
```

## Линейный план реализации

### Stage 0: Source-of-truth baseline

**Назначение:** подготовить рабочий снимок фактов, чтобы implementer не начал с устаревшего плана.

**Зависит от:** текущий файл и актуальное состояние worktree.

**Файлы для чтения:**

- `docs/exec-plans/active/computer-use-win-deferred-work.md`
- `AGENTS.md`
- `docs/architecture/computer-use-win-surface.md`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Runtime.Tooling/ToolNames.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
- `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`

**Шаги:**

- [x] Read the files above and update this stage report with any drift from the plan.
- [x] Confirm current public `computer-use-win` surface is still exactly `list_apps`, `get_app_state`, `click`.
- [x] Confirm latent action wave still exists or record if already removed: `type_text`, `press_key`, `scroll`, `drag`.
- [x] Confirm current negotiated/exported MCP baseline is still `2025-06-18`.
- [x] Confirm `Program.cs` still uses late-bound `hostServices` closure pattern for tool registration.
- [x] Confirm generated interface docs still exist and identify which refresh command owns them.
- [x] Decide whether any stage below is already obsolete because code changed; if yes, update this plan before implementation.

**TDD:** не применяется. Это этап discovery и обновления planning state.

**Проверка:** только статический поиск/чтение. Рекомендуемые команды:

```powershell
rg -n "list_apps|get_app_state|click|type_text|press_key|scroll|drag" src/WinBridge.Server/ComputerUse tests/WinBridge.Server.IntegrationTests
rg -n "2025-06-18|2025-11-25|protocolVersion" src scripts tests docs/generated
rg -n "hostServices|CreateComputerUseWinTools|AddMcpServer" src/WinBridge.Server/Program.cs src/WinBridge.Server/ComputerUse
```

**Commit:** отдельный snapshot commit обязателен, потому что пользовательский workflow требует немедленно фиксировать planning state внутри этого файла.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `35777c6`
- TDD применялся: `нет; discovery-only stage`
- Проверки:
  - `git branch --show-current` -> `codex/computer-use-win-deferred-work-implementation`
  - `git status --short` -> `M docs/exec-plans/active/computer-use-win-deferred-work.md` (ожидаемый snapshot diff текущего stage; unrelated tracked changes absent)
  - статическое чтение и поиск по stage-listed source/tests/docs подтвердили: public surface = `list_apps`, `get_app_state`, `click`
  - статическое чтение и поиск подтвердили latent action wave: `type_text`, `press_key`, `scroll`, `drag` по-прежнему существуют в code + manifest
  - статическое чтение [ToolContractExporter.cs](src/WinBridge.Runtime.Tooling/ToolContractExporter.cs) и integration tests подтвердило negotiated/exported MCP baseline `2025-06-18`
  - статическое чтение [Program.cs](src/WinBridge.Server/Program.cs) подтвердило late-bound `hostServices` closure pattern
  - generated interface docs присутствуют; canonical refresh path: `scripts/refresh-generated-docs.ps1`
  - `scripts/codex/bootstrap.ps1` -> green
  - `scripts/codex/verify.ps1` -> green (`WinBridge.Runtime.Tests` 636/636, `WinBridge.Server.IntegrationTests` 238/238, smoke ok, generated docs refresh ok)
- Review agents:
  - `Huygens (architecture/contract)` -> initial `changes_requested`, re-review `approve_with_minor_notes`
  - `Epicurus (tests/failure/docs/generated)` -> initial `changes_requested`, re-review `approve_with_minor_notes`
- Подтверждённые замечания:
  - `[confirmed]` pre-commit evidence было завышено: строка `git status --short -> clean worktree` противоречила snapshot diff
  - `[confirmed]` использован status вне собственного lifecycle contract: `review_pending` вместо допустимого enum
  - `[confirmed]` generated docs surface был отмечен слишком общо и не перечислял конкретные checked artifacts
- Отклонённые замечания: `нет`
- Исправленные root causes:
  - stage report теперь различает pre-commit snapshot state и post-commit clean state
  - stage lifecycle wording приведён к допустимому status contract этого exec-plan
  - inventory checked paths разделён с общим фактом существования generated docs
- Проверенные соседние paths:
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
  - `src/WinBridge.Runtime.Tooling/ToolNames.cs`
  - `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
  - `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`
  - `docs/generated/project-interfaces.md`
  - `docs/generated/project-interfaces.json`
  - `docs/generated/computer-use-win-interfaces.md`
  - `docs/generated/computer-use-win-interfaces.json`
  - `scripts/refresh-generated-docs.ps1`
  - `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`
- Остаточные риски:
  - Stage 0 подтвердил baseline, но не меняет code invariants; accidental publication и protocol drift остаются задачами `Stage 1-3`
- Разблокировка следующего этапа:
  - `Stage 0` закрыт snapshot commit `35777c6`
  - `Stage 1` разблокирован

### Stage 1: C0 public surface freeze and initial A3 guard rails

**Назначение:** сделать случайную публикацию latent action wave технически невозможной или test-visible до любого product redesign.

**Зависит от:** Stage 0 completed.

**Файлы:**

- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- Modify: `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
- Test: `tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- Docs if behavior/contract wording changes: `docs/architecture/computer-use-win-surface.md`, generated interface docs, `docs/CHANGELOG.md`

**Целевая модель:**

- Public implemented surface remains exactly three tools: `list_apps`, `get_app_state`, `click`.
- `type_text`, `press_key`, `scroll`, `drag` are either removed from callable/public registration construction or moved behind an explicit internal draft boundary with tests proving they are not published.
- Manifest, registration and public profile cannot disagree silently.

**TDD-решение:** обязательно. Этап защищает public contract и publication invariants.

**Шаги:**

- [x] Написать failing test: `computer_use_win_profile_publishes_only_three_implemented_tools`.
- [x] Запустить targeted test и записать RED proof.
- [x] Написать failing test: latent action registration methods cannot appear in public `computer-use-win` profile.
- [x] Запустить targeted test и записать RED proof.
- [x] Implement minimal registration/manifest change to make accidental publication impossible.
- [x] Remove or isolate latent action callable methods only if tests show current form can be published accidentally.
- [x] Запустить targeted tests и affected manifest/profile tests.
- [x] Refactor only after green: name the owner boundary clearly, without introducing compatibility aliases.
- [x] Update generated docs if tool descriptors/generated exports change, and update `docs/CHANGELOG.md` for architecture/checks/publication-guardrail deltas.
- [ ] Запустить review gate с двумя `gpt-5.5` subagents.
- [ ] Обработать review через systematic-debugging и re-review до approval.
- [ ] Сделать отдельный commit для этого stage.

**Команды проверки:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"
dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "ToolContractManifest"
```

**Ожидаемый результат:** tests доказывают, что для `computer-use-win` implemented/published только три намеренных public tools.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `2e7dc4b`
- TDD применялся: `да`
- Проверки:
  - RED proof: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinArchitectureTests.ComputerUseWinProfilePublishesOnlyThreeImplementedTools|ComputerUseWinArchitectureTests.ComputerUseWinToolsExposeOnlyCuratedOperatorEntryPoints"` -> fail, registration всё ещё держал `CreateDragTool/CreatePressKeyTool/CreateScrollTool/CreateTypeTextTool`, а `ComputerUseWinTools` всё ещё публиковал `Drag/PressKey/Scroll/TypeText`
  - GREEN proof: тот же targeted command -> green, `2/2`
  - affected runtime contour: `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "ToolContractManifestTests"` -> green, `20/20`
  - affected integration/profile contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinArchitectureTests|McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> green, `44/44`
  - exploratory broad filter: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin|McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> hit unrelated install-surface failure in `ComputerUseWinInstallSurfaceTests.PublishComputerUseWinPluginCreatesSelfContainedRuntimeBundle` (`JsonDocument.Parse` on empty stdout); not caused by Stage 1 diff and not required by stage acceptance
- Review agents:
  - `Lovelace (architecture/contract)` -> initial `approve`, re-review `approve_with_minor_notes`
  - `Mencius (tests/failure/docs/generated)` -> initial `changes_requested`, re-review `approve`
- Подтверждённые замечания:
  - `[confirmed]` `docs/CHANGELOG.md` обязателен даже без generated export drift, потому что stage меняет architecture/checks/publication guardrail
- Отклонённые замечания: `нет`
- Исправленные root causes:
  - latent deferred action wave больше не живёт в public server boundary: из `ComputerUseWinToolRegistration` удалены top-level MCP builders/schema hooks для deferred tools
  - `ComputerUseWinTools` больше не держит latent callable entrypoints для `type_text`, `press_key`, `scroll`, `drag`
- Проверенные соседние paths:
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
  - `tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs`
- Остаточные риски:
  - full `ComputerUseWin` broad integration filter сейчас не годится как stage gate из-за соседнего install-surface failure path; для Stage 1 использован более узкий accepted contour по registration/profile boundary
- Разблокировка следующего этапа:
  - generated exports и tool descriptors не менялись, поэтому generated docs refresh для Stage 1 не требуется
  - `docs/CHANGELOG.md` обновлён из-за architecture/checks/publication guardrail delta
  - `Stage 1` закрыт commit `2e7dc4b`
  - `Stage 2` разблокирован

### Stage 2: A1 tool-layer decompression and CR1 composition root stabilization

**Назначение:** вывести `computer-use-win` orchestration из transport-facing handler shape до того, как discovery/protocol redesign увеличит сложность.

**Зависит от:** Stage 1 committed.

**Файлы:**

- Modify/Create under: `src/WinBridge.Server/ComputerUse/`
- Modify: `src/WinBridge.Server/Program.cs`
- Modify: `src/WinBridge.Runtime/ServiceCollectionExtensions.cs` only if DI ownership moves there naturally.
- Test: `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`

**Целевая модель:**

- `ComputerUseWinTools` binds MCP request and delegates.
- Application owners hold use-case logic: discovery, app-state observation, click orchestration, result finalization.
- `Program.cs` registers `computer-use-win` tools without fragile late-bound `hostServices` closure pattern for core surface.
- Transport layer does not become a service locator.

**DDD-решение:** использовать лёгкий application/domain language для use-case owners и policy evaluators. Не создавать aggregates/entities только ради паттерна.

**TDD-решение:** обязательно для каждого extracted behavior. Refactor-only extraction может сначала использовать characterization tests, если поведение уже покрыто.

**Шаги:**

- [x] Identify the smallest first extraction target: discovery materialization or get-app-state finalization.
- [x] Write characterization test that locks current public payload/result shape for that target.
- [x] Запустить targeted test и записать RED, если нового инварианта ещё нет, или characterization GREEN, если поведение уже покрыто.
- [x] Extract one owner class with a single responsibility.
- [x] Replace handler logic with delegation and keep public result unchanged.
- [x] Repeat for app-state observation orchestration.
- [x] Repeat for click orchestration only after observation path is stable.
- [x] Write/adjust DI resolution test proving all new owner classes resolve without service locator usage.
- [x] Stabilize `Program.cs` registration after first extraction: registration should compose typed owners, not late-bound closures.
- [x] Run affected integration tests.
- [ ] Запустить review gate и re-review loop.
- [ ] Сделать отдельный commit для этого stage.

**Команды проверки:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"
dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "ServiceCollection"
```

**Ожидаемый результат:** public behavior остаётся стабильным, `ComputerUseWinTools` становится thin adapter, а `Program.cs` больше не владеет core orchestration wiring через late-bound closures.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `b31a9a1`
- TDD применялся: `characterization-first + targeted green`
- Проверки:
  - characterization GREEN: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinActionAndProjectionTests.ListAppsGroupsVisibleWindowsByStableAppIdAndPrefersForegroundRepresentative"` -> green, `1/1`
  - architecture + profile contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinArchitectureTests|ComputerUseWinActionAndProjectionTests.ListAppsGroupsVisibleWindowsByStableAppIdAndPrefersForegroundRepresentative|McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|McpProtocolSmokeTests.ComputerUseWinGetAppStateRequiresApprovalBeforeReturningState|ComputerUseWinObservationTests"` -> green, `54/54`
  - architecture lock only: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinArchitectureTests"` -> green, `45/45`
- Review agents:
  - `Dewey (architecture/contract)` -> `approve_with_minor_notes` (`54/54` evidence drift fixed before commit)
  - `McClintock (tests/failure/docs/generated)` -> `approve`
- Подтверждённые замечания:
  - `[confirmed]` stage report должен фиксировать exact contour count `54/54`, а не устаревшее `53/53`
- Отклонённые замечания: `нет`
- Исправленные root causes:
  - `ComputerUseWinTools` перестал совмещать discovery materialization, get-app-state orchestration, click orchestration и payload/result helpers в одном transport-facing type
  - discovery materialization вынесена в `ComputerUseWinAppDiscoveryService`, а transport list path — в `ComputerUseWinListAppsHandler`
  - get-app-state orchestration вынесена в `ComputerUseWinGetAppStateHandler`
  - click orchestration boundary вынесена в `ComputerUseWinClickHandler` + `ComputerUseWinStoredStateResolver`
  - shared payload/result materialization вынесена в `ComputerUseWinToolResultFactory`
  - `Program.cs` больше не держит per-tool `computer-use-win` registration closures; core surface публикуется через typed `ComputerUseWinRegisteredTools` wrapper с post-build bind
- Проверенные соседние paths:
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinAppDiscoveryService.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinListAppsHandler.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinClickHandler.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinStoredStateResolver.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinRegisteredTools.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
  - `src/WinBridge.Server/Program.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
  - `docs/CHANGELOG.md`
- Остаточные риски:
  - windows-engine side (`launch/open_target/windows.input`) по-прежнему живёт на старом `hostServices` closure pattern; Stage 2 intentionally stabilizes only `computer-use-win` core surface
- Разблокировка следующего этапа:
  - прогнать review gate для Stage 2 extraction package
  - `Stage 2` закрыт commit `b31a9a1`
  - `Stage 3` разблокирован

### Stage 3: M0-M5 MCP 2025-11-25 protocol migration

**Назначение:** выровнять runtime/export/tests/generated contract с latest MCP `2025-11-25` до расширения product surface.

**Зависит от:** Stage 1 committed; Stage 2 preferred.

**Файлы:**

- Modify: `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`
- Modify: `scripts/smoke.ps1`
- Modify: `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- Modify: `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`
- Modify: `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- Modify generated exports: `docs/generated/project-interfaces.md`, `docs/generated/project-interfaces.json`, `docs/generated/computer-use-win-interfaces.md`, `docs/generated/computer-use-win-interfaces.json`
- Docs: `docs/architecture/computer-use-win-surface.md`, `docs/architecture/observability.md`, `docs/CHANGELOG.md`

**Целевая модель:**

- Negotiated/exported protocol baseline becomes `2025-11-25`.
- `stderr` remains valid normal logging path for `STDIO`.
- `Implementation` optional metadata and `tools/list` optional metadata are either implemented deliberately or explicitly absent.
- Tool schemas are audited against JSON Schema `2020-12` default assumptions.
- Input validation errors materialize as tool execution failures where latest spec requires that.
- HTTP/auth/task changes are documented as out-of-scope for current `STDIO` runtime unless product direction changes.

**DDD-решение:** DDD не полезен для самого protocol version bump. Использовать contract-owner model вокруг MCP publication и error semantics.

**TDD-решение:** обязательно. Этап меняет wire contract expectations.

**M0 inventory (зафиксировано до кода):**

- In scope:
  - `STDIO initialize` negotiated/exported baseline -> `2025-11-25`
  - exporter transport baseline и generated interface exports
  - smoke/integration handshake expectations
  - deliberate decision по optional `serverInfo` / `tools/list` metadata для текущего `STDIO` продукта
  - audit manual schema assumptions against JSON Schema `2020-12`
  - tool-level request validation failures там, где validation уже владеет shape semantics
- Out of scope:
  - HTTP transport, auth, streamable HTTP rollout
  - prompts/resources/completions expansion
  - icons asset pipeline и `execution.taskSupport` rollout без отдельной product need
  - любые product redesign changes для discovery/observe split/advisory policy
- Deferred:
  - draft-only MCP deltas после current stable `2025-11-25`
  - broader install-surface hardening, не требуемый самим protocol bump

**Шаги:**

- [x] `M0`: write a migration inventory in this file or a dedicated architecture doc section: in-scope, out-of-scope, deferred.
- [x] Add failing tests expecting `protocolVersion = "2025-11-25"` in integration handshake and exporter output.
- [x] Запустить targeted tests и записать RED proof.
- [x] Change exporter, smoke and integration handshake expectations to `2025-11-25`.
- [x] Запустить targeted tests и записать GREEN proof.
- [x] Add tests around `serverInfo`/`Implementation` optional metadata decision.
- [x] Add tests around `tools/list` metadata decision: `icons`, `execution.taskSupport`, naming guidance.
- [x] Audit manual schemas and add tests for `$schema` presence or documented absence under JSON Schema `2020-12` default.
- [x] Add/adjust tests proving malformed tool arguments become tool-level failures where owned by request validation.
- [x] Update generated exports and docs.
- [x] Запустить review gate с official MCP source references в review prompt.
- [x] Обработать review через systematic-debugging и re-review до approval.
- [x] Сделать отдельный commit для этого stage.

**Команды проверки:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests|ComputerUseWinInstallSurfaceTests"
dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "ToolContractExporter|ToolContractManifest"
scripts/smoke.ps1
scripts/refresh-generated-docs.ps1
```

**Ожидаемый результат:** code, tests, smoke harness и generated docs согласованы на MCP `2025-11-25`, а latest-spec deltas, релевантные local `STDIO`, либо реализованы, либо явно out-of-scope.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `623390d`
- TDD применялся: `да`
- Проверки:
  - `M0` inventory зафиксирован в этой секции; live MCP stable подтверждён по official docs как `2025-11-25`
  - RED proof: `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "ToolContractExporterTests.ExporterPublishesCurrentMcp20251125TransportBaseline"` -> fail (`expected 2025-11-25`, `actual 2025-06-18`)
  - handshake characterization: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests.InitializeNegotiatesMcp20251125ProtocolVersion"` -> green already before code; live MCP layer negotiated `2025-11-25` through current library/runtime
  - protocol metadata contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests.InitializeNegotiatesMcp20251125ProtocolVersion|McpProtocolSmokeTests.InitializePublishesMinimalServerInfoWithoutDescription|McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|ComputerUseWinArchitectureTests.ComputerUseWinManualSchemasRelyOnJsonSchema202012DefaultWithoutExplicitSchemaKeyword"` -> green, `4/4`
  - invalid-request contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests.WindowsInputCallMaterializesMalformedActionElementAsToolLevelFailedResult|McpProtocolSmokeTests.ComputerUseWinGetAppStateDoesNotAttachWindowWhenRequestIsInvalid"` -> green, `2/2`
  - install-surface protocol contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints"` -> green, `1/1`
  - runtime/export contour after refresh: `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "ToolContractExporterTests|ToolContractManifestTests"` -> green, `30/30`
  - `scripts/smoke.ps1` -> green; smoke summary reports `protocol: 2025-11-25`
  - `scripts/refresh-generated-docs.ps1` -> green
- Review agents:
  - `019dc0cc-a350-71e1-acc7-41cc9d1f4312` (`architecture/contract`) -> `approve` after re-review
  - `019dc0cc-a4fd-7292-8ec7-28234b3083b6` (`tests/failure/docs/generated`) -> `approve`
- Подтверждённые замечания:
  - `[confirmed]` stage drift: live MCP handshake уже был на `2025-11-25`, поэтому real migration scope сузился до exporter/tests/scripts/generated docs
  - `[confirmed]` exec-plan sequencing drift: нижние `migration track` / `Current recommendation` были обновлены раньше, чем `Iteration 2` и `Suggested branch split`, из-за чего active roadmap ещё планировал уже закрытую protocol migration как future work
- Отклонённые замечания: `нет`
- Исправленные root causes:
  - exporter baseline в `ToolContractExporter` больше не расходится с live MCP stable revision
  - stdio/request literals в smoke и stage-owned integration tests синхронизированы на `2025-11-25`
  - protocol decisions зафиксированы тестами: `serverInfo` остаётся минимальным, `icons` не публикуются, `execution.taskSupport=optional` materialize-ится у `get_app_state` и `click`, manual `computer-use-win` schemas продолжают использовать default JSON Schema `2020-12` без `$schema`
  - generated interface exports и `stack-research` narrative выровнены на новый stable baseline
  - весь `Stage 3` sequencing narrative в active ExecPlan теперь описывает migration как уже закрытый инженерный слой и больше не планирует отдельную future MCP branch перед `Stage 4`
- Проверенные соседние paths:
  - `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`
  - `tests/WinBridge.Runtime.Tests/ToolContractExporterTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
  - `scripts/smoke.ps1`
  - `docs/generated/project-interfaces.md`
  - `docs/generated/project-interfaces.json`
  - `docs/generated/computer-use-win-interfaces.md`
  - `docs/generated/computer-use-win-interfaces.json`
  - `docs/generated/stack-research.md`
  - `docs/CHANGELOG.md`
  - `docs/exec-plans/active/computer-use-win-deferred-work.md`
- Остаточные риски:
  - broader MCP feature set beyond current `STDIO` product (`HTTP`, auth, prompts/resources/completions expansion, icons asset pipeline) остаётся out-of-scope и сознательно не реализуется этим stage
- Разблокировка следующего этапа:
  - `Stage 3` закрыт commit `623390d`
  - `Stage 4` разблокирован

### Stage 4: Deferred class 1 instance-addressable discovery with A2/S1/C1

**Назначение:** привести public discovery к window-level execution, сохранив app-level approval/policy.

**Зависит от:** Stage 1 committed, Stage 2 committed, Stage 3 committed.

**Файлы:**

- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- Modify/Create under: `src/WinBridge.Server/ComputerUse/`
- Modify: `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- Modify: `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`
- Test: `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- Docs/generated: `docs/architecture/computer-use-win-surface.md`, `docs/generated/computer-use-win-interfaces.md`, `docs/generated/computer-use-win-interfaces.json`, `docs/CHANGELOG.md`

**Целевая модель:**

- Public discovery exposes every selectable visible window instance.
- `appId` remains app-level approval and policy key.
- Concrete public selector identifies the window instance without making raw `HWND` the only semantic selector.
- `get_app_state` can target a discovered instance without foreground guessing.
- Result semantics come from a canonical owner, not per-handler wording.
- Publication + install matrix remains consistent.

**DDD-решение:** оправдано. Identity boundary нужно смоделировать явно: `AppIdentity`, `WindowInstanceIdentity`, `ApprovalKey`, `ExecutionTarget`. Держать это lightweight records/value objects, если они снимают ambiguity.

**TDD-решение:** обязательно. Это product behavior и public contract redesign.

**DTO decision (зафиксировано до кода):**

- Выбрана grouped model: top-level `apps[]` сохраняет app-level approval/policy identity, а каждый app entry публикует `windows[]` со всеми selectable visible instances.
- `windows[]` получает primary public selector `windowId` плюс explicit low-level/debug selector `hwnd`.
- `get_app_state` переходит на targeting по `windowId | hwnd`; `appId` остаётся approval/session identity и убирается из execution selector path.
- Rejected alternative: flat per-window top-level entries. Причина отказа: такой shape дублирует approval key на каждом окне, смешивает policy identity с execution target и оставляет public surface в ambiguity-классе, который этот stage как раз должен убрать.

**Шаги:**

- [x] Choose DTO shape: per-window entries or grouped `windows[]`. Record decision and rejected alternative in this section before code.
- [x] Написать failing multi-window discovery test: two visible windows from same process/app identity both appear as selectable public entries.
- [x] Запустить targeted test и записать RED proof.
- [x] Написать failing test: `get_app_state` can target each discovered instance without ambiguous fallback.
- [x] Запустить targeted test и записать RED proof.
- [x] Introduce lightweight identity model for app policy key vs window execution target.
- [x] Implement discovery materializer in application owner layer.
- [x] Update `list_apps` payload/schema/docs.
- [x] Implement selection path in `get_app_state` from new public discovery fields.
- [x] Centralize result semantics needed for ambiguity/stale/missing/blocked states.
- [x] Update `ToolContractManifest`, registration, generated docs and install/publication acceptance.
- [x] Run integration tests and generated docs refresh.
- [x] Запустить review gate и re-review loop.
- [x] Сделать отдельный commit для этого stage.

**Команды проверки:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests"
scripts/refresh-generated-docs.ps1
```

**Ожидаемый результат:** `list_apps` discovery data достаточно, чтобы клиент выбрал конкретный visible window instance и затем вызвал `get_app_state` для этого же instance без hidden foreground guessing.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `41acfbf`
- TDD применялся: `да`
- Проверки:
  - DTO decision зафиксирован до production code: grouped `apps[] + windows[]`, `windowId` как primary selector, flat per-window top-level model explicitly rejected
  - RED proof: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinActionAndProjectionTests.ListAppsPublishesSelectableWindowInstancesInsideEachAppGroup|ComputerUseWinActionAndProjectionTests.GetAppStateTargetResolverResolvesEachDiscoveredWindowIdWithoutForegroundGuessing"` -> fail на отсутствии `ComputerUseWinAppDescriptor.Windows`, `ComputerUseWinWindowDescriptor` и `windowId` selector path в resolver
  - targeted GREEN: тот же filter -> green, `2/2`
  - affected contract contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinArchitectureTests|ComputerUseWinObservationTests|ComputerUseWinFinalizationTests|ComputerUseWinActionAndProjectionTests.ListAppsPublishesSelectableWindowInstancesInsideEachAppGroup|ComputerUseWinActionAndProjectionTests.GetAppStateTargetResolverResolvesEachDiscoveredWindowIdWithoutForegroundGuessing|McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints"` -> green, `96/96`
  - stage-wide `ComputerUseWin` contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"` -> green, `116/116`
  - stage-wide smoke contour: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests"` -> green, `22/22`
  - publication refresh: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\publish-computer-use-win-plugin.ps1` -> green
  - generated docs refresh: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\refresh-generated-docs.ps1` -> green
- Review agents:
  - `019dc0fb-4a96-7a43-98c4-e7a4d7ca2c79` (`architecture/contract`) -> `changes_requested/approve`
  - `019dc0fb-4f42-7c22-869a-a5b8f018663f` (`tests/failure/docs/generated`) -> `approve`
- Подтверждённые замечания:
  - `[confirmed]` runtime/product gap: `list_apps` публиковал только один representative `HWND` на app group, а `get_app_state(appId)` оставался в ambiguous/foreground-guess path для multi-window app
  - `[confirmed]` install/publication acceptance gap: helper `EnsurePublishedRuntimeBundle(...)` считал bundle готовым только по manifest completeness и мог reuse stale installed copy после contract change
  - `[confirmed]` selection target semantics были split между resolver и handler: resolver уже знал `ComputerUseWinExecutionTarget`, но возвращал только `WindowDescriptor`, а handler повторно восстанавливал `appId/windowId`
  - `[confirmed]` installed-copy acceptance проверял только `tools/list` schema и не делал real public `tools/call` из temp plugin copy
- Отклонённые замечания: `нет`
- Исправленные root causes:
  - app-level approval identity и window-level execution target разведены в lightweight owner model (`ComputerUseWinApprovalKey`, `ComputerUseWinWindowInstanceIdentity`, `ComputerUseWinExecutionTarget`, `ComputerUseWinDiscoveredApp`)
  - discovery materialization больше не collapse-ит app group в single representative hwnd: public payload теперь несёт nested `windows[]` со всеми selectable visible instances
  - `get_app_state` больше не использует `appId` как execution selector; public targeting идёт по `windowId | hwnd`, а `appId` остаётся approval/session identity
  - target failure semantics для `missing_target` / `identity_proof_unavailable` больше не живут ad hoc в handler branches: `ComputerUseWinGetAppStateTargetResolution` теперь несёт `Target`, а resolver остаётся единственным owner-слоем для selection result semantics
  - install-surface harness теперь отличает stale bundle от fresh source tree и не reuse-ит outdated published runtime только потому, что bundle совпал со своим manifest
  - installed-copy acceptance больше не ограничивается descriptor proof и теперь делает real `tools/call:list_apps` из fresh temp plugin copy
- Проверенные соседние paths:
  - `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinIdentityModel.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinAppDiscoveryService.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinListAppsHandler.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateTargetResolver.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinRequestContractValidator.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
  - `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs`
  - `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinFinalizationTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
  - `docs/architecture/computer-use-win-surface.md`
  - `docs/generated/computer-use-win-interfaces.md`
  - `docs/generated/computer-use-win-interfaces.json`
  - `docs/CHANGELOG.md`
- Остаточные риски:
  - `windowId` сознательно discovery-scoped и derived from current live instance identity + hwnd; caller не должен считать его durable beyond window churn и должен refresh-ить discovery/state после window recreation
  - pure observe split и advisory soft-fail policy по-прежнему out-of-scope этого stage и остаются decision gates `Stage 5/6`
- Разблокировка следующего этапа:
  - `Stage 4` закрыт commit `41acfbf`
  - `Stage 5` decision gate разблокирован

### Stage 5: Deferred class 2 decision and optional pure observe split

**Назначение:** решить, нужен ли продукту safe automatic observation без action-session side effects; реализовывать только при принятом решении.

**Зависит от:** Stage 4 committed, или явное product decision, что pure observe срочнее discovery redesign.

**Файлы, если решение принято:**

- Modify/Create under: `src/WinBridge.Server/ComputerUse/`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- Modify: `src/WinBridge.Runtime.Tooling/ToolNames.cs`
- Modify: `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`
- Docs/generated: `docs/architecture/computer-use-win-surface.md`, generated interface docs, `docs/CHANGELOG.md`

**Decision gate:**

**Decision outcome (зафиксировано до кода):**

- На текущем product/client evidence отдельный pure observe tool не нужен.
- Подтверждения:
  - shipped loop по repo docs остаётся `list_apps -> get_app_state -> click -> get_app_state`, а не `observe -> prepare -> act`;
  - [computer-use-win-surface.md](docs/architecture/computer-use-win-surface.md) уже явно фиксирует, что `get_app_state` не является observation-only read-only hint и может мутировать approval/focus/session state;
  - `Stage 4` уже убрал ambiguity через instance-addressable discovery (`windowId`) без добавления второй state tool boundary;
  - в product docs, roadmap и текущем test surface нет явной client/model need для public read-only observation token, который не может вести к action-ready loop.
- Decision: current `get_app_state` остаётся action-ready и side-effecting; `Stage 5` закрывается decision-only без кода и без нового public tool.

- [x] Confirm whether clients/models need safe automatic observation.
- [x] If not needed, record decision: current `get_app_state` remains action-ready and side-effecting; close this deferred class as rejected without code.
- [x] Decision outcome: separate observe public model не выбран, потому что pure observe split отклонён на текущем product stage.
- [x] Do not mark current `get_app_state` as read-only unless its side effects are removed by design.

**Целевая модель, если решение принято:**

- Pure observe path has no approval store writes, no foreground activation, no attached session mutation, no actionable `stateToken`.
- Prepare/action-ready path remains explicit and side-effecting.
- `click` cannot consume a pure observe token by accident.

**DDD-решение:** использовать action-readiness state model, если split принят. Не создавать широкий domain layer для простой DTO publication.

**TDD-решение:** обязательно, если меняется код.

**Шаги, если решение принято:**

- [ ] Написать failing test: pure observe does not activate foreground window.
- [ ] Написать failing test: pure observe does not write approval/session state.
- [ ] Написать failing test: pure observe token cannot be used by `click`.
- [ ] Implement observe owner using shared capture/UIA observation only where side-effect free.
- [ ] Implement explicit prepare/action-ready path and keep `click` gated on prepare token.
- [ ] Update annotations: pure observe `readOnlyHint=true`; prepare/action-ready `readOnlyHint=false`.
- [ ] Update generated docs and smoke assertions.
- [ ] Запустить review gate и re-review loop.
- [ ] Сделать отдельный commit для этого stage или decision-only closure commit, если split отклонён.

**Команды проверки, если решение принято:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"
scripts/refresh-generated-docs.ps1
```

**Ожидаемый результат:** ни один read-only public tool не мутирует foreground, approval или session state; action-ready state остаётся явным.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `6b0197c`
- TDD применялся: `нет, decision-only stage`
- Проверки:
  - static product evidence: [computer-use-win-surface.md](docs/architecture/computer-use-win-surface.md) уже фиксирует, что `get_app_state` не является read-only observe tool и может мутировать approval/focus/session state
  - stage sequencing evidence: после `Stage 4` shipped operator loop остаётся `list_apps -> get_app_state -> click`, а instance-addressable discovery убирает ambiguity без добавления второй state tool boundary
  - contract proof: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|McpProtocolSmokeTests.ComputerUseWinGetAppStateRequiresApprovalBeforeReturningState"` -> green, `2/2`
- Review agents:
  - `019dc10d-c839-7401-a933-43e2e9580729` (`architecture/contract`) -> `approve_with_minor_notes/approve`
  - `019dc10d-c9b9-7b92-beca-1644fa662a26` (`tests/failure/docs/generated`) -> `approve`
- Подтверждённые замечания:
  - `[confirmed]` текущий public contract уже осознанно держит `get_app_state` как action-ready и side-effecting path; evidence для separate pure observe tool в product docs / roadmap / shipped tests не найдено
  - `[confirmed]` minor changelog wording drift: decision note должен был явно говорить `readOnlyHint=false`, а не создавать впечатление отсутствующего annotation-поля
- Отклонённые замечания:
  - `pure observe split should be implemented now` -> rejected: отдельный observe tool расширит public surface и раздвоит state semantics без подтверждённой client/model need
- Исправленные root causes:
  - преждевременное расширение public surface остановлено на decision gate; current operator loop и read/write semantics `get_app_state` оставлены честными без вводимого second tool boundary
  - changelog wording выровнен на реальный smoke-tested contract: `get_app_state` explicitly публикуется с `readOnlyHint=false`
- Проверенные соседние paths:
  - `docs/architecture/computer-use-win-surface.md`
  - `docs/product/okno-roadmap.md`
  - `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
  - `docs/CHANGELOG.md`
- Остаточные риски:
  - future client/model need для safe automatic observation может переоткрыть эту deferred class позже; тогда понадобится отдельный TDD stage с новым public tool и token gating
- Разблокировка следующего этапа:
  - `Stage 5` закрыт decision-only commit `6b0197c`
  - `Stage 6` decision gate разблокирован

### Stage 6: Deferred class 3 decision and advisory provider failure policy

**Назначение:** решить, сохранять truthful failure semantics или принять stage-aware soft-fail для optional enrichment.

**Зависит от:** Stage 4 committed; Stage 5 decision recorded.

**Файлы, если решение принято:**

- Modify/Create under: `src/WinBridge.Server/ComputerUse/`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs`
- Modify: `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- Test: `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`
- Docs: `docs/architecture/computer-use-win-surface.md`, `docs/architecture/observability.md`, `docs/CHANGELOG.md`

**Decision gate:**

**Decision outcome (зафиксировано до кода):**

- Текущее product direction — сохранить truthful failure semantics без нового broad soft-fail matrix.
- Подтверждения:
  - [computer-use-win-surface.md](docs/architecture/computer-use-win-surface.md) уже фиксирует boundary: screenshot + accessibility tree определяют success/failure, advisory instructions soft-fail-ятся только на expected unavailable path, а unexpected provider/runtime bug остаётся `observation_failed`;
  - текущие observation tests уже доказывают нужную матрицу: capture failure -> `observation_failed`, UIA failure -> `observation_failed`, expected advisory instruction unavailability -> success + warning, unexpected provider bug -> `observation_failed`;
  - product docs и roadmap не содержат strong evidence, что availability-first soft-fail нужен шире уже существующего advisory instruction exception path.
- Decision: сохранить текущий invariant и закрыть `Stage 6` decision-only без code changes.

- [x] Confirm product direction: keep truthful failure semantics or choose availability-first soft-fail for named optional stages.
- [x] If keeping current invariant, record decision and close deferred class without code.
- [x] If changing policy, write a stage matrix before code. Decision: policy change rejected; новый matrix не нужен.

**Целевая модель, если решение принято:**

| Stage | Expected unavailable | Unexpected provider bug | Public outcome |
| --- | --- | --- | --- |
| instruction/advisory optional asset | soft-fail | product decision required | success with warning only if matrix allows |
| capture proof | fail | fail | `observation_failed` |
| UIA proof for action-ready state | fail unless explicitly optional | fail | `observation_failed` |
| decorative metadata | soft-fail | soft-fail with diagnostics if accepted | success + warning |

**DDD-решение:** оправдано только для compact stage policy model. Доменное понятие здесь — required proof vs optional enrichment.

**TDD-решение:** обязательно, если меняется policy.

**Шаги, если решение принято:**

- [ ] Написать failing test: required capture proof failure returns `observation_failed`.
- [ ] Написать failing test: required UIA proof failure returns `observation_failed`.
- [ ] Написать failing test: optional enrichment failure returns success with warning only for stages allowed by matrix.
- [ ] Implement stage-aware materializer with no broad catch-all soft-fail.
- [ ] Include stage and diagnostic evidence in warnings/failures.
- [ ] Update docs to state required proof vs optional enrichment.
- [ ] Запустить review gate и re-review loop.
- [ ] Сделать отдельный commit для этого stage или decision-only closure commit, если policy change отклонён.

**Команды проверки, если решение принято:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"
```

**Ожидаемый результат:** required proof failures никогда не скрываются как successful action-ready state; optional failures soft-fail только когда policy явно это разрешает.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `b8f507f`
- TDD применялся: `нет, decision-only stage`
- Проверки:
  - static policy evidence: [computer-use-win-surface.md](docs/architecture/computer-use-win-surface.md) уже фиксирует, что screenshot + accessibility tree остаются required proof, expected advisory instruction unavailability soft-fail-ится только в узком path, а unexpected provider/runtime bug materialize-ится как `observation_failed`
  - product evidence: в current docs/roadmap нет strong signal, что availability-first soft-fail нужен шире уже существующего advisory instruction exception path
  - policy proof: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinObservationTests.AppStateObserverReturnsStructuredFailureWhenCaptureThrows|ComputerUseWinObservationTests.AppStateObserverReturnsStructuredFailureWhenSnapshotDoesNotComplete|ComputerUseWinObservationTests.AppStateObserverTreatsAdvisoryInstructionFailureAsWarningWithoutStateCommit|ComputerUseWinObservationTests.AppStateObserverTreatsUnexpectedInstructionProviderBugAsStructuredFailure"` -> green, `4/4`
- Review agents:
  - `019dc115-a65c-72f3-99b5-be5092086185` (`architecture/contract`) -> `approve`
  - `019dc115-aa97-7be3-9599-58869a676ace` (`tests/failure/docs/generated`) -> `approve`
- Подтверждённые замечания:
  - `[confirmed]` broad availability-first soft-fail policy не подтверждён product/client evidence; current narrow advisory exception path уже покрывает justified optional enrichment case без ослабления required proof semantics
- Отклонённые замечания:
  - `expand soft-fail matrix now` -> rejected: это ослабит truthful action-ready semantics без подтверждённой need и смешает expected advisory-unavailable path с unexpected provider/runtime failures
- Исправленные root causes:
  - преждевременное policy expansion остановлено на decision gate; required proof vs optional enrichment остаются разведены текущим narrow invariant без нового matrix complexity
- Проверенные соседние paths:
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs`
  - `docs/architecture/computer-use-win-surface.md`
  - `docs/CHANGELOG.md`
- Остаточные риски:
  - если позже появится named optional enrichment beyond current advisory instruction path, policy matrix придётся переоткрывать отдельным TDD stage с явным required-vs-optional catalog
- Разблокировка следующего этапа:
  - `Stage 6` закрыт decision-only commit `b8f507f`
  - `Stage 7` разблокирован

### Stage 7: S2/O1/I1 final hardening and closure

**Назначение:** закрыть оставшийся state/audit/isolation drift после product и protocol work.

**Зависит от:** Stage 4 committed; Stage 5/6 decisions recorded.

**Файлы:**

- Modify/Create under: `src/WinBridge.Server/ComputerUse/`
- Modify: `src/WinBridge.Runtime.Diagnostics/` only if audit builder belongs below server layer.
- Modify: `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`
- Modify: `tests/WinBridge.Runtime.Tests/*` if diagnostics or architecture rules move there.
- Docs: `docs/architecture/computer-use-win-surface.md`, `docs/architecture/observability.md`, generated docs, `docs/CHANGELOG.md`

**Целевая модель:**

- State transitions are explicit: `attached`, `observed`, `approved`, `stale`, `blocked`.
- Forbidden transitions are tested:
  - no action from stale state;
  - approval does not replace fresh observation;
  - blocked/stale paths cannot become successful action-ready state without new live proof.
- Audit/event payload construction is centralized and redaction-safe.
- New process isolation is added only for a confirmed risky capability boundary.

**DDD-решение:** оправдано для state transition model и audit evidence policy. Не оправдано для unrelated diagnostics plumbing.

**TDD-решение:** обязательно для state/audit behavior.

**Шаги:**

- [x] Написать failing tests для forbidden state transitions.
- [x] Implement explicit runtime state model and remove duplicated implicit transition checks.
- [x] Написать failing tests для safe audit payload builders: no sensitive fields leak in started/completed events.
- [x] Implement safe audit/event builder owner and replace per-handler maps.
- [x] Review whether any capability now has confirmed host-risky failure profile.
- [x] If yes, design targeted isolation with evidence; if no, record `I1` as intentionally deferred.
- [x] Run full relevant verification contour.
- [x] Запустить review gate и re-review loop.
- [ ] Сделать отдельный commit для этого stage.

**Команды проверки:**

```powershell
dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"
dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "Audit|Diagnostics|Architecture"
scripts/codex/verify.ps1
```

**Ожидаемый результат:** оставшиеся инварианты опираются на явный код/tests, а не на дисциплину handler-ов.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-deferred-work-implementation`
- Commit SHA: `ea9a3e8`
- TDD применялся: `да`
- Проверки:
  - RED proof: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinArchitectureTests.RuntimeStateModelRejectsActionFromStaleState|ComputerUseWinArchitectureTests.RuntimeStateModelDoesNotTreatApprovalAsFreshObservationWithoutLiveProof|ComputerUseWinArchitectureTests.RuntimeStateModelDoesNotPromoteBlockedStateWithoutNewLiveProof|ComputerUseWinFinalizationTests.FinalizerDoesNotLeakStateTokenInCompletionAudit|ComputerUseWinFinalizationTests.ActionFinalizerDoesNotLeakRawReasonInCompletionAudit"` -> fail на отсутствии explicit runtime state model types и current audit leak semantics
  - targeted GREEN: тот же filter -> green, `5/5`
  - re-review targeted red/green: `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "AuditLogTests.BeginInvocationRedactsComputerUseWinStateTokenFromRequestSummary"` -> red then green, `1/1`; `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWinActionAndProjectionTests.StoredStateResolverMaterializesObservedActionReadyStateForLiveStoredWindow"` -> red then green, `1/1`
  - supporting publish refresh before broad suite: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\publish-computer-use-win-plugin.ps1` -> green
  - full `ComputerUseWin` contour after re-review fixes: `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"` -> green, `122/122`
  - runtime audit/diagnostics contour after re-review fixes: `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "Audit|Diagnostics|Architecture"` -> green, `36/36`
  - full repo contour after re-review fixes: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1` -> green; Runtime `638/638`, Server `253/253`, smoke ok, refresh-generated-docs ok
- Review agents:
  - `019dc141-b296-7180-b351-57fb3cb7fba3` (`architecture/contract`) -> `changes_requested/approve`
  - `019dc154-d030-74e3-99c1-0cda9721420b` (`tests/failure/docs/generated`) -> `changes_requested/approve`
- Подтверждённые замечания:
  - `[confirmed]` implicit runtime state transitions оставались размазанными по handler-ам и resolver-ам: stale/approved/blocked behavior опирался на scattered checks, а не на explicit state owner
  - `[confirmed]` `tool.invocation.completed` для `computer-use-win` всё ещё строился ad hoc maps и допускал leak `state_token` / `raw_reason`
  - `[confirmed]` install-surface freshness helper считал test-generated `plugins/computer-use-win/runtime/*` directories source inputs, что могло re-trigger publish inside `dotnet test` и загрязнять full `ComputerUseWin` suite
  - `[confirmed]` `tool.invocation.started` для `computer-use-win` всё ещё мог писать raw `stateToken` через `request_summary`, пока redaction class для public tools не был задан явно
  - `[confirmed]` stage report drift: post-fix verification totals и targeted re-review checks должны были быть синхронизированы с final Stage 7 evidence
- Отклонённые замечания:
  - `expand process isolation now` -> rejected: нового подтверждённого host-risky capability boundary сверх уже существующих isolated slices не найдено
- Исправленные root causes:
  - explicit runtime state owner `ComputerUseWinRuntimeStateModel` теперь фиксирует `attached` / `approved` / `observed` / `stale` / `blocked` и запрещает action-ready promotion из stale/blocked path без нового live proof
  - safe audit/event builder owner `ComputerUseWinAuditDataBuilder` заменил per-handler maps для `get_app_state` / `click` completion paths и убрал raw `stateToken` / raw low-level `reason` из completion audit trail
  - `AuditToolContext` теперь задаёт explicit redaction class для shipped `computer-use-win` tools, поэтому started-event request summary не leak-ит raw `stateToken`
  - typed owner context `ComputerUseWinActionReadyState` materialize-ит `Observed` только после live stored-state proof; click path больше не опирается на implicit “есть live state => можно действовать”
  - install-surface freshness enumeration больше не принимает generated runtime subtree за source-of-truth и не провоцирует nested publish contamination внутри full suite
  - `I1` зафиксирован как intentionally deferred: evidence не показывает новый host-risky capability boundary, требующий отдельного isolation slice
  - final stage report синхронизирован с post-fix verification contour и больше не держит stale totals/evidence
- Проверенные соседние paths:
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinRuntimeStateModel.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinAuditDataBuilder.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinStoredStateResolver.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinActionFinalizer.cs`
  - `src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinFinalizationTests.cs`
  - `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`
  - `docs/architecture/computer-use-win-surface.md`
  - `docs/architecture/observability.md`
  - `docs/CHANGELOG.md`
- Остаточные риски:
  - install/publication freshness still requires explicit publish step before broad suite when source code changed; full contour is now stable again, but this remains a harness precondition worth preserving
  - any future optional enrichment beyond current advisory instruction path may require reopening both state model and policy matrix together, not piecemeal
- Разблокировка следующего этапа:
  - `Stage 7` закрыт commit `ea9a3e8`
  - заполнить final checklist/report и выполнить branch-level review относительно `main`

## Финальный checklist выполнения

- [x] Все обязательные stages `0-4` выполнены и закоммичены.
- [x] Stage `5` либо реализован, либо явно отклонён с product rationale.
- [x] Stage `6` либо реализован, либо явно отклонён с product rationale.
- [x] Stage `7` выполнен или вынесен в новый active ExecPlan с rationale.
- [x] Каждый commit имеет review/re-review evidence от `gpt-5.5` subagents.
- [x] Для каждого подтверждённого review finding записаны root cause, закрытый класс случаев и проверенные neighbor paths.
- [x] Generated docs и `docs/CHANGELOG.md` синхронизированы с final contract.
- [x] Final verification contour записан.
- [x] В этом файле есть final status и commit list.

## Финальный отчёт выполнения

- Общий статус: `completed`
- Завершённые stages:
  - `Stage 0` baseline snapshot -> commit `35777c6`
  - `Stage 1` public surface freeze -> commit `2e7dc4b`
  - `Stage 2` tool-layer decomposition + composition root stabilization -> commit `b31a9a1`
  - `Stage 3` MCP `2025-11-25` migration -> commit `623390d`
  - `Stage 4` instance-addressable discovery -> commit `41acfbf`
  - `Stage 5` pure observe split decision -> rejected without code, commit `6b0197c`
  - `Stage 6` advisory failure policy decision -> broad soft-fail rejected without code, commit `b8f507f`
  - `Stage 7` state/audit hardening + `I1` intentionally deferred -> commit `ea9a3e8`
- Commit list:
  - `35777c6` `docs: snapshot computer-use-win stage 0 baseline`
  - `2e7dc4b` `refactor: freeze computer-use-win public boundary`
  - `b31a9a1` `refactor: decompose computer-use-win tool layer`
  - `623390d` `feat: migrate computer-use-win MCP baseline to 2025-11-25`
  - `41acfbf` `feat: add instance-addressable computer-use-win discovery`
  - `6b0197c` `docs: close computer-use-win pure observe decision gate`
  - `b8f507f` `docs: close computer-use-win advisory policy gate`
  - `ea9a3e8` `refactor: harden computer-use-win state and audit semantics`
- Final verification:
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "ComputerUseWin"` -> green, `122/122`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "McpProtocolSmokeTests"` -> green, `22/22`
  - `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "Audit|Diagnostics|Architecture"` -> green, `36/36`
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\publish-computer-use-win-plugin.ps1` -> green
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1` -> green; Runtime `638/638`, Server `253/253`, smoke ok, refresh-generated-docs ok
- Review summary:
  - Перед каждым stage commit выполнен обязательный `gpt-5.5` review/re-review gate по architecture/contract и tests/docs/generated surface.
  - Подтверждённые findings обрабатывались через root-cause fix, neighbor-path checks и повторный review до `approve` / `approve_with_minor_notes`.
  - После `Stage 7` выполнен branch-level review относительно `main`: cumulative closure-layer после синхронизации final exec-plan evidence получил `approve_with_minor_notes` / `approve_with_minor_notes`; blocking issues на ветке не осталось.
- Post-final review follow-up (`2026-04-26`):
  - `[confirmed]` external branch review нашёл реальный identity gap: `windowId` Stage 4 был discovery-scoped по формулировке, но фактически выводился из reusable live fingerprint. Closure fix перевёл `windowId` на runtime-owned opaque catalog selector и добавил fail-closed continuity proof для `windowId`, attached fallback и `stateToken` paths.
  - `[confirmed]` external branch review нашёл неполный install freshness gate: `PublishedRuntimeBundleIsFresh` не учитывал repo-root build inputs. Closure fix расширил input inventory на `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `WinBridge.sln` и root `*.props` / `*.targets` (плюс `NuGet.Config`, если он появится).
  - `[confirmed]` second external review wave показал, что closure fix смешал continuity semantics разных paths и не гарантировал batch lifetime для опубликованных selectors. Follow-up fix развёл proof modes (`windowId` strict discovery proof, attached session continuity, observed-state continuity), сделал `ComputerUseWinExecutionTargetCatalog` batch-aware и расширил freshness inventory до analyzer/config inputs (`.editorconfig`, `.globalconfig`, `*.globalconfig`, `Directory.Build.rsp`).
  - post-review verification: targeted selector/freshness RED pack -> green `18/18`; `ComputerUseWinActionAndProjectionTests|ComputerUseWinArchitectureTests` -> green `81/81`; `ComputerUseWinInstallSurfaceTests.PublishedRuntimeBundleIsFreshReturnsFalseWhenRepoLevelBuildInputIsNewerThanManifest|ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints` -> green `12/12`; broad `ComputerUseWin` contour -> green `140/140`; `McpProtocolSmokeTests` -> green `22/22`; `scripts/codex/verify.ps1` -> green, Runtime `638/638`, Server `271/271`, smoke ok, refresh-generated-docs ok.
- Deferred decisions:
  - `Stage 5`: отдельный public pure observe tool не вводится; `get_app_state` остаётся action-ready и side-effecting.
  - `Stage 6`: broad availability-first soft-fail policy не вводится; сохраняется truthful failure semantics с narrow advisory instruction soft-fail path.
  - `Stage 7 / I1`: targeted isolation expansion intentionally deferred; нового подтверждённого host-risky capability boundary не найдено.
- Remaining risks:
  - `windowId` остаётся discovery-scoped selector и не должен считаться durable beyond сохранённый discovery snapshot; при drift/recreation runtime теперь намеренно fail-close-ится вместо silent retarget.
  - attached/observed continuity теперь intentionally weaker than strict `windowId` proof, поэтому полностью неотличимый HWND replacement без новых platform signals остаётся фундаментально неразличимым для текущего Windows-visible evidence surface.
  - future optional enrichment beyond current advisory instruction path потребует reopening state/policy matrix отдельным TDD stage.
  - install/publication contour остаётся стабилен при external publish refresh; этот pre-step следует сохранять перед broad suite, если source code changed.

## Справочный материал ниже

Разделы ниже сохраняют исходные findings, rationale, target design, acceptance criteria, sequencing и migration context без потерь. При исполнении рабочий статус ведётся в `Линейный план реализации`, `Отчёт этапа`, `Финальный checklist выполнения` и `Финальный отчёт выполнения` выше. Чекбоксы в старых `Task plan` sections ниже считаются детализацией требований для соответствующих stages, а не отдельной параллельной очередью исполнения.

## Почему это вынесено из текущей ветки

Текущая ветка была contract-hardening веткой. Она должна была сделать уже выбранный public `computer-use-win` surface честным и безопасным: `list_apps -> get_app_state -> click`, lifecycle hints, typed failures, install artifact. Оставшиеся темы меняют не только implementation, но и product model:

- discovery DTO и client workflow;
- адресацию target-а на уровне app vs window instance;
- возможное разделение одного public tool-а на два разных semantic tools;
- policy для того, какие advisory failures считаются truthful product failure, а какие soft-fail evidence.

Такие изменения должны идти отдельной веткой с собственным design review и acceptance matrix.

---

## Deferred class 1: Instance-addressable discovery

### Как нашли

Review findings: `79`, `81`, `92`, `98`.

Суть повторялась в разных формулировках: `list_apps` сейчас группирует видимые окна по process-derived `appId` и публикует один public entry. При multi-window приложениях часть окон становится невидимой для клиента: discovery показывает app-level abstraction, а execution фактически работает с конкретным `HWND` / window identity.

В текущей ветке это не исправлялось как bugfix, потому что existing public loop уже был зафиксирован вокруг `appId`, approval и `stateToken`. Исправление требует изменить public discovery contract.

### Что это такое

Это drift между двумя уровнями identity:

- `appId` как app-level approval / policy key;
- `hwnd` и stable window identity как window-level execution target.

Если discovery прячет window instances за одним `appId`, клиент не может выбрать конкретное background окно без внешнего знания `HWND`. При этом `get_app_state` и `click` уже должны доказывать target на уровне конкретного окна.

### Почему это нужно

Без instance-addressable discovery остаются product gaps:

- несколько окон `notepad`, `explorer` или browser windows не все достижимы через public discovery;
- `ambiguous_target` может быть честным failure, но клиент не получает enough public data, чтобы устранить ambiguity;
- approval model и execution model продолжают жить на разных уровнях abstraction.

### Target design

Нужен public discovery surface, который показывает selectable window instances и при этом сохраняет app-level policy key.

Допустимые варианты:

1. `list_apps` возвращает один entry на window instance.
2. `list_apps` сохраняет app grouping, но внутри каждого app entry публикует `windows[]`.
3. Добавляется новый `list_windows`-style public discovery tool для `computer-use-win`, а `list_apps` остаётся compatibility/discovery summary.

Предпочтительный вариант для отдельной ветки: начать с design spike между вариантами 1 и 2. Не добавлять новый tool, пока не доказано, что existing `list_apps` нельзя расширить без breaking confusion.

### Затрагиваемые файлы

- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `docs/architecture/computer-use-win-surface.md`
- `docs/generated/computer-use-win-interfaces.md`
- `docs/generated/computer-use-win-interfaces.json`
- `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`

### Task plan

- [ ] **Task 1: Capture current discovery contract**
  - Read current `list_apps` DTO and generated docs.
  - Document whether `appId`, `hwnd`, title, process identity and approval fields are public.
  - Add a small design note to this plan or a follow-up design doc before implementation.

- [ ] **Task 2: Choose target DTO shape**
  - Decide between per-window entries and grouped `windows[]`.
  - Preserve `appId` as approval key.
  - Introduce a stable public window selector field if needed, but do not expose raw `HWND` as the only semantic selector unless explicitly accepted.

- [ ] **Task 3: Write failing tests for multi-window discovery**
  - Scenario: two visible windows from same process/app identity.
  - Expected: both windows are selectable through public discovery.
  - Expected: `get_app_state` can target each discovered instance without ambiguous fallback.

- [ ] **Task 4: Implement discovery owner-layer**
  - Move grouping/flattening decision into one `BuildAppDescriptors` / discovery materializer.
  - Keep approval semantics app-level.
  - Keep execution resolution window-level.

- [ ] **Task 5: Update schema/docs/generated interfaces**
  - Regenerate public interface docs.
  - Update architecture docs with app-level policy vs window-level execution model.

- [ ] **Task 6: Add acceptance scenarios**
  - Multi-window `notepad`.
  - Multi-window `explorer`.
  - Browser with multiple top-level windows if stable enough for local smoke.

### Acceptance criteria

- Public discovery exposes every visible selectable window instance.
- Client can select a background window using only public discovery data.
- `appId` remains approval/policy key and does not become a fragile window identity surrogate.
- `get_app_state` no longer has to guess foreground instance when discovery already selected a concrete window.
- Docs describe the distinction between app group, approval key and execution target.

### Verification contour

- Targeted integration tests for multi-window app identity.
- Generated docs refresh.
- `scripts/codex/verify.ps1`.
- Optional manual smoke with two real windows if test harness cannot reliably model Explorer/browser.

---

## Deferred class 2: Optional pure observe split for `get_app_state`

### Как нашли

Related finding: `103`.

The confirmed bug in this area was fixed in current branch by making metadata honest: current `get_app_state` is not read-only because it may approve, activate/focus, commit state token and attach session. The deferred work is different: decide whether product needs a separate pure observation tool.

### Что это такое

Current `get_app_state` is an action-session preparation tool. It observes state, but also prepares the state needed for future actions. That is a valid product model, but it means clients must not treat it as safe read-only.

A future product design could split this into:

- pure observe tool: no approval mutation, no activation, no session attach, no durable state token commit;
- prepare tool: explicit approval/activation/session preparation for action-ready state.

### Почему это нужно

This is only needed if we want clients/models to safely auto-run observation without foreground/session side effects. If current product loop intentionally requires action-ready preparation, the split is not necessary.

### Target design

Do not weaken current `get_app_state` metadata. If split is chosen, add a new explicit surface rather than lying through annotations.

Possible model:

- `observe_app_state` or `preview_app_state`: read-only, no activation, no approval mutation, no action token.
- `get_app_state`: remains action-ready and side-effecting.
- Or rename future action-ready path to `prepare_app_state` while keeping compatibility only if product accepts it.

### Затрагиваемые файлы

- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Runtime.Tooling/ToolNames.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `docs/architecture/computer-use-win-surface.md`
- `docs/generated/computer-use-win-interfaces.md`
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`

### Task plan

- [ ] **Task 1: Decide product need**
  - Confirm whether model/client workflows need safe automatic observation.
  - If not needed, keep current metadata-only fix and close this deferred item as rejected.

- [ ] **Task 2: Define pure observe invariants**
  - No approval store writes.
  - No foreground activation.
  - No attached session mutation.
  - No action-ready `stateToken` unless token is explicitly marked non-actionable.

- [ ] **Task 3: Write failing tests**
  - Pure observe call does not call `ActivateAsync`.
  - Pure observe call does not update approval/session stores.
  - Pure observe payload cannot be used for `click` unless promoted through prepare path.

- [ ] **Task 4: Implement separate owner-layer**
  - Share capture/UIA observation where safe.
  - Keep action lifecycle/token semantics only in prepare/action-ready path.

- [ ] **Task 5: Sync metadata and docs**
  - Pure observe tool: `readOnlyHint=true`.
  - Prepare/action-ready tool: `readOnlyHint=false`, `OsSideEffect`.
  - Generated docs and smoke assertions must reflect both.

### Acceptance criteria

- No tool with `readOnlyHint=true` mutates foreground, approval or session state.
- Action-ready state remains explicit and machine-readable.
- Existing `click` cannot consume a pure observe token by accident.
- Docs explain the difference between observing and preparing.

### Verification contour

- Unit/integration tests around approval/session mutation.
- `tools/list` smoke assertions for annotations.
- Generated interface docs refresh.
- Full `scripts/codex/verify.ps1`.

---

## Deferred class 3: Advisory provider failure policy redesign

### Как нашли

Review findings: `56`, `63`.

These findings argued that advisory/provider bugs should always soft-fail. During current branch triage, this was rejected for the current contract because it conflicted with the accepted invariant: unexpected observation/provider failures must materialize truthfully as `observation_failed` instead of being hidden as success.

### Что это такое

There are two different categories:

- expected advisory unavailable: optional instruction/advisory asset missing or intentionally unavailable;
- unexpected provider/runtime bug: capture/UIA/advisory provider throws or returns inconsistent state.

Current branch preserves the distinction:

- expected advisory unavailable can be non-fatal;
- unexpected provider/runtime bug is truthful failure.

### Почему это может понадобиться

If product direction changes toward “best-effort observation must always return something”, then provider failure policy must be redesigned explicitly. That would trade contract truthfulness for higher availability.

This is not a local bugfix. It changes how much uncertainty public payloads may hide.

### Target design

If reopened, define a stage-aware policy table:

| Stage | Expected unavailable | Unexpected provider bug | Public outcome |
|---|---|---|---|
| instruction/advisory optional asset | soft-fail | maybe `observation_failed` | depends on product choice |
| capture proof | fail | fail | `observation_failed` |
| UIA proof for action-ready state | fail unless explicitly optional | fail | `observation_failed` |
| decorative metadata | soft-fail | soft-fail with diagnostics | success + warning |

No implementation should use broad catch-all soft-fail without stage classification.

### Затрагиваемые файлы

- `src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `docs/architecture/computer-use-win-surface.md`
- `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`

### Task plan

- [ ] **Task 1: Decide policy direction**
  - Keep truthful failure semantics, or explicitly choose availability-first soft-fail for some stages.
  - Record decision in architecture docs before code.

- [ ] **Task 2: Build provider stage matrix**
  - Enumerate capture, UIA, advisory, instruction and decorative metadata stages.
  - Mark each stage as required proof or optional enrichment.

- [ ] **Task 3: Write failing tests from matrix**
  - Required proof failure returns `observation_failed`.
  - Optional enrichment failure returns success with explicit warning/evidence, if that policy is chosen.

- [ ] **Task 4: Implement stage-aware materializer**
  - No broad catch-all soft-fail.
  - Failure/warning must carry stage and diagnostic evidence.

- [ ] **Task 5: Sync docs**
  - Public docs must state which data is required proof and which data is optional enrichment.

### Acceptance criteria

- Required proof failures are not hidden as successful action-ready state.
- Optional advisory failures do not prevent successful state only when policy explicitly marks them optional.
- Public payload includes enough machine-readable information for client retry/refresh decisions.

### Verification contour

- Targeted observation failure tests.
- Audit/evidence tests for warning vs failure.
- Full `scripts/codex/verify.ps1`.

---

## Cross-cutting acceptance

- Every future implementation task must start with a fresh source-of-truth check: current code, tests, generated docs and product docs.
- Redesign-grade changes must not be smuggled into bugfix branches.
- New public DTO fields must be reflected in schema, generated docs, smoke assertions and changelog in the same PR.
- MCP spec citations in docs should point to the latest official revision unless a file is explicitly documenting the currently negotiated runtime baseline.
- If a future review finding belongs to one of these deferred classes, first update this plan or fork it into a dedicated active ExecPlan before coding.

## Дополнительный архитектурный контекст

Более широкий статический разбор той же поверхности добавляет один важный сквозной вывод: текущие deferred items для `computer-use-win` не живут изолированно от host-архитектуры. Основное давление на drift возникает из-за того, что server tool classes всё ещё совмещают несколько ролей:

- binding транспорта MCP;
- validation запроса и selector admission;
- продуктовую policy/orchestration;
- materialization результата и failure;
- publication/profile semantics.

Практически это проявляется в двух местах:

1. `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs` уже одновременно является и публичным adapter-слоем продукта, и фактическим application host для `list_apps`, `get_app_state` и `click`, при этом продолжает держать latent next-wave action methods.
2. `src/WinBridge.Server/Program.cs` всё ещё использует late-bound `hostServices` closures для создания MCP tools. Для текущего размера это допустимо, но после декомпозиции tool owners и появления более мелких handler/application units эта форма станет заметно хрупче.

Это **не** означает, что текущий shipped contract неверен. Это означает, что следующие product branches нужно запускать вместе с небольшим объёмом архитектурного hardening, чтобы product redesign не закреплял ещё сильнее текущую transport-hosted orchestration shape.

## Companion hardening tracks

Эти треки — сопутствующая работа для product redesign classes выше. Они не все обязательны перед каждой будущей веткой, но именно они задают самый безопасный порядок следующих волн `computer-use-win`.

| Track | Почему это важно здесь | Основной scope | Критерий завершения | Priority |
| --- | --- | --- | --- | --- |
| `C0. Deferred action surface freeze` | `ComputerUseWinTools` уже содержит callable methods для `type_text`, `press_key`, `scroll` и `drag`, а `ComputerUseWinToolRegistration` уже содержит create-methods для их MCP publication. Хотя сейчас экспортируются только `list_apps/get_app_state/click`, текущая форма кода держит риск случайной публикации выше, чем нужно. | `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`, `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`, `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`, `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*` | Непубликуемые action paths либо вынесены за явную internal draft boundary, либо их случайная публикация технически невозможна; tests фиксируют ровно три public implemented tools. | `P1` |
| `A1. Tool layer decompression` | Deferred class 1 почти наверняка затронет discovery DTOs, target resolution и public payload shaping. Делать это внутри текущего монолитного tool host значит усилить drift pressure. | Начать с `ComputerUseWinTools`; трогать engine/shared layers только там, где discovery или state prep требуют общих abstractions. | `ComputerUseWinTools` становится thin transport adapter; app-state observation, click orchestration и result presenters/finalizers живут в отдельных owner-layers. | `P1` |
| `A3. Dependency guard rails` | Текущие границы сильны по соглашениям и tests, но ещё не закреплены явными architectural rules. Deferred redesign branches увеличат число moving parts. | Project-reference rules, forbidden namespace/type usage, publication/profile invariants. | CI/test suite падает, если policy-bearing tools обходят gate, если появляется publication/profile drift или если forbidden refs пересекают согласованный layer matrix. | `P1` |
| `CR1. Composition root stabilization` | Когда handlers и presenters начнут дробиться, текущий `hostServices` closure pattern в `Program.cs` станет более явной хрупкостью composition root. | `src/WinBridge.Server/Program.cs`, MCP tool registration helpers. | Tool registration больше не зависит от late-bound `hostServices` closures для core `computer-use-win` surface. | `P1` |
| `A2. First-class application boundary` | Deferred class 1 — первая redesign branch, где `computer-use-win` уже выгодно получить явные application-layer units вместо дальнейшего роста server tool host. | Scenario/use-case handlers, orchestration coordinators, policy evaluators, result presenters/materializers. | Product orchestration становится first-class boundary, а не скрытой ролью MCP tool classes. | `P1` |
| `S1. Unified result semantics` | Discovery redesign, observe/prepare split и advisory policy все зависят от canonical owner для статусов `ok`, `failed`, `blocked`, `approval_required`, `verify_needed` и будущих observation-only states. | Public result/failure lifecycle owners для `computer-use-win`. | Один canonical source of truth для result semantics управляет runtime payloads, docs/export wording и retry/refresh guidance. | `P1` |
| `C1. Publication + install contract matrix` | Public contract для `computer-use-win` — это не только `tools/list`; сюда же входят launcher args, runtime bundle shape и install/runtime materialization path. | `ToolContractManifest`, tool registration, generated interfaces, launcher docs, plugin install/runtime bundle acceptance. | `manifest == registration == profile == launcher/install surface` закреплён tests и docs. | `P2` |
| `S2. Explicit runtime state model` | Deferred class 2 и class 3 заметно упрощаются, если approval/session/state-token transitions описаны явно, а не выводятся косвенно из текущих flows. | `ComputerUseWinStateStore`, session interaction, approval flow, token semantics. | Allowed states и forbidden transitions записаны и покрыты tests. | `P2` |
| `O1. Safe audit builders` | Advisory policy redesign и будущий observe-only split не должны опираться на вручную собранный audit/result wording внутри больших handlers. | Shared audit/event metadata builders и result-to-audit mapping. | Sensitive payload/event metadata собираются в одном safe owner-layer вместо per-handler maps. | `P2` |
| `I1. Targeted isolation expansion` | Имеет смысл только если later redesign покажет ещё одну host-risky capability boundary, похожую на UIA worker isolation. | Capability-specific, только evidence-driven. | Isolation расширяется только для подтверждённого risky slice, а не как topology-first cleanup. | `P3` |

## MCP 2025-11-25 migration track

До закрытия `Stage 3` этот migration block действительно описывал gap между latest MCP revision `2025-11-25` и repo-local baseline `2025-06-18`. По итогам `Stage 3` для текущего `STDIO` продукта этот gap закрыт:

- `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs` теперь публикует `protocolVersion = 2025-11-25`;
- `scripts/smoke.ps1` и stage-owned integration/install tests синхронизированы на `2025-11-25`;
- generated interface exports после refresh больше не расходятся с live handshake expectations.

Primary official sources for this migration block:

- MCP changelog `2025-11-25`;
- MCP tools spec `2025-11-25`;
- MCP lifecycle spec `2025-11-25`;
- MCP transports spec `2025-11-25`.

По официальному changelog и latest spec для `Okno`/`computer-use-win` наиболее релевантны такие дельты:

- `stdio` теперь явно допускает любой logging в `stderr`, а клиент не должен считать `stderr` error-only каналом;
- `Implementation` в `initialize` получил optional `description`, `icons`, `websiteUrl`;
- в `tools/list` появились optional `icons` и `execution.taskSupport`;
- для tool names появилась явная guidance по длине и допустимым символам;
- `inputSchema` и `outputSchema` по умолчанию считаются JSON Schema `2020-12`, если `$schema` не указан;
- input validation errors должны materialize-иться как Tool Execution Errors, а не как protocol errors;
- request payload schemas отделены от RPC method definitions как самостоятельные parameter schemas;
- HTTP/auth/task changes не являются immediate scope для текущего local `STDIO` runtime, но их нужно явно пометить как out-of-scope, а не просто игнорировать.

| Track | Почему это нужно до/вместе с миграцией | Основной scope | Критерий завершения | Priority |
| --- | --- | --- | --- | --- |
| `M0. Spec delta inventory freeze` | Нельзя поднимать `protocolVersion` механически, пока не зафиксировано, какие дельты latest spec реально относятся к текущему `STDIO`-only продукту, а какие сознательно откладываются. | Official MCP delta inventory, repo-local source-of-truth mapping, in-scope vs out-of-scope decisions. | Есть принятый список migration deltas с явным разделением `must implement now` / `explicitly defer`. | `P1` |
| `M1. Negotiated protocol baseline upgrade` | До `Stage 3` smoke/tests/exporter/generated docs жёстко несли `2025-06-18`, из-за чего docs и runtime расходились. | `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`, `scripts/smoke.ps1`, `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`, `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`, generated interface exports. | Все protocolVersion literals/exported baselines синхронизированы на `2025-11-25`, а generated docs не расходятся с live handshake expectations. | `P1` |
| `M2. Initialize metadata and capability audit` | Latest lifecycle расширяет `Implementation` и capability negotiation; это нужно осознанно принять или осознанно не публиковать. | `initialize` request/response expectations, `serverInfo`, docs, smoke assertions, capability negotiation around `tasks`. | Решено и зафиксировано, что сервер публикует или сознательно не публикует из новых optional fields/capabilities; tests отражают это решение. | `P1` |
| `M3. Tool metadata and schema audit` | Latest tools spec добавляет `icons`, `execution.taskSupport`, tool naming guidance и JSON Schema `2020-12` defaults. Это касается contract export, public descriptors и manual schemas. | `ToolContractManifest`, MCP registration, exporter output, tool naming inventory, manual `inputSchema` / `outputSchema` owners. | Public tool metadata и schemas либо поддерживают новые optional fields корректно, либо явно фиксируют отсутствие; имена tools и schema assumptions проверены против latest guidance. | `P1` |
| `M4. Validation and error semantics alignment` | В latest spec input validation errors должны идти как tool execution errors. Для `Okno` это особенно важно из-за честной safety/contract semantics. | Request binders, validators, tool handlers/finalizers, smoke/integration tests across public and deferred surfaces. | Malformed tool arguments materialize-ятся как canonical tool-level failures там, где latest spec требует input-validation semantics; unknown tool и protocol-shape failures остаются protocol-owned. | `P1` |
| `M5. Parameter-schema and generated-contract sync` | После schema/runtime changes нельзя оставить старые generated exports или implicit SDK assumptions. | Exporters, generated docs/json, contract docs, install/publication acceptance, manual schema tests. | `manifest/export/generated docs/smoke/install surface` согласованы с latest protocol baseline и не полагаются на legacy parameter-shape assumptions. | `P2` |

## Sequenced roadmap

Самый безопасный порядок — развести **product-facing redesign** и минимальный **architecture hardening**, который не даёт этим redesign branches закрепить текущий drift.

| Iteration | Цель | Основные работы | Критерий завершения |
| --- | --- | --- | --- |
| `Iteration 1: Surface freeze and handler decompression` | Снизить риск случайной публикации и перестать растить `computer-use-win` внутри одного монолитного tool host. | `C0`, initial `A1` на `ComputerUseWinTools`, параллельный `A3`, затем `CR1`. | Public surface по-прежнему ровно `list_apps/get_app_state/click`; для первых extracted flows уже есть thin transport adapters; architectural rules начинают фиксировать publication и gate usage; `Program.cs` больше не держит core `computer-use-win` registration через текущий late-bound pattern. |
| `Iteration 1.5: MCP 2025-11-25 protocol baseline` | Поднять protocol/docs baseline до latest revision до того, как discovery/public result redesign расширит surface area ещё сильнее. | `M0`, `M1`, `M2`, `M3`, `M4`, затем `M5`. | Runtime/export/tests/generated docs согласованы на `2025-11-25`; latest-relevant deltas либо реализованы, либо явно задокументированы как out-of-scope для текущего `STDIO` продукта. |
| `Iteration 2: Instance discovery redesign on explicit application boundary` | Закрыть deferred class 1 поверх более ясной orchestration shape и уже обновлённого MCP baseline. | Deferred class 1 + `A2` + основной `S1` + `C1`. | Public discovery показывает selectable window instances без скрытого foreground guessing; product orchestration живёт в explicit application units; docs, generated exports, launcher/install surface и publication tests согласованы. |
| `Iteration 3: Optional observation/state-policy redesign` | Открывать optional redesign items только при подтверждённой product need. | Deferred class 2 и/или deferred class 3, плюс `S2`, `O1`, optional `I1`. | Observe-only split или advisory soft-fail policy приняты как product decision, а не как побочный refactor; state transitions и audit semantics описаны явно и проверяются tests. |

## Dependency order

| Track / branch | Зависит от | Когда запускать | Когда считать завершённым |
| --- | --- | --- | --- |
| `C0. Deferred action surface freeze` | nothing | первым | Unpublished action wave больше не может быть экспортирован случайно. |
| Initial `A1` | желательно после старта `C0` | сразу после `C0` | `ComputerUseWinTools` перестаёт быть owner сразу для transport + orchestration + presenter concerns. |
| `A3. Dependency guard rails` | agreed boundary names | параллельно с ранним `A1` | Tests фиксируют allowed direction matrix, gate usage и publication invariants. |
| `CR1. Composition root stabilization` | первые extracted owner layers из `A1` | после первого meaningful extraction seam | Core `computer-use-win` tool registration больше не зависит от `hostServices` closure indirection. |
| `M0. Spec delta inventory freeze` | nothing | можно начать сразу; завершать до code-level migration | Есть accepted inventory relevant MCP deltas для текущего `STDIO` продукта. |
| `M1` + `M2` + `M3` + `M4` + `M5` | `M0`, желательно `C0`; `M2/M3` выигрывают от `A3`, а `M1`/`M5` выигрывают от `CR1` | после Iteration 1 | Protocol baseline, initialize/tool metadata, validation semantics и generated exports согласованы на latest spec. |
| Deferred class 1 + `A2` + main `S1` | ранний `A1`, желательно `CR1`, предпочтительно завершённый `M1-M5` | после Iteration 1.5 | Discovery redesign приземляется на explicit application-layer owners и canonical result semantics уже поверх обновлённого MCP baseline. |
| `C1. Publication + install contract matrix` | DTO/publication shape из deferred class 1 и ownership model из `A2` | ближе к концу Iteration 2 | Все public contract surfaces согласованы: profile, docs, generated interfaces, launcher/install path. |
| Deferred class 2 | class 1 не обязателен, но `S1` крайне желателен | только при явной product need | Observe-only path имеет отдельные invariants и не может быть ошибочно прочитан как action-ready preparation. |
| Deferred class 3 | `S1` крайне желателен, `O1` полезен | только после явного product decision переоткрыть truthful failure invariant | Stage-aware policy для required proof vs optional enrichment описана явно и машинно читаема. |
| `S2`, `O1`, optional `I1` | лучше после `S1`; `I1` только по evidence | Iteration 3 | State transitions, audit semantics и любая новая isolation boundary появляются осознанно и подкреплены tests. |

## Recommended queue inside iterations

### Iteration 1

1. `C0` first.
   - Сначала заморозить deferred action wave, а уже потом делать более широкий refactor.
   - Считать “callable but not published” smell формы, а не harmless dead code.
2. Запустить `A1` внутри `ComputerUseWinTools`.
   - Первыми extraction targets должны быть:
     - discovery materialization,
     - app-state observation orchestration,
     - click orchestration,
     - result/failure finalization.
   - **Не** начинать с полного repo-wide split `WindowTools`.
3. Запустить `A3` параллельно, когда первые owner-layer names уже стабилизированы.
4. Применить `CR1` после первого реального extraction seam.
   - Цель — не перенести текущую `Program.cs` composition shape в новый graph обработчиков.

### Iteration 2

1. Запустить deferred class 1 как первую product-facing redesign branch поверх уже закрытого `Stage 3` / MCP `2025-11-25` baseline.
   - Не переоткрывать отдельную MCP migration branch для `M0-M5`: этот инженерный слой уже закрыт текущим plan execution.
   - Использовать уже зафиксированные `serverInfo` / `tools/list` / schema decisions как стабильный protocol floor для discovery redesign.
2. После этого использовать branch для реального instance-addressable discovery, а не для повторной protocol migration.
3. Использовать эту branch, чтобы сделать `A2` реальным для discovery/app-state path.
4. Довести `S1`, пока branch ещё затрагивает все релевантные public statuses и retry semantics.
5. Закрывать `C1` только после того, как DTO shape, publication semantics и launcher/install model уже согласованы.

### Iteration 3

1. Возвращаться к deferred class 2 только если есть конкретная client/model need для safe automatic observation.
2. Возвращаться к deferred class 3 только если продукт явно выбирает availability-first soft-fail для части stages.
3. Использовать `S2`, чтобы определить allowed states и forbidden transitions, например:
   - no action from stale state;
   - approval не заменяет fresh observation cycle;
   - blocked/stale paths не могут быть эскалированы в successful action-ready state без нового live proof.
4. Использовать `O1`, чтобы убрать вручную собранный audit/result wording из future redesign handlers.
5. Рассматривать `I1` только если появится новая подтверждённая host-risky boundary.

## Suggested branch split

Ниже сохранён branch sketch для всего плана, но ветки `2-3` уже больше не являются pending queue: их scope фактически закрыт текущим `Stage 3` и оставлен здесь только как historical decomposition reference.

1. `codex/computer-use-win-surface-freeze`
   - Владеет `C0`.
   - Может включать минимальные `A3` assertions, нужные для фиксации текущего public surface.

2. `codex/mcp-2025-11-25-baseline` (`historical`, scope закрыт `Stage 3`)
   - Владел `M0`, `M1` и `M5`.
   - Поднял negotiated/exported protocol baseline и синхронизировал generated contract surface.

3. `codex/mcp-2025-11-25-tool-semantics` (`historical`, scope закрыт `Stage 3`)
   - Владела `M2`, `M3` и `M4`, если эти изменения не помещались в baseline branch.
   - Зафиксировала `STDIO` in-scope и HTTP/auth/task items out-of-scope для текущего продукта.

4. `codex/computer-use-win-instance-discovery`
   - Владеет deferred class 1.
   - Предпочтительное место для первого серьёзного `A1/A2/S1` extraction для `computer-use-win`.

5. `codex/computer-use-win-observe-prepare-split`
   - Владеет deferred class 2 только если продукт выбирает pure read-only observation.
   - Не должен стартовать раньше class 1, если нет concrete urgent client need.

6. `codex/computer-use-win-advisory-policy`
   - Владеет deferred class 3 только если текущая truthful observation failure semantics переоткрывается осознанно.

## Current recommendation

Следующей product-facing branch всё ещё должна стать instance-addressable discovery. Обязательный инженерный слой protocol migration branch на MCP `2025-11-25` уже закрыт в `Stage 3`, поэтому актуальная последовательность теперь такая:

1. сначала заморозить deferred action wave (`C0`);
2. сделать narrow decomposition `ComputerUseWinTools` плюс initial dependency rules (`A1` + `A3`);
3. стабилизировать composition root (`CR1`) после появления первого extraction seam;
4. затем запускать `instance-addressable discovery` branch как первую redesign branch, используя её для формирования explicit application boundary у `computer-use-win`.

Держать `get_app_state` observe/prepare split и advisory policy redesign parked до появления явной product/client need. Это всё ещё валидные deferred classes, но они не должны конкурировать ни с discovery branch, ни с ранним hardening, ни с уже закрытым protocol migration stage, который теперь больше не блокирует дальнейшие public changes на latest MCP baseline.
