# ExecPlan: windows.uia_snapshot

Статус: active
Создан: 2026-03-18

## Goal

Добавить `windows.uia_snapshot` как honest MCP observe tool для active/attached/explicit window без регрессии текущего `windows.list_windows` / `windows.attach_window` / `windows.activate_window` / `windows.capture` baseline.

Полезный минимальный outcome:

- tool принимает explicit `hwnd` или использует attached window;
- tool возвращает typed semantic tree snapshot выбранного окна;
- tool не делает hidden focus/input/action side effects;
- tool оставляет audit/evidence и проходит L1/L2/L3.

## Non-goals

- `windows.uia_action` и любые semantic actions по element id;
- расширение `windows.input`, `SendInput`, mouse/keyboard orchestration;
- OCR, screen parsing, image-to-structure fallback;
- event subscriptions, watchers, daemon/background companion;
- browser/DOM-specific semantics;
- broad environment/safety layer beyond hooks, нужных для честного `unsupported`/`failed`;
- silent fallback в `windows.capture`, shell automation или activation path ради "успешного" snapshot.

## Current repo state

- `docs/product/okno-roadmap.md` фиксирует `windows.uia_snapshot` как следующий shipped capability slice после текущего `observe/window` baseline.
- `docs/generated/project-interfaces.md` показывает `windows.uia_snapshot` как declared/deferred tool с текущим outcome `unsupported`.
- `src/WinBridge.Runtime.Tooling/ToolNames.cs` уже объявляет `ToolNames.WindowsUiaSnapshot`.
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` держит tool в `ToolLifecycle.Deferred`.
- `src/WinBridge.Server/Tools/WindowTools.cs` пока возвращает `DeferredToolResult UiaSnapshot(int depth = 3, string? filtersJson = null)`.
- `src/WinBridge.Runtime.Windows.UIA/IUiAutomationService.cs` существует только как пустой seam.
- `src/WinBridge.Runtime/ServiceCollectionExtensions.cs` пока не регистрирует UIA service.
- `src/WinBridge.Runtime.Windows.Shell/IWindowTargetResolver.cs` и `WindowTargetResolver.cs` уже дают проверенный explicit/attached window resolution, который нужно переиспользовать.
- `tests/WinBridge.Runtime.Tests`, `tests/WinBridge.Server.IntegrationTests` и `tests/WinBridge.SmokeWindowHost` уже дают готовый L1/L2/L3 harness; отдельного UIA coverage пока нет.

## Official constraints

Ниже перечислены external constraints, которые нужно считать обязательными, а не вкусовыми:

- UI Automation представляет UI как tree с desktop root и application windows как direct children; tree динамический и может содержать тысячи элементов, поэтому обход должен быть bounded, а не "все descendants без лимитов". Источник: [UI Automation Specification](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification), [UI Automation Tree Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview).
- Для target window primary anchor должен быть `IUIAutomation::ElementFromHandle`, который получает UI Automation element для заданного `HWND`. Источник: [IUIAutomation::ElementFromHandle](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfromhandle).
- UIA cache requests существуют специально для batched property/pattern reads и снижения cross-process cost; V1 должен использовать narrow cache request вместо большого числа ad-hoc live reads. Источник: [IUIAutomationCacheRequest](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationcacherequest).
- UI Automation tree имеет `raw`, `control` и `content` views; для automated testing наиболее уместен `control view`, а `raw view` слишком шумный для shipped default. Это repo decision, вытекающее из [UI Automation Tree Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview).
- Control patterns выражают capability, могут комбинироваться и в некоторых случаях supportятся динамически; snapshot должен сообщать supported patterns, а не обещать, что pattern availability уже равна готовому action contract. Источник: [UI Automation Control Patterns Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview).
- Control type задает canonical control identity и связан с required patterns/properties/tree semantics; result должен возвращать как минимум control type id/name и localized control type. Источник: [UI Automation Control Types Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controltypesoverview).
- `SetForegroundWindow` сильно ограничен Windows policy и может быть отвергнут даже при формальном соблюдении условий; `windows.uia_snapshot` не должен скрыто активировать окно ради success path. Источник: [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).
- `SendInput` subject to UIPI и может fail без явного signal о причине; это прямой аргумент не тащить input fallback в snapshot slice. Источник: [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
- `windows.capture` уже опирается на `IGraphicsCaptureItemInterop::CreateForWindow`; `uia_snapshot` не должен ломать или подменять существующий observe/capture contract. Источник: [IGraphicsCaptureItemInterop::CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow).
- MCP tools обязаны отдавать честный `isError`, structured result можно и нужно публиковать через `structuredContent`, а для backwards compatibility structured payload стоит дублировать в `TextContent`. Источник: [MCP Tools](https://modelcontextprotocol.io/specification/draft/server/tools).
- MCP authorization note для текущего repo не переносится на `STDIO`: HTTP OAuth flow сюда не применяется, credentials должны оставаться environment-based. Источник: [MCP Authorization](https://modelcontextprotocol.io/specification/2025-03-26/basic/authorization).

Дополнительное требование к реализации active-path:

- перед кодом отдельно перепроверить official Windows docs и API для active window resolution; exact runtime meaning `active` нельзя зафиксировать эвристикой "как сейчас кажется удобным".
- рабочая contract-гипотеза для этого плана: `active` означает top-level foreground window, а не focused child element внутри произвольного процесса; если official docs потребуют другой authoritative path, это решение должно быть явно отражено в plan/report/docs.

## Design contract

### Intent

- Закрыть первый semantic observe use-case поверх существующего window/session baseline.
- Дать модели и человеку не screenshot, а typed tree окна с именами, control types и capability hints.
- Подготовить next slice `windows.wait`, не обещая пока action semantics.

### Boundary

- Tool class: `observe`.
- Host responsibility: input validation, explicit/attached target precedence, MCP `isError` semantics, text + structured payload.
- Runtime responsibility: UIA root acquisition, bounded traversal, property/pattern extraction, evidence artifact, tool-level failure reasons.
- Primary OS API path: COM UI Automation client (`ElementFromHandle` + cache request + tree walk).
- Existing capture/session services остаются независимыми и не должны становиться fallback path для snapshot semantics.

### Target model

- Explicit target: `hwnd` parameter.
- Implicit attached target: attached window текущей session. Используется, если `hwnd` не передан.
- Implicit active target: текущий active/foreground top-level window. Используется только если `hwnd` не передан и в session нет attached window.
- Target precedence: `explicit hwnd` -> `attached window` -> `active window`.
- Missing target: честный `failed` с `isError=true`.
- Stale explicit target: честный `failed`; если caller указал `hwnd`, runtime не имеет права молча переключиться на attached или active window.
- Stale attached target: честный `failed`; reuse старого `HWND` без identity re-check запрещен, и silent fallback из stale attached в active path запрещён.
- Ambiguous active target: если runtime не может доказуемо определить один live top-level active window, tool должен вернуть `failed` или `unsupported`, а не выбирать "наиболее похожее" окно.

### Identity model

- Window identity reuse: existing `WindowTargetResolver.ResolveExplicitOrAttachedWindow(...)` + `WindowIdentityValidator`.
- Root element identity: selected live window `HWND`.
- Element identity inside snapshot: runtime-generated path id вида `hwnd:<hwnd>/n0/n3/...`; этот id stable only within one snapshot artifact/result.
- `AutomationId`, provider runtime id и native window handle считаются helpful metadata, но не promised durable identity across runs.
- Mutable metadata: `name`, `value`, `isOffscreen`, `hasKeyboardFocus`, bounds, supported patterns.
- If provider cannot expose strong child identity, element все равно можно вернуть, но только с snapshot-local `elementId`.

### Success / error / fallback

- Success:
- root window live and identity-validated;
- UIA root acquired for this window;
- snapshot built within configured depth/node budget;
- result returns typed root node and evidence path.
- Explicit error:
- no target;
- target stale/not found;
- UIA unavailable in current environment;
- root acquisition failed for explicit/attached window;
- traversal/caching contract violated.
- Allowed fallback:
- from build-cache path to live property reads only if semantic target remains the same window and result marks `acquisitionMode`;
- active path разрешён только как последний target-resolution step после отсутствия explicit и attached target, а не как fallback после stale explicit/attached target.
- Forbidden fallback:
- auto-focus/auto-restore window;
- OCR or capture-based structure synthesis;
- shell/input/action emulation;
- silent switch from stale attached window to active window или "какое-то похожее" live window;
- silent switch from explicit `hwnd` на attached или active window;
- подмена `active window` focused child element или last-used app window без documented official API policy.
- False success policy:
- better `failed` or `unsupported` than shallow tree for wrong window.

### Effect model

- Tool reads live UIA state.
- Tool does not mutate session state.
- Tool does not call focus/activate/input APIs.
- Tool writes evidence artifact to diagnostics directory.
- Tool writes normal audit events through existing `AuditLog`.

### Evidence

- `tool.invocation.started/completed` must include `hwnd`, requested depth, node count, acquisition mode, truncated flag and artifact path.
- Full serialized snapshot must be written to `artifacts/diagnostics/<run_id>/uia/<artifact>.json`.
- `summary.md` must mention successful or failed `windows.uia_snapshot` invocation in the same style as other tools.
- Smoke must prove both MCP payload usefulness and on-disk evidence.

### V1 request/result shape

Repo decision for V1: заменить vague `filtersJson` placeholder на typed request contract.

- `UiaSnapshotRequest`
- `long? Hwnd`
- `int Depth = 2`
- `int MaxNodes = 256`
- `UiaSnapshotResult`
- `string Status`
- `string? Reason`
- `WindowDescriptor? Window`
- `string View = "control"`
- `int RequestedDepth`
- `int RealizedDepth`
- `int NodeCount`
- `bool Truncated`
- `string AcquisitionMode`
- `string? ArtifactPath`
- `DateTimeOffset CapturedAtUtc`
- `UiaElementSnapshot? Root`
- `SessionSnapshot Session`
- `UiaElementSnapshot`
- `string ElementId`
- `string? ParentElementId`
- `int Depth`
- `int Ordinal`
- `string? Name`
- `string? AutomationId`
- `string? ClassName`
- `string? FrameworkId`
- `string ControlType`
- `int ControlTypeId`
- `string? LocalizedControlType`
- `bool IsControlElement`
- `bool IsContentElement`
- `bool IsEnabled`
- `bool IsOffscreen`
- `bool HasKeyboardFocus`
- `string[] Patterns`
- `string? Value`
- `Bounds? BoundingRectangle`
- `long? NativeWindowHandle`
- `UiaElementSnapshot[] Children`

Замечание по `BoundingRectangle`: это только raw provider metadata для diagnostics/inspection. Его нельзя объявлять authoritative hit-testing contract для будущего input slice без отдельной design work.

## Integration points

- Target resolution reuse:
- `src/WinBridge.Runtime.Windows.Shell/IWindowTargetResolver.cs`
- `src/WinBridge.Runtime.Windows.Shell/WindowTargetResolver.cs`
- Runtime contract DTOs:
- new files in `src/WinBridge.Runtime.Contracts/`
- Runtime service seam and implementation:
- `src/WinBridge.Runtime.Windows.UIA/IUiAutomationService.cs`
- new `src/WinBridge.Runtime.Windows.UIA/*.cs`
- DI:
- `src/WinBridge.Runtime/ServiceCollectionExtensions.cs`
- MCP tool handler and `isError` semantics:
- `src/WinBridge.Server/Tools/WindowTools.cs`
- Tool descriptions / contract manifest / exporter:
- `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`
- Audit/evidence:
- `src/WinBridge.Runtime.Diagnostics/AuditLog.cs`
- maybe small helper under `src/WinBridge.Runtime.Diagnostics/` or `src/WinBridge.Runtime.Windows.UIA/` for artifact naming
- Unit tests:
- `tests/WinBridge.Runtime.Tests/*`
- Server integration tests and doubles:
- `tests/WinBridge.Server.IntegrationTests/WindowToolTestDoubles.cs`
- `tests/WinBridge.Server.IntegrationTests/*`
- Real smoke host and smoke runner:
- `tests/WinBridge.SmokeWindowHost/Program.cs`
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- `scripts/smoke.ps1`
- Docs/generated:
- `docs/architecture/observability.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/project-interfaces.json`
- `docs/generated/commands.md`
- `docs/generated/test-matrix.md`
- `docs/product/okno-roadmap.md`
- `docs/CHANGELOG.md`

## DTO/contract changes

### Contracts project

- добавить `src/WinBridge.Runtime.Contracts/UiaSnapshotRequest.cs`;
- добавить `src/WinBridge.Runtime.Contracts/UiaSnapshotResult.cs`;
- добавить `src/WinBridge.Runtime.Contracts/UiaElementSnapshot.cs`;
- при необходимости добавить `src/WinBridge.Runtime.Contracts/UiaSnapshotStatusValues.cs` и/или `UiaSnapshotViewValues.cs`, если строковые literals начнут повторяться.

### Server tool surface

- заменить deferred signature `UiaSnapshot(int depth = 3, string? filtersJson = null)` на typed public surface без opaque JSON-фильтров;
- рекомендуемая MCP shape:
- `windows.uia_snapshot(hwnd?: long, depth?: int, maxNodes?: int) -> CallToolResult`;
- semantic resolution without `hwnd`: сначала attached window, и только если attached target отсутствует, current active/foreground top-level window;
- result должен использовать `UseStructuredContent = true` и `TextContentBlock` с сериализованным JSON.

### Manifest/export

- перевести `windows.uia_snapshot` в `ToolLifecycle.Implemented`;
- обновить description так, чтобы она явно объясняла explicit/attached target precedence, `control` view default и отсутствие action side effects;
- при необходимости добавить `outputSchema`, если текущий MCP SDK/exporter это поддерживает без лишнего слоя ручного JSON.

## Implementation order

### Step 1. Contract-first DTO pass

- Обновить `IUiAutomationService` так, чтобы service boundary принимал `UiaSnapshotRequest` и возвращал `UiaSnapshotResult`.
- Добавить typed DTO в `src/WinBridge.Runtime.Contracts/`.
- Зафиксировать final public method signature для `windows.uia_snapshot` в `WindowTools.cs`.
- До заморозки target resolver отдельно проверить official Windows docs/API для active window path и зафиксировать в коде/доках, что именно считается authoritative active target.

### Step 2. UIA runtime service

- Добавить concrete implementation в `src/WinBridge.Runtime.Windows.UIA/`:
- `Win32UiAutomationService.cs`
- helper для cache request / traversal / artifact naming по месту.
- Root acquisition делать от live window через `ElementFromHandle`.
- Default traversal делать в `control view`, depth-bounded, node-budget-bounded.
- Кешировать only minimum viable properties/patterns, а не полный raw dump.

### Step 3. Tool handler and DI

- Зарегистрировать service в `src/WinBridge.Runtime/ServiceCollectionExtensions.cs`.
- Добавить dependency в `src/WinBridge.Server/Tools/WindowTools.cs`.
- Реализовать handler:
- explicit `hwnd` > attached window;
- if no explicit and no attached target: active window;
- missing/stale target -> `isError=true`;
- stale attached target does not silently fall through to active path;
- successful snapshot -> `isError=false`;
- unsupported environment -> честный typed failure без transport breakage.

### Step 4. Audit and evidence

- Добавить evidence artifact writing under `artifacts/diagnostics/<run_id>/uia/`.
- Расширить `AuditLog` или соседний helper так, чтобы `tool.invocation.completed` для `windows.uia_snapshot` писал structured data:
- `view`
- `requested_depth`
- `realized_depth`
- `node_count`
- `truncated`
- `acquisition_mode`
- `artifact_path`

### Step 5. Tool contract and docs metadata

- Обновить `ToolDescriptions.cs`, `ToolContractManifest.cs`, `ToolContractExporter.cs`.
- Убедиться, что `okno.contract`, exported markdown/json и `tools/list` больше не считают tool deferred/unsupported.

### Step 6. L1 unit tests

- Добавить runtime-focused unit tests в `tests/WinBridge.Runtime.Tests/`, минимум:
- selector/validation tests for request bounds;
- target precedence tests for explicit/attached/active resolution;
- traversal budget tests (`depth`, `maxNodes`, truncation);
- element id/path builder tests;
- artifact naming tests;
- manifest/exporter tests that `windows.uia_snapshot` is implemented.

### Step 7. L2 server integration tests

- Добавить `tests/WinBridge.Server.IntegrationTests/WindowUiaSnapshotToolTests.cs`.
- Расширить `WindowToolTestDoubles.cs` fake UIA service.
- Покрыть:
- explicit `hwnd` path;
- attached window path;
- active window path when no `hwnd` and no attached target;
- missing target;
- stale attached target;
- stale attached target does not fall through to active path;
- `isError` semantics;
- structuredContent/text payload parity.

### Step 8. L3 real smoke

- Расширить `tests/WinBridge.SmokeWindowHost/Program.cs`, чтобы helper window содержал deterministic semantic subtree:
- window title;
- label;
- text box;
- button;
- optional checkbox/list if нужен еще один control type.
- Обновить `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs` и `scripts/smoke.ps1`:
- exercise active path before attach when helper window owns foreground;
- attach helper window;
- call `windows.uia_snapshot`;
- assert root window node;
- assert child nodes contain expected names and/or automation ids;
- assert `artifactPath` exists;
- assert tool succeeds без focus/input side effects.

## Test ladder

### L1. Unit / narrow contract

Команды:

- `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj`

Файлы:

- new `tests/WinBridge.Runtime.Tests/UiaSnapshot*Tests.cs`
- existing `tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs`
- existing `tests/WinBridge.Runtime.Tests/ToolContractExporterTests.cs`
- existing `tests/WinBridge.Runtime.Tests/AuditLogTests.cs`

Минимальные сценарии:

- valid explicit request;
- invalid depth / invalid maxNodes;
- bounded traversal returns `truncated=true` when budget exceeded;
- element ids are snapshot-local and deterministic for one traversal;
- manifest/export no longer classify tool as deferred.

### L2. Server integration

Команды:

- `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj`

Файлы:

- new `tests/WinBridge.Server.IntegrationTests/WindowUiaSnapshotToolTests.cs`
- updated `tests/WinBridge.Server.IntegrationTests/WindowToolTestDoubles.cs`

Минимальные сценарии:

- explicit `hwnd` wins over attached window;
- attached window works when `hwnd` omitted;
- active window path works when neither `hwnd` nor attached target are present;
- missing/stale target returns `failed` + `isError=true`;
- stale attached target does not silently switch to active window;
- unsupported/UIA acquisition error remains typed tool failure, not exception leak;
- successful response returns `structuredContent` and matching serialized text block.

### L3. Real smoke

Команды:

- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`

Файлы:

- `tests/WinBridge.SmokeWindowHost/Program.cs`
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- `scripts/smoke.ps1`

Smoke scenario:

- поднять helper window с predictable child controls;
- подтвердить active-path: пока helper в foreground и session ещё не attach-нута, `windows.uia_snapshot` без `hwnd` снимает именно helper window;
- дождаться окна через `windows.list_windows`;
- выполнить `windows.attach_window`;
- вызвать `windows.uia_snapshot`;
- подтвердить, что snapshot rooted in attached helper window;
- подтвердить наличие expected child semantics;
- подтвердить наличие JSON artifact в diagnostics run directory.

## Source-of-truth docs sync

После реализации в том же цикле обновить:

- `docs/product/index.md`
- `docs/product/okno-spec.md`
- `docs/product/okno-roadmap.md`
- `docs/product/okno-vision.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/project-interfaces.json`
- `docs/generated/commands.md`
- `docs/generated/test-matrix.md`
- `docs/bootstrap/bootstrap-status.json`
- `docs/architecture/observability.md`
- `docs/CHANGELOG.md`

Для roadmap это означает не только обновить priority table/status, но и вычистить legacy narrative sections, которые всё ещё ставят clipboard/input раньше `windows.uia_snapshot`.

Для product docs это означает не ограничиваться generated/export слоем: spec, vision и product index должны отражать final V1 contract и execution order этого slice, а не старую greenfield-последовательность.

Команда синхронизации:

- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`

Замечание:

- `docs/architecture/capability-design-policy.md` менять только если в процессе появится reusable guardrail, который пригодится и для `windows.wait`/`windows.input`, а не как локальный комментарий к одной реализации.

## Risks / rollback

- UIA provider quality varies by framework; некоторые окна дадут слабое дерево даже при корректном root acquisition. Это не повод silent fallback'иться в screenshot/OCR.
- Unbounded descendant search может взорвать latency или дать шумный tree. Это снимается обязательными `Depth` и `MaxNodes`.
- Element identity across runs inherently weak; snapshot-local `elementId` нельзя рекламировать как future-proof selector for actions.
- `BoundingRectangle` может быть полезным metadata, но не должен неявно становиться input contract.
- Minimized/offscreen windows могут дать бедный или меняющийся tree; tool должен сообщать фактический результат, а не активировать окно.

Rollback:

- Если production implementation окажется unstable, допустим rollback только до честного deferred/unsupported state с одновременным откатом manifest/docs/tests, а не до half-implemented tool.
- Если unstable окажется только cache path, допустим сохранить implemented tool с `acquisitionMode=live_read`, но только если target semantics и L1/L2/L3 остаются зелеными.

## Checklist

- [ ] План начинается с contract-first и не тянет реализацию до фиксации success/error/fallback/identity/evidence.
- [ ] `windows.uia_snapshot` формулируется как honest observe tool для active/attached/explicit window.
- [ ] `windows.uia_action`, `windows.input`, OCR, watchers и daemon явно оставлены вне объема.
- [ ] Все integration points перечислены по конкретным файлам.
- [ ] Все external constraints опираются на official Microsoft/MCP docs.
- [ ] Есть явный L3 smoke-сценарий на живой helper window.
- [ ] `windows.capture` baseline не используется как fallback for semantic snapshot.
- [ ] Docs sync и generated refresh включены в тот же execution cycle.
- [ ] Rollback не оставляет репозиторий в declared-implemented-but-unsupported state.
