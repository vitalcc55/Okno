# ExecPlan: windows.uia_snapshot

Статус: active
Создан: 2026-03-18
Обновлён: 2026-03-18

## Goal

Зафиксировать и доказать только `Package A: contract + target policy` для capability slice `windows.uia_snapshot`.

Что именно закрывает этот пакет:

- docs-driven target policy для `explicit -> attached -> active`;
- typed DTO groundwork для snapshot request/result/element;
- typed shell seam для target-resolution policy;
- L1/L2 tests на precedence, stale-policy и honest deferred surface.

Что этот пакет намеренно **не** делает:

- concrete `Win32UiAutomationService`;
- DI registration;
- public MCP handler rollout;
- `ToolLifecycle.Implemented`;
- audit/evidence rollout для нового tool;
- smoke/generated-docs rollout;
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

Статус: `next`

Отложено:

- concrete UIA service;
- `ElementFromHandle` / build-cache / traversal implementation;
- artifact writing;
- audit payload enrichment;
- DI wiring.

### Package C — server rollout + smoke + generated docs

Статус: `pending`

Отложено:

- public `WindowTools` handler;
- `CallToolResult` / `structuredContent` path;
- lifecycle switch в `Implemented`;
- smoke scenario;
- `refresh-generated-docs.ps1`.

## Current repo state

- `windows.uia_snapshot` уже объявлен в `ToolNames`, но остаётся `Deferred` в `ToolContractManifest`.
- Public handler в `WindowTools` по-прежнему возвращает `DeferredToolResult`; Package A не меняет этот факт.
- Текущий репозиторий уже содержит pre-existing generated/bootstrap diffs вне объёма этого пакета; они не являются Package A truth и не должны откатываться в этом цикле.
- После Package A в repo появляются typed contracts и policy seam, но tool всё ещё честно считается не реализованным.

## Official constraints

- `GetForegroundWindow` возвращает окно, с которым пользователь сейчас работает; это authoritative source для cross-process active window semantics в текущем V1.
- `GetActiveWindow` и `GetFocus` не подходят как meaning `active` для `windows.uia_snapshot`, потому что они привязаны к queue/thread semantics вызывающего контекста, а не к global top-level active target.
- Для cross-thread GUI focus diagnostics существует `GetGUIThreadInfo`, но Package A не строит новый focus API; он только фиксирует, что `active` не равен focused child element.
- UIA root acquisition для будущей реализации должен строиться от `IUIAutomation::ElementFromHandle`.
- `IUIAutomationCacheRequest` и `ElementFromHandleBuildCache` считаются valid future optimization path, но не меняют Package A public surface.
- Default semantic tree для shipped V1 остаётся `control view`, а не `raw view`.
- `windows.uia_snapshot` не должен скрыто активировать окно; `SetForegroundWindow` restrictions остаются прямым запретом на hidden activation fallback.
- MCP tools требуют honest `isError` semantics, но Package A не публикует новый tool handler.
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

Package A меняет только следующие слои:

- `src/WinBridge.Runtime.Contracts/`
- `src/WinBridge.Runtime.Windows.UIA/`
- `src/WinBridge.Runtime.Windows.Shell/`
- `tests/WinBridge.Runtime.Tests/`
- `tests/WinBridge.Server.IntegrationTests/`
- `docs/exec-plans/active/windows-uia-snapshot.md`
- `docs/product/okno-spec.md`
- `docs/product/okno-roadmap.md`
- `docs/CHANGELOG.md`

Package A осознанно не меняет:

- `src/WinBridge.Server/Tools/WindowTools.cs`
- `src/WinBridge.Runtime/ServiceCollectionExtensions.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- generated docs/exporters;
- `docs/architecture/observability.md`.

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

### L2. Honest deferred surface

Обязательные сценарии:

- `WindowTools.UiaSnapshot(...)` всё ещё возвращает `DeferredToolResult` / `unsupported`;
- `ToolContractManifest` всё ещё держит tool в `Deferred`;
- tests не притворяются, что есть public structured snapshot payload или implemented handler.

### L3. Not in Package A

- реальный smoke;
- end-to-end MCP snapshot flow;
- artifact assertions.

## Docs sync policy

В рамках Package A синхронизируются только:

- этот exec-plan;
- `okno-spec`, если wording по `active/selected window` слишком vague;
- `okno-roadmap`, если wording по stage 6 расходится с `explicit -> attached -> active`;
- `CHANGELOG`.

Не синхронизируются в этом пакете:

- generated docs;
- observability docs;
- exporter output.

## Checklist

- [x] Зафиксирован Package A как единственный scope текущего цикла.
- [x] Зафиксирован precedence `explicit -> attached -> active`.
- [x] Зафиксировано authoritative meaning `active = foreground top-level window`.
- [x] Запрещён silent fallback из stale explicit/attached target в active path.
- [x] Typed contracts и target policy seam разрешены без premature public rollout.
- [x] `windows.uia_snapshot` остаётся честным `Deferred/unsupported` после Package A.
