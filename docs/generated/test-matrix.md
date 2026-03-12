# Test Matrix

| Layer | Command | Coverage now | Status |
| --- | --- | --- | --- |
| Static/analyzers | `dotnet build WinBridge.sln --no-restore` | compile, nullability, analyzers, warnings-as-errors | green |
| Unit | `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj` | audit schema routing, session dedupe, session mutation | green |
| Integration | `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj` | initialize + `tools/list` over raw stdio MCP | green |
| Smoke | `powershell -File scripts/smoke.ps1` | init -> list tools -> health -> list windows -> attach -> session_state | green |
| Local CI | `powershell -File scripts/ci.ps1` | restore + build + test + smoke | green |

## Чего пока не хватает

- Contract tests на конкретную JSON-форму каждого deferred tool.
- UIA/capture/input/clipboard tests.
- Boundary tests на проектные зависимости, если слоёв станет больше.
- Coverage reporting как отдельный отчётный шаг.
