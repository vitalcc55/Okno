# Commands Inventory

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

## Canonical Entry Points

| Command | Purpose |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1` | `dotnet restore` |
| `powershell -ExecutionPolicy Bypass -File scripts/build.ps1` | solution build with analyzers into .NET artifacts root |
| `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` | unit + integration tests with staged server/helper bundle |
| `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | stdio MCP smoke with staged run bundle, owned helper scenario, click-first `windows.input` proof, fresh-host acceptance, terminal `windows.open_target` folder proof and artifact report |
| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | regenerate deterministic generated docs and bootstrap status |
| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |
| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | open latest local audit/smoke summaries |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/prepare-okno-test-bundle.ps1` | stage immutable server/helper run bundle for integration and smoke |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/resolve-okno-test-bundle.ps1` | resolve or materialize the effective staged bundle for the current verification context |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/write-okno-plugin-repo-root-hint.ps1` | stamp repo-root hint into plugin install surface before reinstall or refresh |
| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | run MCP server manually |

## Validation Entry Points

> Этот раздел перечисляет канонические validation commands и не зависит от конкретного run id. Для evidence конкретного запуска смотри `artifacts/smoke/<run_id>/` или используй `scripts/investigate.ps1`.

- `dotnet build WinBridge.sln --no-restore`
- `dotnet test WinBridge.sln --configuration Debug`
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1`

## Artifact Layout

- `artifacts/diagnostics/<run_id>/events.jsonl`
- `artifacts/diagnostics/<run_id>/summary.md`
- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png`
- `artifacts/diagnostics/<run_id>/launch/<launch_id>.json`
- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json`
- `artifacts/diagnostics/<run_id>/wait/<wait_id>.json`
- `artifacts/diagnostics/<run_id>/input/input-*.json`
- `artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png`
- `artifacts/smoke/<run_id>/report.json`
- `artifacts/smoke/<run_id>/summary.md`