# Project Interfaces Map

## Runtime interfaces

### MCP over STDIO

- transport: `STDIO`
- protocol baseline: MCP `2025-06-18`
- server entry point: `src/WinBridge.Server/Program.cs`
- delivery status: `product-ready target`
- smoke-validated methods:
  - `initialize`
  - `tools/list`
  - `tools/call`

### HTTP / URL server

- status: `not implemented`
- policy: не входит в текущий delivery baseline;
- activation point: только после готового и стабилизированного `STDIO`.

## Tool surface

### Implemented now

| Tool | Safety class | Policy | Notes |
| --- | --- | --- | --- |
| `list_apps` | `read_only` | — | Возвращает running Windows apps для Computer Use for Windows. Публичный operator surface: показывает app identity, foreground hints, approval status и window metadata без low-level engine tool noise. |
| `get_app_state` | `read_only` | — | Начинает или продолжает app use session и возвращает action-ready состояние app window: screenshot, compact accessibility tree, stateToken, captureReference и warnings. stateToken публикуется только если capture и accessibility tree построены успешно; observation failure отвечает structured `failed` без session commit. |
| `click` | `os_side_effect` | — | Кликает по elementIndex или pixel coordinates из последнего app state. При наличии elementIndex runtime сначала пере-подтверждает target через свежий UIA snapshot; coordinate click остаётся low-confidence path и требует explicit confirm. |

### Deferred but declared

| Tool | Current outcome | Planned phase | Policy |
| --- | --- | --- | --- |
| `type_text` | `unsupported` | computer-use-win action wave 2 | — |
| `press_key` | `unsupported` | computer-use-win action wave 2 | — |
| `scroll` | `unsupported` | computer-use-win action wave 2 | — |
| `drag` | `unsupported` | computer-use-win action wave 2 | — |

## Script interfaces

- `scripts/bootstrap.ps1`
- `scripts/build.ps1`
- `scripts/test.ps1`
- `scripts/smoke.ps1`
- `scripts/ci.ps1`
- `scripts/investigate.ps1`
- `scripts/refresh-generated-docs.ps1`
- `scripts/codex/bootstrap.ps1`
- `scripts/codex/verify.ps1`

## Artifact interfaces

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
