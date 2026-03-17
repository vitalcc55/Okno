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

- smoke run id: 20260317T152814184
- monitor count: 3
- desktop monitor id: display-source:0000000000012673:2
- audit directory: artifacts/diagnostics/20260317T122815827-46875
- capture artifact: artifacts/diagnostics/20260317T122815827-46875/captures/window-window-2885672-20260317T122816898-c28bc49d6cf0466f9fe932c041874b87.png
- helper capture artifact: artifacts/diagnostics/20260317T122815827-46875/captures/window-window-2885672-20260317T122817536-e2a23c3d21d74615ab07bbbbe06e5c6f.png
- smoke report: artifacts/smoke/20260317T152814184/report.json
