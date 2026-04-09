# Test Matrix

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

> Матрица ниже описывает coverage и entry points. Она не утверждает факт конкретного успешного прогона; evidence отдельного запуска смотри в `artifacts/smoke/<run_id>/` и `artifacts/diagnostics/<run_id>/`.

| Layer | Command | Coverage now |
| --- | --- | --- |
| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors |
| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, launch exporter drift, launch runtime/status/evidence policy, display identity pipeline, monitor id formatting, activation decision logic, wait runtime/status/evidence policy, UIA runtime packaging/evidence, session dedupe, session mutation |
| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol, public `windows.launch_process` and `windows.open_target` schema/result mapping, attach/focus/activate contract semantics, live `windows.uia_snapshot` target policy/result shape, public `windows.wait` schema/result mapping, monitor inventory, desktop capture by `monitorId`, desktop capture by explicit `hwnd`, capture result shape |
| Smoke | `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | init -> tools/list -> health -> `windows.launch_process` dry-run/live helper launch -> list monitors -> list windows -> attach -> session_state -> uia_snapshot -> capture -> helper minimize/activate/window capture -> wait active/exists/gone/text/focus/visual -> terminal `windows.open_target` dry-run/live folder proof -> open_target and launch artifact/event cross-check |
| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke |

## Чего пока не хватает

- Contract tests на конкретную JSON-форму каждого deferred tool.
- Production-coverage для следующих slices: input, clipboard.
- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.
- Boundary tests на проектные зависимости, если слоёв станет больше.
- Coverage reporting как отдельный отчётный шаг.