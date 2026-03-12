# Commands Inventory

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

## Canonical Entry Points

| Command | Purpose |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1` | `dotnet restore` |
| `powershell -ExecutionPolicy Bypass -File scripts/build.ps1` | solution build with analyzers |
| `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` | unit + integration tests |
| `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | stdio MCP smoke and artifact report |
| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | regenerate generated docs and bootstrap status |
| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |
| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | open latest audit/smoke summaries |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |
| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | run MCP server manually |

## Latest Verified Validation

- `dotnet build WinBridge.sln --no-restore` -> success, 0 warnings, 0 errors.
- `dotnet test WinBridge.sln` -> success, 12/12 tests passed.
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` -> success; verified init, tools/list, `okno.health`, `windows.list_windows`, `windows.attach_window`, `okno.session_state`.
- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> success; regenerated `project-interfaces.*`, `commands.md`, `bootstrap-status.json`.
- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` -> success.

## Latest Smoke Evidence

- smoke run id: 20260312T194932621
- audit directory: artifacts/diagnostics/20260312T164933088-df26e
- smoke report: artifacts/smoke/20260312T194932621/report.json
