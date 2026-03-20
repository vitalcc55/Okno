# CHANGELOG

Политика: фиксировать только инженерно значимые изменения, влияющие на operating model, control plane, архитектуру, проверки или контракт инструментов.

## 2026-03-20 08:30

- Source-of-truth docs для следующего slice `windows.wait` выровнены между exec-plan, product spec и roadmap: V1 теперь везде описан как один публичный tool `windows.wait`, а не zoo из `wait_for_*`; target model синхронизирован как `explicit -> attached -> active` без hidden activation/auto-attach drift, summary-row roadmap больше не теряет `text appears`, а сам exec-plan честно меняет MCP annotation expectation на `ReadOnly = false`, потому что wait обязан писать diagnostics artifact.

## 2026-03-20 09:16

- Package A для `windows.wait` закодирован без premature rollout: добавлены typed `Wait*` contracts, `IWaitService` больше не пустой, shell resolver получил capability-specific `ResolveWaitTarget(...)` с precedence `explicit -> attached -> active` и без fallback из stale explicit/attached target, а deferred manifest/exported contract теперь честно помечает `windows.wait` как `os_side_effect`, сохраняя lifecycle `Deferred`.

## 2026-03-19 18:23

- Добавлен отдельный exec-plan для следующего shipped public slice `windows.wait` в `docs/exec-plans/active/windows-wait.md`: план фиксирует V1 boundary, polling-first target model `explicit -> attached -> active`, condition matrix для `active/focus/element/text/visual`, honest status/error model, evidence contract, file-level integration map, L1/L2/L3 ladder, docs sync и rollback без преждевременного захода в `windows.uia_action` или broad `windows.input`.

## 2026-03-19 14:34

- Финальный branch-аудит по `windows.uia_snapshot` добрал остаточный doc drift вне основного rollout-контура: `architecture/index`, `architecture/layers`, `product/okno-roadmap`, `generated/stack-inventory` и `generated/stack-research` больше не описывают UIA как future seam/deferred path там, где текущий код уже публикует shipped public `windows.uia_snapshot`.
- Review-driven hardening для `windows.uia_snapshot` закрыл четыре корневые проблемы public rollout сразу на boundary-уровне: shared `UiaSnapshotRequestValidator` теперь одинаково валидирует request в server/runtime и держит публичный `maxNodes` в диапазоне `1..1024`, runtime service больше не публикует stale pre-resolution descriptor и теперь берет `window` только как sparse runtime-observed metadata из того же backend capture path, который построил фактический root/subtree, diagnostics boundary получил отдельный sanitized-failure path с сохранением `exception_type`/`exception_message` в audit trail, а smoke-контуру добавлен polling до materialized semantic subtree вместо single-shot UIA assertions.

## 2026-03-19 13:25

- `windows.uia_snapshot` доведён до shipped public slice: `Okno.Server` теперь публикует live MCP handler с `hwnd + depth + maxNodes`, `CallToolResult`, `structuredContent` и одним `TextContentBlock` без image block; target policy `explicit -> attached -> active` честно резолвится через existing shell seam, а stale/ambiguous target cases больше не маскируются deferred stub-ом.
- Public rollout осознанно изменил server runtime contract: `Okno.Server` получил host-facing зависимость на `WinBridge.Runtime.Windows.UIA.Hosting`, runtime config теперь legitimately включает `Microsoft.WindowsDesktop.App`, а boundary protection перенесён из pre-rollout запрета в positive guard на staged UIA worker artifacts и новый WindowsDesktop-backed shipped state.
- Smoke и generated/docs слой выровнены под live contract: helper window теперь содержит предсказуемый semantic subtree на WinForms standard controls, `McpProtocolSmokeTests` и `scripts/smoke.ps1` проверяют attached-source UIA snapshot и JSON evidence artifact, `project-interfaces`/`bootstrap-status`/`test-matrix` больше не держат UIA в deferred scope, а `observability`/`okno-spec`/`okno-roadmap` синхронизированы по фактическому shipped behavior.

## 2026-03-19 11:16

- Review-driven hardening для `windows.uia_snapshot` развёл semantics bounded traversal по двум независимым границам: `UiaSnapshotTreeBuilder` больше не зондирует child nodes после исчерпания `MaxNodes`, `Truncated` остаётся только флагом доказанного clipping, а новый `node_budget_boundary_reached` фиксирует strict no-probe budget boundary так же явно, как `depth_boundary_reached` фиксирует depth boundary.
- Isolated worker transport boundary доведён до evidence-grade поведения: parent/worker stdio теперь жёстко зафиксированы на UTF-8, stdin/process/payload failures маппятся в typed `worker_process` result без unhandled leak, raw worker stderr больше не публикуется как operator-facing `Reason`, а сохраняется в отдельный diagnostic artifact с `diagnostic_artifact_path` в runtime audit event.
- Worker packaging переведён на publish-aware consumer graph: `WinBridge.Runtime.Windows.UIA.Worker` включён в hosting graph как non-copying sidecar dependency, build и publish path stage-ят build/published artifacts раздельно, а runtime launcher теперь использует явный dual launch contract (`worker.exe` или apphost-less `worker.dll` через текущий `dotnet` host), поэтому valid framework-dependent `UseAppHost=false` consumer publish больше не ломает UIA sidecar path.

## 2026-03-19 09:51

- Consumer-facing DI boundary для `windows.uia_snapshot` доведён до self-contained registration contract: в `WinBridge.Runtime.Diagnostics` выделен общий `AddWinBridgeRuntimeDiagnostics(...)`, `WinBridge.Runtime` и `WinBridge.Runtime.Windows.UIA.Hosting` переиспользуют один и тот же diagnostics/time-provider fragment, а `AddWinBridgeRuntimeWindowsUia(...)` теперь явно принимает `contentRootPath` и `environmentName` вместо скрытой зависимости от чужого composition root.
- Добавлен минимальный consumer regression test на разрешение `IUiAutomationService` через hosting boundary без ручного дотягивания `AuditLog`/`AuditLogOptions`/`TimeProvider`; staging worker-а и DI resolution теперь проверяются как один согласованный hosting contract.

## 2026-03-19 09:30

- Follow-up hardening для `windows.uia_snapshot` довёл packaging/runtime boundary до consumer-facing контракта: `WinBridge.Runtime.Windows.UIA.Hosting` теперь stage-ит `WinBridge.Runtime.Windows.UIA.Worker.exe/.dll/.deps.json/.runtimeconfig.json` в собственный output и в output потребителей через явный MSBuild target, поэтому helper worker больше не появляется случайно только из-за прямых test references.
- В том же цикле deployment failure перестал ломать DI construction: `ProcessIsolatedUiAutomationBackend` проверяет наличие worker-а только на execution path и переводит misdeployment в `failed` result + `uia.snapshot.runtime.completed`, а `UiAutomationMtaRunner` закреплён как cooperative MTA utility, тогда как единственным strict-timeout owner остаётся isolated process boundary.

## 2026-03-18 16:58

- `windows.uia_snapshot` получил только Package B runtime/evidence слой без premature public rollout: добавлены `Win32UiAutomationService`, managed UIA backend на отдельном MTA thread, `ElementFromHandle` root acquisition, bounded `control view` traversal, enforcement `Depth`/`MaxNodes`/`Truncated` и JSON artifact под `artifacts/diagnostics/<run_id>/uia/`.
- Follow-up hardening этого же пакета убрал unintended host dependency drift и diagnostic gaps: UIA registration вынесена в отдельный host-facing project `WinBridge.Runtime.Windows.UIA.Hosting`, bootstrap `Okno.Server` снова не требует `Microsoft.WindowsDesktop.App` до Package C, worker artifact больше не ищется в `repo Debug` layout, production execution boundary переведена на isolated worker process, evidence shape теперь включает `requested_max_nodes`, `depth_boundary_reached` и `failure_stage`, а tree builder больше не перегружает `Truncated` ложноположительными depth-boundary срабатываниями.

## 2026-03-18 16:04

- Ещё один review-driven hardening проход довёл UIA target policy до строгой семантики explicit target: `explicitHwnd <= 0` в `windows.uia_snapshot` больше не нормализуется в “target absent”, а возвращает явный `stale_explicit_target`; при этом общий resolver для существующих `focus/capture` путей остаётся без этого UIA-specific правила.
- Deterministic generation для tracked docs усилена до byte-level policy: `scripts/refresh-generated-docs.ps1` теперь не только пишет фиксированный UTF-8, но и нормализует line endings, а `bootstrap-status.json` получает полный JSON string escaping без runtime-dependent serializer drift между `powershell.exe` и `pwsh`.

## 2026-03-18 15:41

- Review-driven hardening локализовал `explicitHwnd <= 0` обратно в `windows.uia_snapshot` policy path: общий `ResolveExplicitOrAttachedWindow(...)` больше не меняет semantics для существующих `windows.focus_window` и `windows.capture`, а regression tests теперь явно страхуют `hwnd: 0` при attached session window.
- `scripts/refresh-generated-docs.ps1` доведён до кросс-shell deterministic generation: tracked generated files теперь пишутся через общий UTF-8 writer с фиксированной BOM-policy, а `bootstrap-status.json` больше не зависит от различий `ConvertTo-Json` между `powershell.exe` и `pwsh`.

## 2026-03-18 15:22

- Review-driven fix для harness encoding вернул `scripts/refresh-generated-docs.ps1` в совместимый с Windows PowerShell режим: `.editorconfig` теперь фиксирует `utf-8-bom` для `*.ps1`, а сам скрипт повторно сохранён с BOM, чтобы `powershell.exe -File` больше не падал на non-ASCII литералах.
- В том же цикле deterministic generated docs остались без run-specific smoke metadata, но `refresh-generated-docs.ps1`, `ci.ps1` и `scripts/codex/verify.ps1` снова проходят именно через стандартный Windows PowerShell execution path, а не только через текущую `pwsh`-сессию Codex.

## 2026-03-18 12:59

- `windows.uia_snapshot` переведён на package-aware execution plan: active exec-plan теперь явно разделяет `Package A` (target policy + typed groundwork), `Package B` (runtime/evidence) и `Package C` (server rollout/smoke/generated docs), чтобы текущий цикл больше не притворялся полным rollout до `Implemented`.
- Для `windows.uia_snapshot` зафиксировано official-docs-driven meaning `active = foreground top-level window`, а product wording в spec/roadmap выровнен под precedence `explicit -> attached -> active` без преждевременного опубликования live MCP contract.
- В коде добавлены typed `UiaSnapshot*` DTO, typed `IUiAutomationService` seam и capability-specific `UiaSnapshotTargetResolution`; при этом public handler, manifest lifecycle и generated docs намеренно оставлены в честном deferred state.

## 2026-03-18 15:10

- Review-driven hardening для `windows.uia_snapshot` убрал внутреннюю двусмысленность groundwork: service-level request больше не несёт второй authoritative target, defaults централизованы в `UiaSnapshotDefaults`, а `UiaSnapshotResult` больше не может родиться с implicit success status.
- Active-path policy в `WindowTargetResolver` сделана snapshot-consistent: explicit `hwnd <= 0` нормализуется как absent target, active candidate выбирается из одного `ListWindows` snapshot, duplicate foreground entries по тому же `HWND` дедуплицируются, а `missing_target` и `ambiguous_active_target` больше не смешиваются.
- `scripts/refresh-generated-docs.ps1`, `docs/generated/*`, `docs/bootstrap/bootstrap-status.json` и tool contract export переведены на deterministic generation: tracked docs больше не содержат run-specific smoke ids, absolute artifact paths и `generated_at_utc`, а конкретное evidence остаётся только в локальных `artifacts/`.

## 2026-03-18 11:08

- Добавлен отдельный exec-plan для следующего shipped capability slice `windows.uia_snapshot` в `docs/exec-plans/active/windows-uia-snapshot.md`: план фиксирует contract-first goal, non-goals, official Microsoft/MCP constraints, file-level integration map, typed DTO/result shape, L1/L2/L3 ladder, docs sync и rollback policy без преждевременного захода в `windows.wait`, `windows.input` или `windows.uia_action`.
- В тот же exec-plan добавлена явная target policy `explicit -> attached -> active` для `windows.uia_snapshot` с запретом на silent fallback из stale attached/explicit target и расширен обязательный docs-sync до полного source-of-truth набора по slice: `product/index`, `okno-spec`, `okno-roadmap`, `okno-vision`, `observability` и generated docs.

## 2026-03-18 09:50

- `docs/product/okno-roadmap.md` переведён из чисто greenfield narrative в repo-aligned operational roadmap: документ теперь явно фиксирует текущий реализованный bootstrap baseline (`Server` / `Contracts` / `Diagnostics` / `Session` / `Windows.Display` / `Windows.Shell` / `Windows.Capture`) и различает `реализовано`, `частично`, `только_seam` и `запланировано` для capability slices.
- В roadmap добавлена таблица приоритетов в реальных именах репозитория и tool surface, чтобы execution order больше не расходился с текущим contract/runtime: следующий рубеж формулируется как `windows.uia_snapshot -> windows.wait -> environment/safety -> launch -> input`, а не как старый абстрактный порядок из greenfield-версии.
- Порядок приоритета при нехватке времени и итоговая формулировка V1 выровнены под текущий repo state: `launch` разделён на `windows.launch_process` и `windows.open_target`, а преждевременное расширение `windows.input` прямо зафиксировано как риск до появления snapshot/wait/guard layers.
- `docs/product/okno-vision.md` и `docs/product/okno-spec.md` дополнительно выровнены по concrete tool naming: V1 core tools теперь используют `windows.list_monitors`, `windows.focus_window`, `windows.activate_window`, `windows.clipboard_get` и `windows.clipboard_set`, а clipboard/paste терминология больше не расходится между vision/spec/roadmap.

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

- Добавлен exec-plan `docs/exec-plans/completed/completed-2026-03-17-display-window-contract-hardening.md` для линейного refactor-контура по DPI/coordinate semantics, display identity diagnostics, MCP self-documentation и optional `IsWindowArranged`.

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
