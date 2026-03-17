# CHANGELOG

Политика: фиксировать только инженерно значимые изменения, влияющие на operating model, control plane, архитектуру, проверки или контракт инструментов.

## 2026-03-17 16:07

- WGC one-shot capture path больше не считает первый пришедший frame автоматически годным: runtime теперь валидирует `Direct3D11CaptureFrame.ContentSize`, допускает ровно один `Direct3D11CaptureFramePool.Recreate(...)` и сохраняет PNG только после стабилизации геометрии кадра.
- Persistent WGC size drift после single `Recreate` теперь считается отдельным acquisition failure class: для `desktop` runtime уходит в существующий `Graphics.CopyFromScreen` fallback, а для `window` возвращает честный tool-level error без подмены window semantics screen-copy screenshot'ом.
- Metadata и artifact materialization больше не опираются на pre-acquisition target snapshot: после WGC stabilization runtime пере-выравнивает target fields под финальный PNG, а перед desktop fallback заново резолвит текущий monitor target вместо silent reuse устаревших bounds.
- Для успешного `desktop` WGC path monitor identity больше не может silently перепрыгнуть на новый live monitor после refresh: refreshed topology используется только если она подтверждает тот же target, по которому уже был создан capture item.
- Добавлены unit tests на sizing/failure policy и authoritative target materialization, чтобы WGC stabilization не расходилась с MCP metadata path.

## 2026-03-17 10:30

- Review-driven hardening закрыл подтверждённые P1/P2 gaps: MCP server теперь явно включает per-monitor DPI awareness в startup bootstrap, а integration smoke ждёт достижение этого process invariant перед валидацией contract.
- Display identity diagnostics переведены на отдельный builder/state machine: query failures больше не могут ложно репортиться как `display_config_strong`, а деградация `GetTargetName` теперь сохраняется в typed diagnostics без понижения strong identity до `gdi_fallback`.
- `okno.contract` обогащён до структурированных `ContractToolDescriptor` вместо списка имён, а `ToolContractManifest` и generated interfaces теперь публикуют те же полные описания, что и MCP `tools/list`.
- Manual docs синхронизированы с новым contract: `observe-capture`, `observability` и `okno-spec` больше не описывают удалённый monitor `dpi scale` и теперь отражают `coordinateSpace`, window-authoritative DPI и display identity diagnostics.

## 2026-03-17 10:44

- Закрыт residual bug в `DisplayIdentityDiagnosticsBuilder`: mixed-case `GetTargetName` degradation + `gdi_fallback` больше не может выдавать противоречивое сообщение в духе "strong identity preserved"; добавлен red/green unit test на этот сценарий.

## 2026-03-17 10:53

- Stage 3 contract surface доведён до консистентности: `okno.contract` и export/generated contract теперь используют общий `ContractToolDescriptorFactory`, а enum-like поля `lifecycle` и `safetyClass` публикуются в одном canonical snake_case literal format без drift между live tool и exported interfaces.

## 2026-03-17 11:40

- Review-driven closeout для workstream 1/2/3 завершён: `Win32WindowManager` больше не маскирует `GetDpiForWindow == 0` значением `96`, coverage-driven `gdi_fallback` теперь получает typed reason `display_config_coverage_gap`, а manual spec `windows.list_monitors` / `windows.list_windows` синхронизирована с фактическими diagnostics и window DPI fields.

## 2026-03-17 12:15

- Verification gaps закрыты локальными test seams: добавлен pipeline-level runtime test на mixed-case display failures (`GetTargetName` -> `GetSourceName`) и behavioural integration test на `windows.capture(scope="desktop", hwnd=...)`, подтверждающий monitor resolution explicit HWND поверх attached window. Smoke по-прежнему проверяет tools/list metadata и end-to-end protocol flow, а не подменяет эти два таргетных контракта.

## 2026-03-17 09:39

- Перестроен display/window contract: `MonitorDescriptor` больше не несёт authoritative `DpiScale`, `WindowDescriptor` теперь содержит `EffectiveDpi`, а `windows.capture` возвращает `coordinateSpace=physical_pixels` и window-authoritative DPI metadata только для window targets.
- `WinBridge.Runtime.Windows.Display` теперь возвращает typed `DisplayTopologySnapshot` с `DisplayIdentityDiagnostics`; `windows.list_monitors`, `okno.health`, audit `events.jsonl` и summary получили evidence о `display_config_strong` vs `gdi_fallback` без изменения safe fallback behavior.
- MCP surface стал самодокументируемым: добавлен единый `ToolDescriptions` source of truth, `DescriptionAttribute` на ключевые tools/parameters и smoke/integration checks на наличие descriptions в `tools/list`.
- `IsWindowArranged` переведён в optional metadata probe через runtime export lookup, чтобы `windowState=arranged` оставался enrichment-сигналом и не создавал жёсткой платформенной зависимости inventory.

## 2026-03-17 15:15

- `refresh-generated-docs.ps1` больше не публикует ложный `green` в `docs/generated/test-matrix.md`: матрица стала декларативной, а не evidence-claiming. В том же цикле добавлена `Normalize-SmokeReport`, чтобы generated docs не падали на legacy/partial smoke reports, а `GetMonitorInfo` degradation теперь поднимается в typed diagnostics вместо тихого skip-case.

## 2026-03-17 09:21

- Добавлен активный exec-plan `docs/exec-plans/active/display-window-contract-hardening.md` для линейного refactor-контура по DPI/coordinate semantics, display identity diagnostics, MCP self-documentation и optional `IsWindowArranged`.

## 2026-03-15 14:57

- `windows.activate_window` теперь маркирует `ambiguous` как tool-level error на MCP boundary, а не как успешный result.
- `WinBridge.Server.IntegrationTests` получил явный build dependency на `WinBridge.SmokeWindowHost`, чтобы deterministic helper window собирался вместе с integration graph.
- Display seam переведён на source/view-oriented `monitorId` для captureable desktop views; alias fallback для stale strong ids убран, а `windows.list_windows` перешёл на batch-friendly monitor lookup без full scan на каждый HWND.
- Smoke harness переведён на helper как канонический attach/capture target: сначала normal window capture, затем `minimize -> activate_window -> helper capture`, при этом acceptance для activation path выровнен с платформой как `done | ambiguous` вместо безусловочного `done`.

## 2026-03-16 14:58

- `windows.activate_window` теперь возвращает `done` только когда финальный live snapshot подтверждает usable state: окно остаётся foreground и уже не находится в `minimized`.
- `WinBridge.Runtime.Windows.Display` зарегистрирован как полноценный runtime slice в `WinBridge.sln`, а `scripts/refresh-generated-docs.ps1` больше собирает `runtime_projects` автоматически по `WinBridge.Runtime*` csproj, чтобы bootstrap inventory не дрейфовал от фактической архитектуры.

## 2026-03-16 15:55

- Helper-based smoke harness переведён на contract-true readiness: helper считается готовым только после появления в `windows.list_windows(includeInvisible=false)`, а не сразу после появления `MainWindowHandle`.
- Post-activation helper capture в smoke и raw MCP integration smoke теперь ждёт фактическую capturable ready-state через `windows.capture(scope="window")`, вместо безусловочного принятия любого `ambiguous` как достаточного для следующего шага.

## 2026-03-16 16:14

- Smoke harness и raw MCP integration smoke переведены на session-scoped JSON-RPC request ids и строгий response matching по ожидаемому `id`, чтобы retry-циклы не могли переиспользовать корреляционные идентификаторы и ловить ответы не от своей фазы.

## 2026-03-16 16:56

- `windows.activate_window` больше не собирает final verdict из разрозненных raw-handle probes: финальная verification теперь опирается на единый platform-backed snapshot с identity (`ProcessId`/`ThreadId`/`ClassName`) и usability (`IsForeground`/`IsMinimized`) signals, а `done` выдаётся только по нему.

## 2026-03-15 12:36

- `windows.attach_window` теперь разводит invalid selector и ambiguous match: отсутствие `hwnd`/`titlePattern`/`processName` возвращается как `failed`, а `ambiguous` остаётся только для реального multi-match path.
- Добавлены integration tests на `windows.focus_window` и contract semantics `windows.attach_window`, включая success/failure paths, `already_attached` и гарантию, что focus не мутирует session state.
- `docs/generated/test-matrix.md` переведён в generated-артефакт через `scripts/refresh-generated-docs.ps1`, чтобы coverage-summary не расходился с `commands.md`.

## 2026-03-15 13:58

- Добавлен новый `Windows.Display` seam с monitor inventory и strong `monitorId` на основе `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo`, а `windows.list_windows` и `windows.capture` теперь становятся monitor-aware.
- Добавлены новые MCP tools `windows.list_monitors` и `windows.activate_window`; `windows.capture(scope="desktop")` теперь поддерживает explicit `monitorId`, а `window capture` для minimизированного окна честно требует предварительный `activate_window`.
- Smoke harness и raw stdio integration smoke расширены до deterministic helper-window сценария: explicit desktop capture по `monitorId`, `minimize -> activate_window -> window capture`.

## 2026-03-12 16:16

- Инициализирован harness bootstrap: созданы `docs/`, ExecPlan, верхнеуровневый `AGENTS.md`, solution/project skeleton и baseline-конфиг для `net8.0-windows`.
- Добавлен рабочий `STDIO` MCP skeleton runtime на `C# / .NET 8`, включая `windows.list_windows`, `windows.attach_window`, `windows.focus_window` и честный deferred tool contract.
- Нормализованы `scripts/bootstrap.ps1`, `scripts/build.ps1`, `scripts/test.ps1`, `scripts/smoke.ps1`, `scripts/ci.ps1`, `scripts/investigate.ps1` и `scripts/codex/*`.
- Проверки подтверждены командами `dotnet build`, `dotnet test`, `powershell -File scripts/smoke.ps1` и `powershell -File scripts/ci.ps1`.
- Продуктовое имя выровнено на `Okno`, product docs перенесены в `docs/product/`, добавлены `README.md` и `docs/architecture/engineering-principles.md`.

## 2026-03-12 21:02

- Реализован первый observe-loop `windows.list_windows -> windows.attach_window -> windows.capture` с `CallToolResult`, `structuredContent`, `image/png` и локальным PNG artifact в `artifacts/diagnostics/<run_id>/captures/`.
- `windows.capture` переведён из deferred в implemented tool contract, добавлен в smoke-required контур и покрыт server-side/integration тестами.
- Для `window` и `desktop` capture выбран `Windows.Graphics.Capture` как основной backend, с native GDI fallback для target-specific случаев, где WGC не даёт стабильный frame вовремя.
- `scripts/smoke.ps1`, generated docs и bootstrap status синхронизированы под новый observe/capture slice.

## 2026-03-13 14:15

- Добавлена универсальная policy для проектирования новых capability slices в [docs/architecture/capability-design-policy.md](architecture/capability-design-policy.md): identity, lifecycle, fallback, false-success, evidence и verification matrix.
- `architecture/index`, `engineering-principles` и `AGENTS.md` теперь явно ссылаются на эту policy как на обязательный baseline для следующих `focus`, `clipboard`, `input`, `wait`, `uia` и других feature.

## 2026-03-13 09:40

- Ужесточена семантика `window capture`: minimизированный `HWND` теперь считается явной ошибкой, а не допустимым success target.
- Fallback policy разделена по `scope`: `Graphics.CopyFromScreen` оставлен только для `desktop monitor` path, но больше не подменяет `window` semantics.
- Desktop fallback теперь достигается и в `WGC unsupported` средах, а не только после timeout/native error в WGC path.
- Untargeted `desktop` capture теперь явно выбирает primary monitor через Win32 monitor enumeration, а не через virtual-origin heuristic.
- `desktop` capture теперь корректно падает в primary monitor и при stale attached window, вместо того чтобы требовать ручного исправления session state.
- Attached target теперь переоценивается по `HWND + ProcessId + ThreadId + ClassName`; `Title` снова не считается обязательной частью identity.
- MCP annotations для `windows.capture` выровнены с реальным поведением: tool больше не `readOnly`/`idempotent`, и теперь помечен как `openWorld`.
- Expected operational failures в capture path нормализованы в `CaptureOperationException`, чтобы клиент получал tool-level `isError=true`, а не transport-level exception.
- `FrameArrived` теперь освобождает лишние `Direct3D11CaptureFrame`, а smoke/integration selection выбирают candidate через реальные attach/capture success criteria вместо геометрических эвристик.
