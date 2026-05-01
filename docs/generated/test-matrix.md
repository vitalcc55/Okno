# Test Matrix

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

> Матрица ниже описывает coverage и entry points. Она не утверждает факт конкретного успешного прогона; evidence отдельного запуска смотри в `artifacts/smoke/<run_id>/` и `artifacts/diagnostics/<run_id>/`.

| Layer | Command | Coverage now |
| --- | --- | --- |
| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors |
| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, launch exporter drift, launch runtime/status/evidence policy, input contract/runtime/materializer policy, display identity pipeline, monitor id formatting, activation decision logic, wait runtime/status/evidence policy, UIA runtime packaging/evidence, session dedupe, session mutation |
| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol through staged server/helper bundle with run-aware resolver semantics, public `windows.launch_process`, `windows.open_target` and click-first `windows.input` schema/result mapping, public `computer-use-win` action wave (`press_key`, `set_value`, `type_text`, `scroll`, `perform_secondary_action`, `drag`) including focused and coordinate-confirmed `type_text` fallback, selected-action `observeAfter` / `successorState`, strict `windowId` continuity reuse/fail-close, helper-backed drag proof, attach/focus/activate contract semantics, temp plugin install-copy schema proof, live `windows.uia_snapshot` target policy/result shape, public `windows.wait` schema/result mapping, monitor inventory, desktop capture by `monitorId`, desktop capture by explicit `hwnd`, capture result shape |
| Install/publication proof | `powershell -ExecutionPolicy Bypass -File scripts/codex/prove-computer-use-win-cache-install.ps1` | cache-installed `computer-use-win` plugin hash matches repo plugin files, launches from user cache path, exposes exact nine-tool tools/list surface, includes `type_text.allowFocusedFallback`, `type_text.observeAfter`, `type_text.point`, `type_text.coordinateSpace` with capture_pixels-only enum, selected-action `observeAfter`, no semantic-only `observeAfter`, and `list_apps.status=ok`; proof metadata records `repoHead`, working-tree cleanliness, runtime-affecting dirty paths, runtime bundle manifest freshness against publication inputs, acceptance eligibility and plugin digest |
| Smoke | `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | init -> tools/list -> health -> `windows.launch_process` dry-run/live helper launch -> list monitors -> list windows -> attach -> session_state -> uia_snapshot -> capture -> helper minimize/activate/window capture -> `windows.input` click-first helper textbox proof -> fresh-host `windows.input` tools/list/contract/binding proof -> wait active/exists/gone/text/focus/visual -> terminal `windows.open_target` dry-run/live folder proof -> input/open_target/launch artifact/event cross-check |
| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke |

## Чего пока не хватает

- Production-coverage для следующих slices: clipboard и broad input actions beyond click-first.
- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.
- Boundary tests на проектные зависимости, если слоёв станет больше.
- Coverage reporting как отдельный отчётный шаг.