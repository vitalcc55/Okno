# ExecPlan: safety baseline для future action tools

Статус: completed
Архивирован: 2026-03-31
Создан: 2026-03-30
Обновлён: 2026-03-31

## Goal

Довести reporting-first guard layer до общего execution baseline для будущих action tools, чтобы следующий публичный action slice опирался на готовое решение по `allow / blocked / needs_confirmation / dry_run_only`, а не втаскивал собственный mini-policy на boundary.

Итог этого workstream должен дать:

- единый typed policy/decision слой между `WinBridge.Runtime.Tooling`, `WinBridge.Runtime.Guards`, `WinBridge.Server` и `WinBridge.Runtime.Diagnostics`;
- канонический shape для blocked/confirmation/dry-run decisions и readable reasons;
- redaction hooks для чувствительных request/result/artifact payloads;
- достаточный contract/docs/test baseline, после которого `windows.launch_process` реализуется как tool semantics + existing guard decision.

Текущий итог по состоянию на `2026-03-31`:

- `Package A`, `Package B`, `Package C` и verification/proof wave `Package D` завершены;
- `Package E` завершён: shared launch-readiness policy больше не остаётся always-blocked placeholder и теперь даёт reusable `ready / degraded / blocked / unknown` модель на уровне общего guard layer;
- общий baseline по metadata, gate, redaction, contract/export и verification contour собран и подтверждён последовательным verification loop;
- контрольный ответ для `windows.launch_process` теперь = `да`: следующий slice можно строить как tool semantics + existing guard decision без новой safety-логики внутри launch handler-а.

### Non-goals

В этот workstream намеренно не входят:

- реализация `windows.launch_process`, `windows.open_target`, `windows.input`, `windows.clipboard_*` или `windows.uia_action.*`;
- изменение lifecycle или hidden behavior уже shipped observe tools;
- большой ACL/policy-engine с per-user overrides, background enforcement или persistent policy store;
- скрытый deny-path для shipped tools без отдельного contract change;
- попытка заранее решить все future action edge-cases вместо минимального reusable baseline.

### Done When

Safety baseline считается закрытым, когда одновременно верно следующее:

- в `Tooling` есть один canonical metadata layer для action-oriented policy;
- server/runtime boundary умеет выдавать `allowed`, `blocked`, `needs_confirmation`, `dry_run_only`;
- blocked/confirmation reasons имеют один typed shape и не расходятся между manifest, evaluator и export;
- diagnostics умеет redaction-first журналирование чувствительных payloads без утечки исходных значений;
- `okno.health` и/или exported contract явно показывают, что future action tools будут опираться на existing safety layer;
- L1/L2 tests доказывают одинаковые решения на metadata/runtime/server границах;
- после этого `windows.launch_process` можно проектировать без повторного изобретения block/confirm/dry-run/redaction logic.

## Current Repo State After Shipped Guard Layer

- `okno.health` уже публикует typed readiness snapshot через `HealthResult`, `RuntimeReadinessSnapshot`, `CapabilityGuardSummary` и `GuardReason`.
- `RuntimeGuardService` + `RuntimeGuardPolicy` уже являются reporting-first source of truth для readiness facts по `desktop_session`, `session_alignment`, `integrity`, `uiaccess` и derived capability summaries для `capture`, `uia`, `wait`, `input`, `clipboard`, `launch`.
- `ToolContractManifest` пока знает только `lifecycle`, `safety_class`, `summary`, `planned_phase`, `suggested_alternative` и `smoke_required`; policy metadata для future action tools в manifest отсутствует.
- `ToolExecution` и `AuditLog` обеспечивают invocation tracing и exception boundary, но не имеют общего preflight evaluator и не умеют tool-aware redaction.
- `okno.health` осознанно reporting-first и пока ничего не меняет в execution behavior already shipped tools.

## Gap Between Reporting-First And Safety Baseline

Сейчас runtime умеет ответить на вопрос “что blocked/degraded”, но ещё не умеет единообразно ответить на вопрос “что делать на tool boundary”.

Конкретный разрыв между текущим слоем и нужным baseline:

- readiness snapshot описывает риск по capability families, но не привязан к tool metadata для будущих action tools;
- `ToolDescriptor` не может выразить `risk_level`, `policy_group`, `supports_dry_run`, `requires_confirmation`, `redaction_sensitivity`;
- server boundary не имеет одного evaluator, который по manifest metadata + runtime guard snapshot возвращает каноническое preflight decision;
- diagnostics пишут `request_summary` через generic JSON serialization и могут утекать в raw payload, когда появятся текстовый input, launch args, clipboard values или target paths;
- generated/docs surface ещё не может объяснить агенту, где future action tool будет hard-blocked, где нужен explicit confirmation, а где допустим only dry-run path.

## Public Surface Invariants

- `okno.health` остаётся reporting-first summary tool и не превращается в отдельный policy-engine API.
- Shipped observe/session tools (`okno.health`, `okno.contract`, `okno.session_state`, `windows.list_*`, `windows.attach_window`, `windows.activate_window`, `windows.focus_window`, `windows.capture`, `windows.uia_snapshot`, `windows.wait`) не меняют lifecycle и hidden execution semantics в рамках этого плана.
- Новый safety layer допускает additive contract expansion, но не должен публиковать выдуманный success для blocked action path.
- Если boundary возвращает blocked/confirmation/dry-run decision, это должно происходить как canonical tool result будущего action tool, а не как protocol-level MCP error.
- `needs_confirmation` не подменяет platform hard block: отсутствующий `uiAccess`, session mismatch и другие объективные ограничения остаются blocked-path, а не “спросить пользователя и всё равно выполнить”.
- Dry-run должен быть честным non-side-effect mode, а не hidden partial execution.

## Policy Metadata Model

### Design stance

Не перегружать текущий `ToolSafetyClass`. Он по-прежнему отвечает на coarse transport/safety annotation, а новый safety baseline добавляет отдельный typed слой policy metadata.

### Preferred shape

В `src/WinBridge.Runtime.Tooling/` вводится новый typed descriptor, например `ToolExecutionPolicyDescriptor`, который описывает:

- `risk_level`
- `policy_group`
- `guard_capability`
- `supports_dry_run`
- `confirmation_mode`
- `redaction_class`

Предпочтительная файловая посадка:

- новый файл `src/WinBridge.Runtime.Tooling/ToolExecutionPolicyDescriptor.cs`;
- новые literal enums/value objects в `src/WinBridge.Runtime.Tooling/` для risk/confirmation/redaction/policy-group;
- `src/WinBridge.Runtime.Tooling/ToolDescriptor.cs` расширяется полем `ExecutionPolicy`;
- `src/WinBridge.Runtime.Contracts/ContractToolDescriptor.cs` и `src/WinBridge.Runtime.Tooling/ContractToolDescriptorFactory.cs` начинают экспортировать этот metadata layer в `okno.contract` и generated docs.

### Policy groups

Минимальный набор групп для этого baseline:

- `observe`
- `session_mutation`
- `launch`
- `input`
- `clipboard`
- `uia_action`

### Risk levels

Минимальный набор risk levels:

- `low`
- `medium`
- `high`
- `destructive`

### Confirmation modes

Минимальный набор confirmation modes:

- `none`
- `required`
- `conditional`

### Redaction classes

Минимальный набор redaction classes:

- `none`
- `target_metadata`
- `text_payload`
- `clipboard_payload`
- `launch_payload`
- `artifact_reference`

### Metadata rules

- Для already shipped observe tools metadata может быть `null` или explicit minimal profile без new behavior.
- Для существующих deferred action descriptors (`windows.input`, `windows.clipboard_set`, `windows.uia_action`) metadata должна быть заполнена сразу.
- Для ещё не добавленных public tools (`windows.launch_process`, `windows.open_target`) baseline должен зафиксировать policy presets заранее, но без premature publication tool names в manifest.
- `guard_capability` должен явно связывать tool metadata с existing readiness families (`input`, `clipboard`, `launch`), чтобы evaluator не дублировал hand-written routing по handler-ам.

## Execution Boundary Model

### Core evaluator

Нужен единый gate/evaluator seam с contracts в `src/WinBridge.Runtime.Tooling/` и concrete evaluator в `src/WinBridge.Runtime.Guards/`, условно:

- `IToolExecutionGate`
- `ToolExecutionGate`
- `ToolExecutionDecision`

Его вход:

- `ToolExecutionPolicyDescriptor`
- current `SessionSnapshot`
- current `RuntimeGuardAssessment`
- invocation intent (`is_dry_run_requested`, optional confirmation token/state позже)

Его выход:

- `decision`
- `risk_level`
- `reasons`
- `requires_confirmation`
- `dry_run_supported`

Канонические значения `decision`:

- `allowed`
- `blocked`
- `needs_confirmation`
- `dry_run_only`

### Decision rules

- `allowed`: runtime может перейти к tool semantics.
- `blocked`: future action tool завершает вызов без side effects и возвращает canonical blocked payload.
- `needs_confirmation`: boundary завершает вызов без side effects и возвращает typed confirmation-required payload; повторный запуск после explicit confirmation станет отдельным contract step, а не hidden retry.
- `dry_run_only`: boundary запрещает live side effect, но разрешает deterministic preview path при явном `dryRun=true`.

### Where the gate lives

Еvaluator должен жить рядом с existing runtime guard source of truth, а не в каждом tool handler.

Предпочтительная схема:

- `RuntimeGuardService` продолжает собирать authoritative snapshot;
- `ToolExecutionGate` поверх него проецирует tool-level decision;
- `WinBridge.Runtime.Diagnostics/ToolExecution.cs` получает новый overload/hook для gated execution;
- future action handlers вызывают только этот helper, а не руками собирают checks по `session_alignment`, `integrity`, `uiaccess` и т.д.

### Boundary invariants

- observe tools не обязаны сразу переходить на новый gated overload;
- future action tools не должны делать ad hoc policy logic внутри handler;
- blocked/confirmation/dry-run reasons должны использовать те же reason codes/source families, что и reporting layer, без параллельного словаря на server boundary;
- evaluator не должен читать новые raw probes напрямую, если этот факт уже есть в `RuntimeGuardAssessment`.

## Redaction / Diagnostics Model

### Problem to close

`AuditLog.BeginInvocation(...)` сейчас пишет `request_summary` через generic serialization. Для future action tools это приведёт к утечке текста, аргументов запуска, URL/file targets, clipboard payloads и других чувствительных данных в `events.jsonl` и `summary.md`.

### Required model

В `src/WinBridge.Runtime.Diagnostics/` нужен отдельный redaction-first слой, условно:

- `IAuditPayloadRedactor`
- `AuditPayloadRedactor`
- `AuditRedactionResult`

Этот слой должен:

- принимать tool metadata + raw request/result payload;
- выпускать sanitized summary;
- помечать, что redaction применён;
- перечислять redacted fields/class без публикации raw values.

### Minimum redaction rules

- `text_payload`: не писать вводимый текст, а публиковать только длину, наличие и redaction marker.
- `clipboard_payload`: не писать значение clipboard, а публиковать только content kind/size hints.
- `launch_payload`: не писать raw command line / env / secret-bearing args, а публиковать executable identity + redacted argument marker.
- `artifact_reference`: можно публиковать artifact path/status, но не inline contents чувствительных artifacts.
- blocked/confirmation/dry-run payloads тоже проходят через redactor, чтобы reason path случайно не раскрыл исходные args.

### Diagnostics invariants

- readable public reason остаётся коротким и безопасным;
- internal audit event может хранить `reason_code`, `decision`, `risk_level`, `redaction_applied`, `redacted_fields`;
- при сбое redaction fail-safe поведение должно быть “не писать чувствительный summary”, а не “записать raw payload ради диагностики”;
- existing observe diagnostics не должны терять текущий полезный контекст.

## Integration Points By File

| Файл | Изменение | Зачем |
| --- | --- | --- |
| `src/WinBridge.Runtime.Tooling/ToolDescriptor.cs` | Добавить `ExecutionPolicy` | Сделать metadata частью canonical source of truth |
| `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` | Заполнить policy metadata для deferred action descriptors и policy presets | Убрать ad hoc policy из будущих handlers |
| `src/WinBridge.Runtime.Tooling/ContractToolDescriptorFactory.cs` | Экспортировать policy fields в contract DTO | Синхронизировать runtime source of truth и `okno.contract` |
| `src/WinBridge.Runtime.Contracts/ContractToolDescriptor.cs` | Добавить exported policy fields | Сделать metadata machine-readable для агента и generated docs |
| `src/WinBridge.Runtime.Guards/RuntimeGuardService.cs` | Переиспользовать existing snapshot как input для gate | Не плодить новый probe stack |
| `src/WinBridge.Runtime.Guards/RuntimeGuardPolicy.cs` | Добавить helper mapping readiness -> tool gate reasons при сохранении current health semantics | Одна reason taxonomy для reporting и execution |
| `src/WinBridge.Runtime.Tooling/` (новые файлы) | Ввести `IToolExecutionGate`, `ToolExecutionDecision`, `ToolExecutionIntent` | Зафиксировать reusable gate contracts без project-cycle в graph |
| `src/WinBridge.Runtime.Guards/ToolExecutionGate.cs` | Реализовать evaluator поверх existing `RuntimeGuardAssessment` | Дать единый preflight evaluator без нового probe stack |
| `src/WinBridge.Runtime.Diagnostics/ToolExecution.cs` | Добавить gated execution overload | Вынести preflight logic из future action handlers |
| `src/WinBridge.Runtime.Diagnostics/AuditLog.cs` | Подключить redaction-aware request/result summaries | Предотвратить утечки чувствительных payloads |
| `src/WinBridge.Runtime.Diagnostics/` (новые файлы) | Ввести redaction service/result types | Канонизировать sanitized audit path |
| `src/WinBridge.Server/Tools/AdminTools.cs` | Отразить safety baseline в `okno.health` и/или `okno.contract` surface без нового tool | Сделать policy visibility доступной агенту |
| `src/WinBridge.Server/Tools/WindowTools.cs` | Без изменения shipped behavior; только future adoption target | Зафиксировать, что gating будет подключаться здесь позже, а не переписываться заново |
| `tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs` | Проверить manifest/export alignment для policy metadata | Исключить doc/manifest drift |
| `tests/WinBridge.Runtime.Tests/ContractToolDescriptorFactoryTests.cs` | Проверить snake_case/export policy fields | Зафиксировать contract literal set |
| `tests/WinBridge.Runtime.Tests/RuntimeGuardPolicyTests.cs` | Проверить mapping из readiness facts в gate reasons | Не допустить расходящегося reason vocabulary |
| `tests/WinBridge.Runtime.Tests/RuntimeGuardServiceTests.cs` | Проверить reuse existing snapshot in gate path | Зафиксировать единый source of truth |
| `tests/WinBridge.Runtime.Tests/AuditLogTests.cs` | Добавить redaction assertions | Закрыть payload leak regression |
| `tests/WinBridge.Server.IntegrationTests/AdminToolTests.cs` | Проверить exported policy visibility в `okno.health` / `okno.contract` | Доказать agent-visible surface |
| `tests/WinBridge.Server.IntegrationTests/` (новый тест) | Synthetic gated action boundary test | Доказать `blocked / needs_confirmation / dry_run_only` без публичного action tool |
| `docs/architecture/capability-design-policy.md` | Зафиксировать reusable decision/redaction invariants | Закрыть архитектурную дыру до action rollout |
| `docs/architecture/observability.md` | Описать новые audit/redaction markers | Синхронизировать investigation path |
| `docs/generated/project-interfaces.md` | Отразить новые exported policy fields | Согласовать contract docs с runtime |
| `docs/generated/commands.md` | Отразить новые policy annotations/notes | Согласовать agent-facing command surface |
| `docs/product/okno-roadmap.md` | Обновить status/wording после фактической поставки, не раньше | Не публиковать незавершённый shipped state |

## Official Constraints And Decisions

### MCP tools contract

Source: [MCP Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools), [MCP Schema](https://modelcontextprotocol.io/specification/2025-11-25/schema)

Binding facts:

- tool execution errors должны возвращаться через result с `isError: true`, а не через protocol-level JSON-RPC error;
- `structuredContent` является каноническим structured payload;
- для sensitive operations клиентам рекомендуется human-in-the-loop с confirmation.

Decision:

- future blocked/confirmation/dry-run outcomes публикуются как canonical tool results;
- confirmation request описывается machine-readable payload, а не скрытым side effect;
- output schema/structured content должны быть готовы к этим outcomes заранее.

### Input integrity / UIPI

Source: [SendInput](https://learn.microsoft.com/en-gb/windows/win32/api/winuser/nf-winuser-sendinput?redirectedfrom=MSDN), [Application manifests](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests), [UI Automation Security Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-security-overview)

Binding facts:

- `SendInput` subject to UIPI и разрешён только в equal-or-lower integrity targets;
- отсутствие явного `uiAccess`/manifest baseline не позволяет честно обещать bypass protected UI;
- `uiAccess` по умолчанию `false` и не должен трактоваться как soft warning.

Decision:

- future `input` и часть `uia_action` получают hard block при неподтверждённом integrity/uiAccess path;
- `needs_confirmation` не может обойти platform hard block;
- `launch` не должен обещать elevated interaction path только по факту наличия admin group hint.

### Session / desktop readiness

Source: [OpenInputDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openinputdesktop), [WTSGetActiveConsoleSessionId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-wtsgetactiveconsolesessionid), [ProcessIdToSessionId](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-processidtosessionid), [WTSQuerySessionInformation](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsquerysessioninformationw)

Binding facts:

- `OpenInputDesktop` может вернуть desktop handle даже для disconnected session;
- `WTSGetActiveConsoleSessionId` возвращает `0xFFFFFFFF` в attach/detach transition;
- `ProcessIdToSessionId` и `WTSGetActiveConsoleSessionId` дают прямые session identifiers;
- `WTSQuerySessionInformation` полезен как enrichment, но может fail-иться, когда Remote Desktop Services не запущен.

Decision:

- desktop handle сам по себе не означает `allowed`;
- session mismatch/transition остаются authoritative blockers для future live action path;
- `WTSQuerySessionInformation` остаётся enrichment probe, а не single point of truth.

### Foreground / focus limits

Source: [Window Features / SetForegroundWindow restrictions](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)

Binding facts:

- система ограничивает, какие процессы могут вывести окно на foreground;
- даже когда условия формально подходят, `SetForegroundWindow` всё равно может быть denied.

Decision:

- focus/activate success нельзя использовать как глобальный allow-signal для `launch` или `input`;
- future action tools обязаны делать post-verification собственных semantics, а safety baseline не должен обещать больше, чем реально гарантирует платформа.

### Token facts

Source: [GetTokenInformation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation), [CheckTokenMembership](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-checktokenmembership)

Binding facts:

- `GetTokenInformation` является canonical API для чтения token metadata;
- `CheckTokenMembership` решает узкий вопрос membership/enabled SID и не заменяет полноценный token profile.

Decision:

- integrity/elevation/uiAccess gating строится на token information, а membership используется только как secondary hint;
- evaluator не должен принимать allow/deny решения только по признаку “пользователь администратор”.

## Delivery Packages

### Package A: Tooling policy metadata

Содержимое:

- ввести typed policy descriptor и value sets;
- расширить `ToolDescriptor`, `ContractToolDescriptor`, manifest/export path;
- завести metadata для deferred action descriptors и policy presets для future launch family.

Выход пакета:

- `okno.contract` и generated docs умеют показать policy metadata;
- manifest становится единственным source of truth для tool-level policy intent.

### Package B: Execution gate

Содержимое:

- добавить `IToolExecutionGate`, `ToolExecutionDecision`, `ToolExecutionIntent` и concrete evaluator;
- добавить `ToolExecution` overload для gated invocation;
- доказать synthetic boundary path без публикации нового action tool.

Выход пакета:

- server/runtime boundary умеет одинаково возвращать `allowed / blocked / needs_confirmation / dry_run_only`;
- future action handler больше не проектирует safety logic самостоятельно.

### Package C: Redaction baseline

Содержимое:

- ввести tool-aware redactor;
- санитизировать request/result summaries и artifact references;
- добавить audit markers `redaction_applied`, `redacted_fields`, `decision`.

Выход пакета:

- чувствительные payloads больше не попадают в audit trail сырыми;
- failure path fail-safe: лучше отсутствие summary, чем утечка значения.

### Package D: Docs / verification / launch-readiness proof

Содержимое:

- обновить architecture/product/generated docs;
- синхронизировать `okno.health` / `okno.contract` wording;
- зафиксировать proof question для `windows.launch_process`.

Целевой выход пакета:

- другой инженер может начать `windows.launch_process` как следующий slice и не придумывать заново block/confirm/dry-run/redaction logic.

Статус по факту `2026-03-31`:

- последовательный verification contour (`scripts/bootstrap.ps1` -> `scripts/build.ps1` -> `scripts/test.ps1` -> `scripts/smoke.ps1` -> `scripts/refresh-generated-docs.ps1` -> `scripts/codex/verify.ps1`) пройден без build/test/smoke drift;
- generated/export слой не дал содержательного diff: `refresh-generated-docs.ps1` не изменил tracked generated files, а `okno.contract` / exporter / docs остались синхронизированы с manifest;
- контрольный ответ для `windows.launch_process` = `нет`: shared baseline уже закрывает reusable metadata/gate/redaction boundary, но сам shared launch-readiness policy ещё не умеет честно различать safe live launch и hard block по executable elevation/manifest boundary;
- evidence для residual gap находится в shared guard layer, а не в будущем handler-е: `RuntimeGuardPolicy.BuildLaunch(...)` всегда добавляет `launch_elevation_boundary_unconfirmed` и возвращает `launch` как deferred blocked capability даже в otherwise healthy environment; это закреплено `RuntimeGuardPolicyTests.BuildCapabilitiesAlwaysIncludesLaunchBoundaryWhenEnvironmentLooksReady()`;
- свежий smoke report (`artifacts/smoke/20260331T092213872/report.json`) подтверждает тот же итог на live surface: `okno.health` возвращает `launch=blocked` с reason codes `capability_not_implemented` и `launch_elevation_boundary_unconfirmed`, при этом dedicated health artifact/event по-прежнему не materialized.
- финальный checklist-пункт про `windows.launch_process` остаётся незакрытым намеренно: это residual safety gap, а не недописанный отчёт.

### Package E: Shared launch-readiness policy completion

Содержимое:

- доработать shared guard layer так, чтобы `launch` больше не был всегда hard-blocked только из-за baseline assumption;
- ввести reusable allow/degraded/blocked model для launch readiness на уровне shared policy, а не future handler-а;
- явно разделить platform hard blocks, confirmation-worthy live launch paths и dry-run-only paths там, где это честно возможно;
- закрепить это unit/integration/verify evidence и затем повторно ответить на proof question из `Package D`.

Жёсткие границы пакета:

- не реализовывать сам `windows.launch_process`;
- не расширять scope в `open_target`, `input`, `clipboard` или broad policy-engine;
- не дублировать launch policy внутри будущего handler-а, если тот же decision можно выразить в shared guard layer;
- не закрывать пакет фиктивно, если shared policy по-прежнему не может честно отличить safe live launch от hard block.

Целевой выход пакета:

- `RuntimeGuardPolicy.BuildLaunch(...)` перестаёт быть always-blocked placeholder и становится reusable launch-readiness policy;
- `okno.health`, gate decisions и docs остаются синхронизированы с этой моделью;
- на контрольный вопрос “можно ли теперь строить `windows.launch_process` без новой safety-логики внутри launch?” можно ответить `да`.

Статус по факту `2026-03-31`:

- `RuntimeGuardPolicy.BuildLaunch(...)` больше не использует deferred blocked placeholder и различает `ready`, `degraded`, `blocked` и `unknown` из уже существующих shared runtime facts (`desktop_session`, `session_alignment`, `integrity`) без нового policy subsystem;
- launch policy теперь честно разделяет environment hard blocks и confirmation-worthy medium-integrity path: interactive session transition остаётся `blocked`, unknown probes остаются `unknown`, medium integrity даёт `launch=degraded` с warning `launch_elevation_boundary_unconfirmed`, а high/system integrity даёт `launch=ready`;
- `ToolExecutionGate` не потребовал новых special-case веток: existing matrix уже даёт `needs_confirmation` для ready/degraded launch policy, `blocked`/`dry_run_only` для hard blocks и `allowed` для granted confirmation или explicit dry-run;
- unit/integration coverage обновлена на shared launch matrix, health projection и synthetic gate boundary; ручной export sync не понадобился, потому что `refresh-generated-docs.ps1` не дал tracked generated diff;
- свежий smoke report (`artifacts/smoke/20260331T102916305/report.json`) подтверждает live surface: `okno.health` возвращает `launch=degraded`, `blockedCapabilities` больше не включают `launch`, а warning list дополняется `launch_elevation_boundary_unconfirmed` без materialized health artifact/event;
- proof question закрыт положительно: residual gap из `Package D` исчез именно в shared guard layer, поэтому будущий `windows.launch_process` может опираться на existing launch preset + shared gate и добавлять только tool semantics, preview availability и post-action verification.

## L1 / L2 / L3

### L1

- unit tests на policy descriptor export и snake_case literals;
- unit tests на `ToolExecutionGate` decision matrix;
- unit tests на redaction rules и fail-safe behavior;
- unit tests на mapping existing readiness snapshot -> tool gate reasons.

### L2

- integration tests для `okno.contract` и `okno.health`, подтверждающие agent-visible policy metadata;
- synthetic server-boundary test, который вызывает gated execution helper и проверяет `blocked`, `needs_confirmation`, `dry_run_only` без side effects;
- regression tests, что shipped observe tools не меняют поведение из-за появления нового baseline.

### L3

Последовательный verify loop, без параллельного запуска shared build artifacts:

1. `scripts/build.ps1`
2. `scripts/test.ps1`
3. `scripts/smoke.ps1` только если additive admin/contract surface меняет smoke expectations
4. `scripts/refresh-generated-docs.ps1`
5. `scripts/codex/verify.ps1`

Если пакет меняет только internal/tests/docs и не трогает live MCP surface, это должно быть явно зафиксировано в verification notes, а не подразумеваться.

## Docs Sync

При фактической реализации safety baseline синхронизировать в том же цикле:

- `docs/architecture/capability-design-policy.md`
- `docs/architecture/observability.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/commands.md`
- `docs/bootstrap/bootstrap-status.json`
- `docs/product/okno-roadmap.md`
- при необходимости `docs/product/okno-spec.md`, если `okno.contract` или future action payload shape становятся user-visible

Жёсткое правило:

- не помечать roadmap row как completed/implemented до тех пор, пока не закрыты tests + docs + export sync;
- не публиковать `launch_process` как следующий shipped slice, если baseline ещё не доказал reusable gate behavior.

## Rollback

- additive metadata fields можно откатить без трогания current observe behavior;
- новый execution gate должен оставаться отключаемым для already shipped tools;
- если redaction слой нестабилен, fallback = не писать чувствительный summary, а не публиковать raw payload;
- если export/docs sync ломает consumer expectations, сначала откатить additive contract fields, а не менять readiness model задним числом;
- existing `okno.health` reporting-first snapshot остаётся источником фактов и не должен быть удалён ради возврата к ad hoc policy в handlers.

## Implementation Checklist

- [x] Добавить новый exec-policy descriptor в `src/WinBridge.Runtime.Tooling/`.
- [x] Расширить `src/WinBridge.Runtime.Tooling/ToolDescriptor.cs` полем `ExecutionPolicy`.
- [x] Расширить `src/WinBridge.Runtime.Contracts/ContractToolDescriptor.cs` exported policy fields.
- [x] Обновить `src/WinBridge.Runtime.Tooling/ContractToolDescriptorFactory.cs` для policy export.
- [x] Обновить `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` и заполнить metadata для deferred action descriptors.
- [x] Зафиксировать policy presets для future `launch` family без premature publication новых tool names.
- [x] Добавить reusable gate contracts (`IToolExecutionGate`, `ToolExecutionDecision`, `ToolExecutionIntent`) и concrete evaluator.
- [x] Подключить gate к existing `RuntimeGuardAssessment`, не создавая новый probe stack.
- [x] Добавить gated overload в `src/WinBridge.Runtime.Diagnostics/ToolExecution.cs`.
- [x] Добавить tool-aware redaction service в `src/WinBridge.Runtime.Diagnostics/`.
- [x] Перевести `src/WinBridge.Runtime.Diagnostics/AuditLog.cs` на sanitized request/result summaries.
- [x] Сделать safety metadata видимым через `okno.contract` без раздувания `okno.health`.
- [x] Подтвердить, что `src/WinBridge.Server/Tools/WindowTools.cs` не меняет shipped behavior в этом workstream.
- [x] Добавить L1 tests для manifest/export alignment.
- [x] Добавить L1 tests для gate decision matrix.
- [x] Добавить L1 tests для redaction markers и отсутствия raw payload leakage.
- [x] Добавить L2 integration test на synthetic gated action boundary.
- [x] Обновить `docs/architecture/capability-design-policy.md` и `docs/architecture/observability.md`.
- [x] После реализации прогнать `scripts/refresh-generated-docs.ps1` и синхронизировать generated docs.
- [x] Доработать shared launch-readiness policy в `RuntimeGuardPolicy.BuildLaunch(...)` так, чтобы `launch` не оставался always-blocked placeholder в otherwise healthy environment.
- [x] Закрепить reusable allow/degraded/blocked model для launch readiness tests и verification evidence, не реализуя сам `windows.launch_process`.
- [x] Перед закрытием workstream ответить на контрольный вопрос: “можно ли теперь строить `windows.launch_process` без новой safety-логики внутри launch?”
