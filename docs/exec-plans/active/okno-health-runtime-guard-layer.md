# ExecPlan: okno.health + runtime guard layer

Статус: in_progress
Создан: 2026-03-23
Обновлён: 2026-03-25

## Goal

Довести `okno.health` до authoritative runtime readiness snapshot и ввести единый guard-layer, который заранее объясняет, какие capability сейчас доступны, деградированы или опасны для использования.

Что должен закрыть итоговый workstream:

- `okno.health` остаётся единственным публичным health tool и начинает публиковать не только runtime summary, но и typed readiness/guard snapshot.
- runtime получает единый internal source of truth для `ready`, `degraded`, `blocked` и `warning` paths, который потом переиспользуют `launch`, `input`, `clipboard` и частично `capture/uia/wait`.
- первый этап остаётся reporting-first: guard-layer ничего скрыто не меняет в lifecycle и execution behavior уже shipped tools, пока это отдельно не зафиксировано в tool contract.
- readiness model становится достаточно конкретной для evidence, audit, L1/L2/L3 tests и последующего enforcement follow-up.

## Non-goals

В текущий workstream намеренно не входят:

- новый public tool вместо `okno.health`;
- смена lifecycle или public behavior уже shipped `windows.capture`, `windows.uia_snapshot`, `windows.wait`, `windows.list_windows`, `windows.attach_window`;
- реализация `windows.input`, `windows.clipboard_*`, `windows.launch_process` или `windows.open_target`;
- V2/V3 policy-engine с dynamic allow/deny, per-tool overrides или background enforcement;
- скрытый runtime deny-path для shipped tools без отдельного contract change и docs sync;
- попытка решить все UIA/input/elevation edge-cases через один общий “security subsystem”.

## Public surface

Публичная поверхность остаётся узкой и контролируемой:

- public tool остаётся `okno.health` в `src/WinBridge.Server/Tools/AdminTools.cs`;
- `HealthResult` в `src/WinBridge.Runtime.Contracts/HealthResult.cs` расширяется без замены tool name;
- `okno.contract` меняется только косвенно: описания, notes и generated docs должны отражать новый health scope, но не появляется отдельный guard tool;
- text description `okno.health` в `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs` обновляется так, чтобы обещать readiness/guard snapshot, а не только transport/artifacts/tool list.

## Internal surface

Внутренняя поверхность меняется шире публичной:

- новый typed runtime guard service как единственный source of truth для readiness domains и capability guard summaries;
- новые typed DTO в `WinBridge.Runtime.Contracts` для domain status, capability status, warning/blocking reasons и provenance probes;
- optional diagnostics/audit enrichment: health artifact и capability-specific runtime event;
- явные integration hooks для future `launch`, `input`, `clipboard`, но без автоматического enforcement на первом этапе.

## Current repo state

- `okno.health` уже реализован как public tool в `src/WinBridge.Server/Tools/AdminTools.cs` и сейчас возвращает тонкий runtime summary через `RuntimeInfo`, monitor topology и `ToolContractManifest`.
- текущий `HealthResult` содержит только service/version/transport/audit/artifacts, monitor count, display identity diagnostics и списки implemented/deferred tools.
- shipped public slices уже включают `windows.list_monitors`, `windows.list_windows`, `windows.attach_window`, `windows.activate_window`, `windows.focus_window`, `windows.capture`, `windows.uia_snapshot` и `windows.wait`; это подтверждено `docs/generated/project-interfaces.md` и `ToolContractManifest`.
- roadmap уже сдвинул следующий приоритет на `okno.health + runtime guard layer`, а deferred остались главным образом в `windows.clipboard_*`, `windows.input` и `windows.uia_action`.
- observability baseline уже умеет вести runtime-specific artifacts и events для `windows.capture`, `windows.uia_snapshot` и `windows.wait`, но у `okno.health` пока нет сопоставимого typed guard/evidence слоя.
- текущий `okno.health` описан как `read_only`, что остаётся валидным только если новый snapshot не начнёт создавать неожиданные OS side effects; запись локального artifact допустима как diagnostics side effect, но это нужно явно проверить против existing policy и descriptions.

## Why this slice now

- после shipped `windows.uia_snapshot` и `windows.wait` репозиторий уже умеет наблюдать live UI, но ещё не умеет заранее и единообразно объяснять, где среда честно готова к следующему шагу, а где нет;
- будущие `launch`, `input` и `clipboard` будут упираться не в отсутствие API-обёрток, а в недоопределённые guard rules: desktop session, session alignment, integrity/UIPI, UIA reachability и shell/input constraints;
- если не ввести guard baseline сейчас, следующие action slices начнут встраивать разрозненные проверки по своим модулям и размоют source of truth между handler-ами, runtime service-ами и docs;
- `okno.health` уже существует как публичный entry point и потому является правильным местом для reporting-first readiness snapshot без множения public surface.

## Official constraints

- `OpenInputDesktop` открывает desktop, который получает пользовательский ввод; вызов требует input-capable window station, а в disconnected session возвращает handle desktop-а, который станет активным после reconnection. Это делает probe пригодным для определения desktop-session readiness, но недостаточным как единственный signal “можно безопасно делать input прямо сейчас”.
- `ProcessIdToSessionId` даёт session id процесса, а `WTSGetActiveConsoleSessionId` даёт текущую physical console session; их расхождение должно трактоваться как authoritative signal для `session_alignment`, а не как косвенная эвристика.
- `WTSQuerySessionInformation` на локальной машине используется для получения session information и умеет честно падать, если Remote Desktop Services недоступен; это делает его подходящим enrichment probe, но не обязательным single point of truth.
- `GetTokenInformation` является canonical API для чтения token metadata по `TOKEN_INFORMATION_CLASS`; из него должны извлекаться integrity/elevation/uiAccess-related facts, а не вычисляться косвенно.
- Mandatory Integrity Control задаёт integrity label через access token и securable object SACL; объект без integrity SID трактуется как medium integrity. Это важно для честного маппинга `integrity` в `warning` против `blocked`.
- `CheckTokenMembership` годится для узкого факта вроде membership в Administrators group, но не заменяет `GetTokenInformation` для runtime readiness model.
- `SendInput` subject to UIPI и разрешён только в процессы с equal or lesser integrity level. Значит, `input_readiness` нельзя публиковать как `ready`, если runtime не может честно подтвердить отсутствие этого ограничения для целевого сценария.
- `SetForegroundWindow` не гарантирует принудительный foreground success даже при выполнении ряда условий; следовательно, guard-layer не должен обещать `launch/input` usability только по факту возможности вызвать focus API.
- UI Automation overview прямо фиксирует, что UI Automation не обеспечивает communication между процессами, запущенными разными пользователями через `Run as`; это должно входить в `uia_readiness` и будущий `input_readiness`.
- UI Automation tree имеет desktop root, но Microsoft рекомендует не делать descendant search от root как primary path и стартовать поиск от application window или lower-level container; значит, health должен репортить UIA readiness как window-scoped capability baseline, а не как обещание global UI reachability.
- MCP tools spec различает protocol errors и tool execution errors через `result.isError`; для `okno.health` это означает, что сам tool может оставаться success-path даже при наличии `blocked` capability, пока health snapshot честно и структурированно публикует ограничения среды.

## Guard domains

### Design stance

Текущий план фиксирует guard-layer как:

- reporting-first;
- typed-status-first;
- reusable-by-future-tools;
- non-enforcing для already shipped tools в первой поставке.

Переход к enforcement-first допускается только отдельным follow-up, когда конкретный tool contract и docs прямо поменяются.

### Domain status policy

Единый статус для readiness domains и capability summaries:

- `ready` — runtime имеет достаточные доказательства, что базовая capability семантически доступна в текущей среде;
- `degraded` — path частично доступен, но есть документированные ограничения, которые нельзя скрыть;
- `blocked` — обещать capability как usable сейчас было бы нечестно;
- `unknown` — probe не дал authoritative ответа, и runtime не должен симулировать уверенность.

Отдельно от статуса нужны reason severities:

- `warning` — есть caveat, но базовая операция всё ещё может оставаться честной в пределах контрактных ограничений;
- `blocked` — есть условие, при котором обещать capability-ready path нельзя;
- `info` — диагностическое уточнение без влияния на readiness status.

### Domain matrix

| Domain | Что означает | Authoritative probes | Базовый status mapping | Кому нужно |
| --- | --- | --- | --- | --- |
| `desktop_session` | есть ли у runtime доступ к input-capable desktop/session | `OpenInputDesktop`, process window station prerequisites, session query fallback | `blocked`, если input desktop недоступен; `degraded`, если desktop есть, но session disconnected/transitioning | всем GUI tools |
| `session_alignment` | согласован ли текущий process session с active console/current desktop | `ProcessIdToSessionId`, `WTSGetActiveConsoleSessionId`, optional `WTSQuerySessionInformation` | `blocked`, если процесс не в active console session для interactive scenario; `unknown`, если probes недоступны | capture, wait, input, launch |
| `integrity` | какой integrity/elevation profile у runtime token | `GetTokenInformation`, optional `CheckTokenMembership` for admin hint | `degraded` или `blocked` в зависимости от mismatch risk, а не по одному факту admin group | input, clipboard, launch, elevated-window scenarios |
| `uiaccess` | есть ли `uiAccess` и можно ли обходить обычный UIPI barrier | `GetTokenInformation`, application manifest facts | `blocked` для higher-integrity interaction без `uiAccess`; `ready` только при подтверждённом signal | future input / elevated UIA edge-cases |
| `capture_readiness` | может ли runtime честно делать promised capture semantics в этой среде | existing capture backend diagnostics, desktop/session alignment, display identity state | `ready` для current shipped capture baseline; `degraded` если есть fallback-only limits | `windows.capture`, `visual_changed` |
| `uia_readiness` | можно ли честно обещать current UIA observe semantics | UIA client availability, window-scoped acquisition policy, user/session restrictions | `degraded` при user/elevation caveats, `blocked` при отсутствии рабочего UIA path | `windows.uia_snapshot`, `windows.wait`, future `uia_action` |
| `wait_readiness` | можно ли честно обещать `windows.wait` как observe/verify slice | composition from `session_alignment`, `capture_readiness`, `uia_readiness` | derived summary, а не отдельный raw probe | `windows.wait` |
| `input_readiness` | можно ли обещать будущие input semantics без ложного success | composition from `desktop_session`, `session_alignment`, `integrity`, `uiaccess`, focus constraints | в первом этапе чаще `blocked` или `degraded`, пока public tool не shipped | future `windows.input` |
| `clipboard_readiness` | можно ли безопасно обещать future clipboard read/write semantics | composition from session, integrity and desktop access probes | чаще `degraded` или `blocked` до отдельного slice | future `windows.clipboard_*` |
| `launch_readiness` | можно ли честно обещать future `launch_process` / `open_target` without silent elevation or focus assumptions | composition from shell/session/integrity/focus constraints | derived summary | future `windows.launch_process`, `windows.open_target` |

### Warning vs blocked rules

- `warning` публикуется, когда shipped capability остаётся честной, но важны caveats для следующего шага агента: например, `capture_ready`, но `input_readiness=blocked`.
- `blocked` публикуется, когда следующий capability path нельзя обещать без contract lie: например, `SendInput`-style higher-integrity interaction без `uiAccess`.
- один и тот же raw probe может вести к разным domain statuses в зависимости от capability derivation; например, medium-integrity runtime может быть `ready` для capture и `blocked` для future higher-integrity input.

## Public health contract

### Contract principles

- `okno.health` остаётся human-readable public summary, но становится одновременно machine-usable readiness snapshot.
- existing top-level runtime facts не удаляются, чтобы не ломать текущих consumers.
- новый guard snapshot добавляется как расширение, а не как скрытая замена уже существующих полей.
- health tool по-прежнему не должен скрыто менять session state, focus state или tool lifecycle.

### Proposed `HealthResult` evolution

Сохраняются существующие поля:

- `Service`
- `Version`
- `Transport`
- `AuditSchemaVersion`
- `RunId`
- `ArtifactsDirectory`
- `ActiveMonitorCount`
- `DisplayIdentity`
- `ImplementedTools`
- `DeferredTools`

Добавляются новые поля верхнего уровня или вложенного snapshot:

- `SnapshotTimestampUtc`
- `Readiness`
- `GuardNotices`
- `BlockedCapabilities`
- `Warnings`
- `ArtifactPath` или nested diagnostics reference, если для health вводится отдельный JSON artifact

Предпочтительный shape для V1:

- top-level `HealthResult` остаётся summary DTO;
- новый nested `RuntimeReadinessSnapshot` содержит domains, capability summaries, counts и probe provenance;
- flattening-поля `Warnings` и `BlockedCapabilities` остаются короткими индексами для human/agent consumption.

### Proposed DTO set

Минимальный typed набор в `src/WinBridge.Runtime.Contracts/`:

- `RuntimeReadinessSnapshot`
- `ReadinessDomainStatus`
- `CapabilityGuardSummary`
- `GuardReason`
- `GuardSeverity`
- `GuardStatus`
- `ProbeProvenance`

DTO invariants:

- domain status и capability status используют один canonical literal set;
- reasons всегда содержат `code`, `severity`, `message` и `source`;
- public messages не раскрывают внутренние исключения или чувствительные token details;
- tool-level descriptions остаются достаточно стабильными, а vendor/API-specific raw facts прячутся в provenance/detail fields.

### Public result rules

- `okno.health` возвращает success result, если сам snapshot удалось построить, даже когда отдельные capability помечены как `blocked`;
- `isError` для health нужен только при невозможности собрать сам snapshot или при явном invalid runtime state tool-level масштаба;
- reporting-first baseline не меняет автоматически `okno.contract`, `windows.wait`, `windows.capture` и другие tools на уровне execution semantics;
- future tools могут ссылаться на те же status codes и reason codes, но это отдельный rollout.

## Internal runtime guard model

### Source of truth

Нужен единый runtime service, условно:

- `IRuntimeGuardService`
- `RuntimeGuardService`

Ответственность сервиса:

- собрать raw probes;
- построить domain statuses;
- вывести capability summaries;
- нормализовать reason codes/messages;
- вернуть один immutable snapshot для `okno.health` и future runtime consumers.

### Raw probe groups

Первый набор probes:

- `DesktopSessionProbe`
- `SessionAlignmentProbe`
- `IntegrityProbe`
- `UiAccessProbe`
- `CaptureReadinessProbe`
- `UiaReadinessProbe`

Принципы probes:

- probe даёт raw fact + provenance, а не сразу business-friendly summary;
- probe failure конвертируется в `unknown` или `blocked` по явной policy, а не через silent catch-all;
- derived capability summaries не должны читать ОС повторно, если уже есть authoritative raw fact того же вызова.

### Derivation pipeline

Пайплайн V1:

1. Снять raw facts текущего runtime/session/token/UI capability state.
2. Нормализовать их в domain statuses.
3. Построить capability summaries для `capture`, `uia`, `wait`, `input`, `clipboard`, `launch`.
4. Собрать flattened `warnings` и `blockedCapabilities`.
5. При включённом diagnostics path materialize-ить JSON artifact и runtime audit event.

### Enforcement boundary

На первом этапе guard snapshot:

- используется `okno.health` как public reporting surface;
- может читаться internal consumers read-only способом;
- не должен сам по себе запретить уже shipped tool call;
- не должен silently downgrade tool result вне отдельно запланированного contract update.

### Evidence and audit

Предпочтительный observability baseline:

- новый artifact `artifacts/diagnostics/<run_id>/health/<health_id>.json`;
- один итоговый runtime event наподобие `health.runtime.completed` или `guard.runtime.completed`;
- event payload включает counts по `ready/degraded/blocked/unknown`, ключевые blocked capability codes и `artifact_path`;
- `summary.md` и investigation workflow обновляются только если health artifact действительно materialize-ится.

Принятое продуктовое решение для rollout:

- отдельный health artifact и runtime event нужны, но только для сценариев с `warning`, `blocked` или `unknown`;
- Package A это решение только фиксирует и не materialize-ит artifact/event в runtime.

## Integration points

### Question-driven map

| Вопрос | Точка интеграции | Файлы |
| --- | --- | --- |
| Как сейчас выглядит public health? | MCP tool boundary | `src/WinBridge.Server/Tools/AdminTools.cs`, `src/WinBridge.Runtime.Contracts/HealthResult.cs` |
| Где должен жить canonical tool description? | source of truth для public wording | `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs` |
| Где утверждается canonical truth по tool surface? | tool contract | `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` |
| Где уже есть runtime summary facts? | runtime metadata | `src/WinBridge.Runtime/RuntimeInfo.cs` |
| Где уже есть display-related readiness signal? | existing monitor/display diagnostics | existing `WinBridge.Runtime.Windows.Display` services + `DisplayIdentityDiagnostics` |
| Где живёт observability contract? | diagnostics architecture | `docs/architecture/observability.md`, `src/WinBridge.Runtime.Diagnostics/*` |
| Где уже проверяется `okno.health`/`okno.contract`? | integration tests | `tests/WinBridge.Server.IntegrationTests/AdminToolTests.cs` |
| Где фиксировать rollout intent и sequencing? | roadmap + exec-plan | `docs/product/okno-roadmap.md`, этот exec-plan |

### File-level integration map

- `src/WinBridge.Runtime.Contracts/HealthResult.cs`
  - расширить публичный health DTO без удаления текущих top-level fields.
- `src/WinBridge.Runtime.Contracts/`
  - добавить typed readiness/guard DTO files для domains, capability summaries и reasons.
- `src/WinBridge.Server/Tools/AdminTools.cs`
  - перевести `okno.health` на новый guard service и materialized readiness snapshot.
- `src/WinBridge.Runtime/RuntimeInfo.cs`
  - оставить runtime identity source и при необходимости расширить не-OS facts, полезные для health snapshot.
- `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs`
  - обновить описание `okno.health`.
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
  - при необходимости обновить contract notes, но не вводить новый tool.
- `src/WinBridge.Runtime.Diagnostics/`
  - добавить audit/event/artifact helpers для health guard snapshot.
- `src/WinBridge.Runtime.Windows.Display/`
  - переиспользовать существующий display identity signal как вход в `capture_readiness`, а не дублировать probe.
- новый runtime guard area
  - предпочтительно отдельный cross-cutting проект или namespace для `IRuntimeGuardService`, raw probes и derivation policy, чтобы не перегружать `WinBridge.Runtime` composition root.
- `tests/WinBridge.Runtime.Tests/`
  - L1 tests на status mapping, reason codes и capability derivation.
- `tests/WinBridge.Server.IntegrationTests/AdminToolTests.cs`
  - L2 tests на public `HealthResult`, snake_case literals и `okno.health` result shape.
- `scripts/smoke.ps1`
  - при необходимости добавить smoke-assertions на `okno.health` readiness payload и artifact presence.
- `docs/product/okno-roadmap.md`
  - обновить progress после реализации пакетов.
- `docs/architecture/observability.md`
  - описать health artifact/event, если они будут materialized.
- `docs/generated/project-interfaces.md`
  - обновить notes для `okno.health`.

## Delivery packages

### Package A - boundary + contract shape

Что входит:

- зафиксировать reporting-first boundary;
- определить canonical DTO shape для domains, capability summaries и reasons;
- решить, какие поля остаются top-level в `HealthResult`, а какие уходят в nested snapshot;
- определить canonical status/reason literal set;
- обновить `ToolDescriptions.OknoHealthTool` и contract notes без изменения tool lifecycle.

Критерий завершения:

- другой инженер может открыть `HealthResult.cs`, `AdminTools.cs` и новые contracts и понять итоговую public schema без догадок.

Текущий статус:

- Package A закрыт минимальным conservative rollout: `HealthResult` расширен typed readiness snapshot-ом, `okno.health` публикует explicit `unknown`/`blocked` guard surface без raw probes и без hidden enforcement.
- Guard service, probe derivation, artifact/event materialization и handler-level rollout для реальных readiness facts остаются в Packages B/C.

### Package B - raw probes + domain mapping

Что входит:

- реализовать probes для `desktop_session`, `session_alignment`, `integrity`, `uiaccess`;
- определить policy mapping raw facts -> `ready/degraded/blocked/unknown`;
- переиспользовать existing display diagnostics для `capture_readiness`;
- ввести `uia_readiness` как honest observe-path summary, а не обещание full action semantics.

Критерий завершения:

- health snapshot различает `warning` и `blocked` на основе authoritative probes, а не через hardcoded prose.

Текущий статус:

- добавлен отдельный `WinBridge.Runtime.Guards` project с `IRuntimeGuardService`, `RuntimeGuardPolicy` и win32 probe-platform;
- `AdminTools.Health()` переведён на guard service и больше не строит synthetic readiness snapshot вручную;
- raw probes и mapping для `desktop_session`, `session_alignment`, `integrity`, `uiaccess` реализованы, а observe/deferred capability lists перенесены в guard-layer без rollout Package C.

Особое внимание после Package A:

- вынести domain/capability lists и synthetic `unknown` / `blocked` builders из `AdminTools` в единый guard source of truth;
- заменить общий top-level warning и placeholder-reasons на probe-backed reasons per domain/capability;
- не ломать conservative contract Package A: пока probes не доказали `ready` или `degraded`, status не должен оптимистично повышаться.

### Package C - capability summaries + health handler rollout

Что входит:

- построить derived summaries для `capture`, `uia`, `wait`, `input`, `clipboard`, `launch`;
- подключить новый guard service в `AdminTools.Health()`;
- сохранить backwards-friendly top-level summary fields;
- при необходимости добавить health artifact/event и связать их с public result.

Критерий завершения:

- `okno.health` становится machine-usable readiness snapshot, а существующие health consumers не теряют базовый summary.

Особое внимание после Package B:

- использовать `IRuntimeGuardService` как единственный source of truth и не возвращать synthetic mapping обратно в `AdminTools`;
- заменить временные `assessment_not_implemented` capability-reasons для `capture`, `uia`, `wait` на probe-backed capability summaries, а не наслаивать вторую логику поверх domain statuses;
- строить `input`, `clipboard` и `launch` summaries от domain statuses и уже существующих runtime facts консервативно, без hidden enforcement и без premature artifact/event rollout.

Статус по факту `2026-03-26`:

- capability derivation для `capture`, `uia`, `wait`, `input`, `clipboard`, `launch` переведён на runtime facts и domain statuses внутри guard layer;
- `okno.health` продолжает читать только `IRuntimeGuardService`, а `Warnings` / `BlockedCapabilities` больше не опираются на observe-placeholders;
- узкие L1/L2 tests для runtime guard и `AdminTools` добавлены, но bundled checklist `L1/L2/L3` ниже не закрывается до полного Package D.

### Package D - tests + docs sync

Что входит:

- L1 tests на domain mapping и capability derivation;
- L2 tests на public health payload и canonical literals;
- L3 smoke update, если health snapshot materializes artifact/runtime event;
- sync roadmap, observability doc, generated interfaces map и changelog.

Критерий завершения:

- runtime, docs и smoke expectations синхронизированы и не расходятся по public health semantics.

Особое внимание после Package C:

- закрывать docs/test wave именно по `okno.health + runtime guard layer`: roadmap, observability, generated interfaces, smoke и checklist, а не считать sidecar README/plugin апдейты эквивалентом полного docs sync;
- финальная verification wave должна подтверждать текущую capability derivation из guard-layer, а не прежний placeholder baseline;
- если рядом идут plugin/runtime packaging изменения, не смешивать их с критерием завершения этого workstream и не подменять ими L3/health-docs evidence.

## Test ladder

### L1 - contract and policy tests

- `HealthResult` serializer shape и snake_case literals;
- domain status mapping для `desktop_session`, `session_alignment`, `integrity`, `uiaccess`;
- capability derivation tests: `capture` может быть `ready`, пока `input` остаётся `blocked`;
- regression tests на `unknown`/probe-failure handling без silent downgrade в `ready`.

### L2 - server integration tests

- `okno.health` возвращает расширенный `HealthResult` через `AdminTools`;
- public payload сохраняет legacy summary fields;
- `okno.contract` и `project-interfaces` не расходятся по описанию health scope;
- `blocked` capability не превращает сам `okno.health` в ошибку, если snapshot собран успешно.

### L3 - smoke

- live `okno.health` присутствует в `tools/list`;
- `tools/call okno.health` возвращает readiness snapshot;
- если health artifact введён, smoke проверяет `artifactPath` и существование файла;
- smoke не должен требовать elevated/multi-user special-case среды как обязательного предусловия.

## Docs sync

- `docs/product/okno-roadmap.md` — статус workstream и его место после shipped `windows.wait`.
- `docs/architecture/observability.md` — health artifact/event, если они materialize-ятся.
- `docs/generated/project-interfaces.md` — notes для `okno.health` как readiness snapshot.
- `docs/CHANGELOG.md` — инженерно значимое изменение public/admin baseline.
- при необходимости `docs/architecture/index.md` — короткое упоминание guard layer как cross-cutting baseline между observe и future act slices.

## Rollback

- rollback допускается без удаления `okno.health` как tool;
- минимальный rollback path: оставить только existing top-level summary fields и скрыть новый readiness snapshot за внутренним feature step до повторной доработки;
- если artifact/event часть окажется шумной, её можно откатить отдельно от public DTO, сохранив reporting-first snapshot;
- enforcement hooks для future tools не должны входить в тот же rollback surface, пока они не введены отдельным change.

## Implementation checklist

- [x] Boundary зафиксирован как расширение `okno.health`, а не новый tool.
- [x] Non-goals исключают input/clipboard/launch implementation из этого workstream.
- [x] Выбран canonical DTO set для readiness domains, capability summaries и reasons.
- [x] Для каждого domain определён authoritative probe source.
- [x] Явно разведены `warning` и `blocked`.
- [x] Решено, materialize-ится ли отдельный health artifact и runtime event.
- [x] `AdminTools.Health()` переведён на единый guard service.
- [x] `ToolDescriptions.OknoHealthTool` отражает readiness snapshot, а не старое узкое описание.
- [ ] L1/L2/L3 test ladder покрывает domains, capability derivation и public payload.
- [ ] Docs sync включает roadmap, observability, generated interfaces и changelog.
- [x] В первой поставке нет скрытого enforcement для already shipped tools.
