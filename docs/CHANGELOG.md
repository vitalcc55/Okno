# CHANGELOG

Политика: фиксировать только инженерно значимые изменения, влияющие на operating model, control plane, архитектуру, проверки или контракт инструментов.

## 2026-03-15 14:57

- `windows.activate_window` теперь маркирует `ambiguous` как tool-level error на MCP boundary, а не как успешный result.
- `WinBridge.Server.IntegrationTests` получил явный build dependency на `WinBridge.SmokeWindowHost`, чтобы deterministic helper window собирался вместе с integration graph.
- Display seam получил alias-aware `monitorId` resolution и batch-friendly monitor lookup для window enumeration, чтобы не терять explicit targeting при fallback и не делать full monitor scan на каждый HWND.
- Smoke harness переведён на helper как канонический attach/capture target: сначала normal window capture, затем `minimize -> activate_window -> helper capture`.

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
