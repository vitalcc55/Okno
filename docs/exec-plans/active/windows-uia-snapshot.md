# ExecPlan: windows.uia_snapshot

Статус: done
Создан: 2026-03-18
Обновлён: 2026-03-19

## Goal

Зафиксировать staged delivery для capability slice `windows.uia_snapshot`, где:

- `Package A: contract + target policy` уже завершён;
- `Package B: runtime service + evidence` уже завершён;
- текущий цикл закрывает `Package C: server rollout + smoke + generated docs`.

Что именно закрывает текущий пакет:

- public `WindowTools` handler для `windows.uia_snapshot`;
- `CallToolResult` / `structuredContent` public MCP shape;
- lifecycle switch в `Implemented` и live tool contract;
- end-to-end smoke scenario и generated docs refresh.

Что этот пакет намеренно **не** делает:

- commit/push.

## Delivery packages

### Package A — contract + target policy

Статус: `done`

В объёме текущего изменения:

- фиксируем authoritative meaning `active`;
- добавляем `UiaSnapshot*` DTO;
- добавляем `UiaSnapshotTargetResolution` и failure/source literals;
- расширяем shell seam до `ResolveUiaSnapshotTarget(...)`;
- держим public surface `windows.uia_snapshot` в честном `Deferred/unsupported`.

### Package B — runtime service + evidence

Статус: `done`

В объёме текущего изменения:

- добавлен concrete `Win32UiAutomationService` c managed `System.Windows.Automation` backend;
- root acquisition строится только от `AutomationElement.FromHandle(hwnd)` на dedicated MTA thread;
- traversal ограничен `control view`, `Depth` и `MaxNodes`, `Truncated` теперь означает только реальное clipping по budget, а `DepthBoundaryReached` отдельно сигнализирует о достижении depth boundary без child probing;
- runtime пишет JSON artifact в `artifacts/diagnostics/<run_id>/uia/` и capability-specific audit event `uia.snapshot.runtime.completed` c `requested_max_nodes`, `depth_boundary_reached`, `node_budget_boundary_reached`, `failure_stage` и `diagnostic_artifact_path`;
- execution boundary для production path вынесен в isolated worker process, чтобы timeout утилизировал stuck UIA worker вместе с процессом, а in-proc MTA runner оставался только cooperative utility без ложного hard-timeout обещания;
- host-specific registration boundary вынесена в отдельный проект `WinBridge.Runtime.Windows.UIA.Hosting`, а companion worker включён в project graph hosting-слоя как non-copying sidecar dependency; build и publish path stage-ят build/published worker artifacts раздельно, без repo-layout fallback; runtime launch boundary теперь понимает и `worker.exe`, и apphost-less `worker.dll` через текущий `dotnet` host, так что publish semantics больше не протекают напрямую в runtime launcher contract; сам hosting entry point остаётся self-contained и явно принимает diagnostics context (`contentRootPath`, `environmentName`), а не рассчитывает на скрытые prereqs из `WinBridge.Runtime`;
- public deferred handler и manifest lifecycle намеренно не менялись.

### Package C — server rollout + smoke + generated docs

Статус: `done`

Закрыто:

- public `WindowTools` handler;
- `CallToolResult` / `structuredContent` path;
- lifecycle switch в `Implemented`;
- smoke scenario;
- `refresh-generated-docs.ps1`.

## Current repo state

- `windows.uia_snapshot` объявлен в `ToolNames` и публикуется как `Implemented` в `ToolContractManifest`.
- Public handler в `WindowTools` использует runtime UIA service и возвращает live `CallToolResult`.
- Текущий репозиторий уже содержит pre-existing generated/bootstrap diffs вне объёма этого пакета; они не являются truth текущего пакета и не должны откатываться в этом цикле.
- Slice закрыт как shipped public capability без преждевременного захода в `windows.uia_action`, `windows.wait` и `windows.input`.

## Official constraints

- `GetForegroundWindow` возвращает окно, с которым пользователь сейчас работает; это authoritative source для cross-process active window semantics в текущем V1.
- `GetActiveWindow` и `GetFocus` не подходят как meaning `active` для `windows.uia_snapshot`, потому что они привязаны к queue/thread semantics вызывающего контекста, а не к global top-level active target.
- Для cross-thread GUI focus diagnostics существует `GetGUIThreadInfo`, но Package A не строит новый focus API; он только фиксирует, что `active` не равен focused child element.
- UIA root acquisition для будущей реализации должен строиться от `IUIAutomation::ElementFromHandle`.
- `IUIAutomationCacheRequest` уже используется в runtime path этого пакета; `ElementFromHandleBuildCache` остаётся допустимой follow-up optimization/hardening option, если позже понадобится более агрессивный cache-first path.
- Default semantic tree для shipped V1 остаётся `control view`, а не `raw view`.
- `windows.uia_snapshot` не должен скрыто активировать окно; `SetForegroundWindow` restrictions остаются прямым запретом на hidden activation fallback.
- MCP tools требуют honest `isError` semantics, но Package B по-прежнему не публикует новый tool handler.
- MCP authorization note для `STDIO` не влечёт отдельного auth rollout в этом пакете.

## Package A design contract

### Boundary

- `windows.uia_snapshot` в этом пакете остаётся declared/deferred tool.
- Host/server boundary не меняется.
- Runtime/UIA boundary становится typed, но остаётся seam-only.
- Target policy фиксируется в shell seam, а не в `WindowTools`.

### Target model

- Explicit target: `hwnd`, если он задан.
- `explicitHwnd <= 0`, если он явно передан в UIA snapshot path, считается invalid explicit target и должен приводить к явному `stale_explicit_target`, а не к fallback в attached/active. Это правило не меняет shared explicit-target semantics существующих `focus/capture` tools.
- Attached target: attached window текущей session, если explicit target не задан.
- Active target: current foreground top-level window только если explicit и attached target отсутствуют.
- Precedence: `explicit -> attached -> active`.

Правила ошибок:

- `missing_target`: explicit target не задан, attached target отсутствует и foreground `HWND` либо недоступен, либо уже не мапится в live top-level окно.
- `stale_explicit_target`: caller передал explicit `HWND`, но live window по нему больше не существует. Silent fallback в attached/active запрещён.
- `stale_attached_target`: attached window больше не проходит stable-identity revalidation. Silent fallback в active запрещён.
- `ambiguous_active_target`: foreground `HWND` мапится более чем в один live candidate.

Authoritative meaning `active`:

- это foreground top-level window, а не focused child element;
- это не `GetActiveWindow` calling thread;
- это не “last used app window” и не эвристика по title/process.
- active candidate должен определяться из одного live window snapshot, а не из двух раздельных чтений foreground/window inventory.

### Identity model

- Explicit path в Package A резолвится только по live `HWND`; дополнительную historical identity для caller-provided explicit `HWND` пока не вводим.
- Attached path использует существующую stable-identity revalidation (`ProcessId` + `ThreadId` + `ClassName`).
- Child UIA element identity остаётся future-runtime concern; Package A лишь вводит typed DTO shape.

Invariant:
- shell/resolution слой выбирает target для runtime;
- runtime/result слой публикует metadata окна из фактического snapshot path;
- `resolved target` и `observed window` нельзя сливать в один stale carrier object.

### Contract-first types

Новые contracts этого пакета:

- `UiaSnapshotRequest`
- `UiaSnapshotResult`
- `UiaElementSnapshot`
- `UiaSnapshotStatusValues`
- `UiaSnapshotViewValues`
- `UiaSnapshotTargetSourceValues`
- `UiaSnapshotTargetFailureValues`
- `UiaSnapshotTargetResolution`

Typed UIA seam после Package A:

- `Task<UiaSnapshotResult> SnapshotAsync(WindowDescriptor targetWindow, UiaSnapshotRequest request, CancellationToken cancellationToken);`

## Integration points

Package A/B меняют только следующие слои:

- `src/WinBridge.Runtime.Contracts/`
- `src/WinBridge.Runtime.Windows.UIA/`
- `src/WinBridge.Runtime.Diagnostics/`
- `src/WinBridge.Runtime.Windows.Shell/`
- `src/WinBridge.Runtime/`
- `tests/WinBridge.Runtime.Tests/`
- `tests/WinBridge.Server.IntegrationTests/`
- `docs/exec-plans/active/windows-uia-snapshot.md`
- `docs/architecture/observability.md`
- `docs/CHANGELOG.md`

Package A/B осознанно не меняют:

- `src/WinBridge.Server/Tools/WindowTools.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- generated docs/exporters;
- `docs/product/okno-spec.md`;
- `docs/product/okno-roadmap.md`.

## Test ladder for Package A

### L1. Runtime / contract tests

Обязательные сценарии:

- request/result defaults;
- explicit wins over attached/active;
- stale explicit does not fall through;
- attached path works when explicit is absent;
- stale attached does not fall through;
- active path works only for one foreground top-level window;
- missing foreground => `missing_target`;
- foreground `HWND` без live match => `missing_target`;
- foreground `HWND` с несколькими candidates => `ambiguous_active_target`.

### L2. Public handler + contract surface

Обязательные сценарии:

- `WindowTools.UiaSnapshot(...)` возвращает live `CallToolResult` c `structuredContent` и одним `TextContentBlock`;
- `ToolContractManifest` публикует tool как `Implemented` / `SmokeRequired`;
- `okno.contract`, exporter и `tools/list` не расходятся с реальным handler semantics.

### L3. Smoke / evidence

- реальный smoke;
- end-to-end MCP snapshot flow;
- artifact assertions.

## Docs sync policy

В рамках Package C синхронизируются:

- этот exec-plan;
- `observability`, `okno-spec`, `okno-roadmap` и `CHANGELOG`;
- generated docs и exporter output.

## Checklist

- [x] Зафиксирован Package A как единственный scope текущего цикла.
- [x] Зафиксирован precedence `explicit -> attached -> active`.
- [x] Зафиксировано authoritative meaning `active = foreground top-level window`.
- [x] Запрещён silent fallback из stale explicit/attached target в active path.
- [x] Typed contracts и target policy seam разрешены без premature public rollout.
- [x] `windows.uia_snapshot` остаётся честным `Deferred/unsupported` после Package A.
- [x] Package B добавил concrete runtime UIA service без hidden activation side effects.
- [x] `ElementFromHandle` используется как canonical root acquisition path.
- [x] `Depth` / `MaxNodes` / `Truncated` теперь enforced в runtime layer без ложноположительного truncation на depth boundary; `node_budget_boundary_reached` отдельно выражает strict no-probe budget boundary без перегрузки `Truncated`.
- [x] JSON artifact пишется в `artifacts/diagnostics/<run_id>/uia/`.
- [x] Audit event `uia.snapshot.runtime.completed` добавлен без public handler rollout и несёт `requested_max_nodes`, `depth_boundary_reached`, `node_budget_boundary_reached`, `failure_stage` и `diagnostic_artifact_path`.
- [x] Timeout boundary для production path изолирован отдельным worker process.
- [x] DI wiring добавлен как optional host-specific boundary через `WinBridge.Runtime.Windows.UIA.Hosting`, но `WindowTools` и `ToolContractManifest` по-прежнему deferred.
- [x] Runtime и deferred regression tests покрывают Package B boundary.
- [x] Package C выполнил public handler, `structuredContent`, lifecycle switch, smoke и generated docs.
- [x] Server runtime contract осознанно обновлён под live `windows.uia_snapshot` и больше не притворяется pre-rollout boundary.
- [x] Residual scope остаётся ограничен только соседними capability slices: `windows.uia_action`, `windows.wait`, `windows.input`.
