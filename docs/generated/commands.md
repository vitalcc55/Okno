# Commands Inventory

## Канонические entry points

| Команда | Назначение |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1` | `dotnet restore` |
| `powershell -ExecutionPolicy Bypass -File scripts/build.ps1` | solution build с analyzers |
| `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` | unit + integration tests |
| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | перегенерировать `project-interfaces.json/md` из manifest |
| `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | stdio MCP smoke и artifact report |
| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |
| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | открыть latest audit/smoke summaries |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |
| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | запустить MCP server вручную |

## Последняя подтверждённая валидация

- `dotnet build WinBridge.sln --no-restore` -> success, 0 warnings, 0 errors.
- `dotnet test WinBridge.sln` -> success, 12/12 tests passed.
- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> success; regenerated manifest-derived `project-interfaces.json/md`.
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` -> success; verified init, tools/list, `okno.health`, `windows.list_windows`, `windows.attach_window`, `okno.session_state`.
- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` -> success.

## Последний smoke evidence

- smoke run id: `20260312T193352097`
- audit directory: `artifacts/diagnostics/20260312T163352866-a4bcf`
- smoke report: `artifacts/smoke/20260312T193352097/report.json`
