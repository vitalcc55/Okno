# Commands Inventory

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

## Canonical Entry Points

| Command | Purpose |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1` | `dotnet restore` |
| `pwsh -ExecutionPolicy Bypass -File scripts/lint-powershell.ps1` | PowerShell static analysis for non-ignored repo-owned scripts and plugin launchers through PSScriptAnalyzer 1.25.0+ on pwsh 7.2.11+ |
| `powershell -ExecutionPolicy Bypass -File scripts/build.ps1` | solution build with analyzers into .NET artifacts root |
| `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` | runtime unit tests + fast integration tests with staged server/helper bundle |
| `powershell -ExecutionPolicy Bypass -File scripts/test-install-surface-acceptance.ps1` | install/release acceptance suite for packaging, installer, shared runtime and public install surface |
| `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` | stdio MCP smoke with staged run bundle, owned helper scenario, click-first `windows.input` proof, fresh-host acceptance, terminal `windows.open_target` folder proof and artifact report |
| `powershell -ExecutionPolicy Bypass -File scripts/computer-use-win-physical-policy-proof-smoke.ps1` | narrow helper-backed real-STDIO proof-smoke for phase-1 `computer-use-win` executionFacts, covering semantic, expected_physical and fallback_physical action paths |
| `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` | regenerate deterministic generated docs and bootstrap status |
| `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1` | local CI equivalent |
| `powershell -ExecutionPolicy Bypass -File scripts/release-verify.ps1` | full release gate: fast CI + install/release acceptance + cache-install publication proof |
| `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1` | open latest local audit/smoke summaries |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1` | Codex bootstrap handshake |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/prepare-okno-test-bundle.ps1` | stage immutable server/helper run bundle for integration and smoke |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/resolve-okno-test-bundle.ps1` | resolve or materialize the effective staged bundle for the current verification context |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/resolve-okno-server-launch-target.ps1` | resolve the effective staged Windows launch target from pinned `artifacts_root` (`Okno.Server.exe` preferred, `dotnet + .dll` fallback) |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- runtime install --descriptor-path <path> --json` | install a shared per-user `computer-use-win` runtime into the canonical `%LocalAppData%\\Okno\\computer-use-win` store and mark it current |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- runtime status --descriptor-path <path> --json` | inspect the current shared per-user `%LocalAppData%\\Okno\\computer-use-win` runtime store state and descriptor compatibility |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- runtime verify --descriptor-path <path> --json` | verify current shared runtime manifest/state integrity and fail closed on drift or incompatibility |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- runtime repair --descriptor-path <path> --json` | re-materialize the current shared per-user runtime from the pinned release descriptor |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- install codex --descriptor-path <path> --json` | install the shared runtime, install the thin `computer-use-win` plugin into the user-owned Codex path, and update the personal marketplace |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- install runtime-only --descriptor-path <path> --json` | install only the shared runtime and return a ready-to-paste MCP snippet for plain `STDIO` clients |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- update codex --descriptor-path <path> --json` | refresh the Codex install path while preserving unrelated marketplace entries |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- update runtime-only --descriptor-path <path> --json` | refresh the shared runtime-only installation from the pinned descriptor |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- repair codex --descriptor-path <path> --json` | restore missing Codex plugin/runtime coupling and rewrite the personal marketplace if needed |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- uninstall codex --json` | remove the installed Codex plugin surface and keep or remove the shared runtime according to install receipts |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- uninstall runtime-only --json` | remove the runtime-only install receipt and delete the shared runtime store when no other install receipt still needs it |
| `dotnet run --project src/WinBridge.Setup.Cli/WinBridge.Setup.Cli.csproj -- status --json` | inspect the combined installer status for the shared runtime plus Codex/runtime-only receipts |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/package-computer-use-win-runtime-release.ps1 -Version <semver> -Rid win-x64 [-RuntimeDownloadBaseUrl <base-url>]` | package a versioned standalone `computer-use-win` runtime release zip plus SHA256SUMS and emit a canonical runtime descriptor for downstream installer artifacts |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/package-computer-use-win-plugin-release.ps1 -Version <semver> -RuntimePackagingResultPath <path>` | package a versioned thin `computer-use-win` plugin bundle zip plus SHA256SUMS without embedding the runtime directory, using the canonical runtime packaging result supplied by the runtime release step |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/package-computer-use-win-setup-cli-payload.ps1 -Version <semver> -Rid win-x64 -RuntimePackagingResultPath <path>` | package the headless setup CLI payload zip plus SHA256SUMS for installer-first distribution, embedding the canonical runtime descriptor proven by the runtime packaging result |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/package-okno-setup-app-release.ps1 -Version <semver> -Rid win-x64 -RuntimePackagingResultPath <path>` | package the WinUI 3 `Okno Setup.exe` installer zip plus SHA256SUMS for installer-first distribution, embedding the canonical runtime descriptor proven by the runtime packaging result |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/install-computer-use-win.ps1 -Mode codex|runtime-only -PayloadArchivePath <zip> [-PayloadChecksumPath <path>] [-DescriptorPath <path>]` | thin PowerShell bootstrap installer that runs the packaged setup CLI without repo checkout and verifies local payload archives by checksum unless an explicit unsafe dev-only bypass is used |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1` | publish self-contained `computer-use-win` runtime bundle into `plugins/computer-use-win/runtime/win-x64/` |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/materialize-computer-use-win-cache-copy.ps1` | mirror the repo `computer-use-win` plugin into the local cache-install proof root before cache-surface verification |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/prove-computer-use-win-cache-install.ps1` | prove cache-installed `computer-use-win` tools/list/schema surface matches the repo plugin copy, fresh-thread `get_app_state -> set_value` materializes `executionFacts`, `type_text.coordinateSpace` is capture_pixels-only, runtime bundle is fresh for current publication inputs, and runtime release descriptor metadata is present |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/test-install-surface-acceptance.ps1` | Codex wrapper for the install/release acceptance suite |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1` | Codex verify handshake |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/release-verify.ps1` | Codex wrapper for the full release gate |
| `powershell -ExecutionPolicy Bypass -File scripts/codex/write-okno-plugin-repo-root-hint.ps1` | stamp repo-root hint into internal okno plugin install surface before reinstall or refresh |
| `dotnet run --project src/WinBridge.Server/WinBridge.Server.csproj --no-build` | run MCP server manually |

## Validation Entry Points

> Этот раздел перечисляет канонические validation commands и не зависит от конкретного run id. Для evidence конкретного запуска смотри `artifacts/smoke/<run_id>/` или используй `scripts/investigate.ps1`.

- `dotnet build WinBridge.sln --no-restore`
- `pwsh -ExecutionPolicy Bypass -File scripts/lint-powershell.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/test-install-surface-acceptance.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/computer-use-win-physical-policy-proof-smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/ci.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/release-verify.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/release-verify.ps1`

## Artifact Layout

- `artifacts/diagnostics/<run_id>/events.jsonl`
- `artifacts/diagnostics/<run_id>/summary.md`
- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png`
- `artifacts/diagnostics/<run_id>/computer-use-win/action-*.json`
- `artifacts/diagnostics/<run_id>/launch/<launch_id>.json`
- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json`
- `artifacts/diagnostics/<run_id>/wait/<wait_id>.json`
- `artifacts/diagnostics/<run_id>/input/input-*.json`
- `artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png`
- `artifacts/smoke/<run_id>/report.json`
- `artifacts/smoke/<run_id>/summary.md`
- `artifacts/smoke/computer-use-win-physical-policy-phase-1/<run_id>/report.json`
- `artifacts/smoke/computer-use-win-physical-policy-phase-1/<run_id>/summary.md`