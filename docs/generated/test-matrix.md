# Test Matrix

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

> Матрица ниже описывает coverage и entry points. Она не утверждает факт успешного последнего прогона; смотри `docs/generated/commands.md` и `docs/bootstrap/bootstrap-status.json` для latest verified validation.

| Layer | Command | Coverage now |
| --- | --- | --- |
| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors |
| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, display identity pipeline, monitor id formatting, activation decision logic, session dedupe, session mutation |
| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol, attach/focus/activate contract semantics, monitor inventory, desktop capture by `monitorId`, desktop capture by explicit `hwnd`, capture result shape |
| Smoke | `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | init -> tools/list -> health -> list monitors -> desktop capture by monitorId -> list windows -> attach -> session_state -> capture -> helper minimize/activate/window capture |
| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke |

## Чего пока не хватает

- Contract tests на конкретную JSON-форму каждого deferred tool.
- Production-coverage для следующих slices: UIA, input, clipboard, wait.
- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.
- Boundary tests на проектные зависимости, если слоёв станет больше.
- Coverage reporting как отдельный отчётный шаг.
