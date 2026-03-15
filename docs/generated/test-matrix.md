# Test Matrix

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

| Layer | Command | Coverage now | Status |
| --- | --- | --- | --- |
| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors | green |
| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, title-pattern timeout guardrails, monitor id formatting, activation decision logic, session dedupe, session mutation | green |
| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | raw stdio MCP protocol, attach/focus/activate contract semantics, monitor inventory, desktop capture by `monitorId`, capture result shape | green |
| Smoke | `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | init -> tools/list -> health -> list monitors -> desktop capture by monitorId -> list windows -> attach -> session_state -> capture -> helper minimize/activate/window capture | green |
| Local CI | `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | restore + build + test + smoke | green |

## Чего пока не хватает

- Contract tests на конкретную JSON-форму каждого deferred tool.
- Production-coverage для следующих slices: UIA, input, clipboard, wait.
- Отдельный monitor-select contract beyond `windows.list_monitors` + `monitorId` targeting, если позже понадобится richer multi-monitor workflow.
- Boundary tests на проектные зависимости, если слоёв станет больше.
- Coverage reporting как отдельный отчётный шаг.
