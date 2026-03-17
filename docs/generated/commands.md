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

## Validation Entry Points

> Этот раздел перечисляет канонические validation commands, но не утверждает факт успешного последнего прогона. Реальное smoke evidence публикуется ниже, а full validation state должен подтверждаться отдельными run artifacts или `scripts/codex/verify.ps1`.

- `dotnet build WinBridge.sln --no-restore`
- `dotnet test WinBridge.sln`
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1`

## Latest Smoke Evidence

- smoke run id: 20260317T173903433
- monitor count: 3
- desktop monitor id: display-source:0000000000012673:2
- audit directory: artifacts/diagnostics/20260317T143905097-7911c
- capture artifact: artifacts/diagnostics/20260317T143905097-7911c/captures/window-window-3933762-20260317T143905862-6ba4dcae43f64d0b9f7fbd84f6e35822.png
- helper capture artifact: artifacts/diagnostics/20260317T143905097-7911c/captures/window-window-3933762-20260317T143906232-aa67cbe0a50447cdb7b761a12e55552a.png
- smoke report: artifacts/smoke/20260317T173903433/report.json
