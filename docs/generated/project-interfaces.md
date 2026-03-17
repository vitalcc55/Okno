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

| Tool | Safety class | Notes |
| --- | --- | --- |
| `okno.health` | `read_only` | Возвращает сводку состояния runtime: transport, artifacts, implemented tools, active monitor count и состояние display identity path. |
| `okno.contract` | `read_only` | Возвращает текущий MCP contract runtime: implemented tools, deferred tools и notes без вызова side effects. |
| `okno.session_state` | `read_only` | Возвращает текущий session snapshot, включая attached window и mode без изменения session state. |
| `windows.list_monitors` | `read_only` | Возвращает active monitor targets текущей desktop session вместе с diagnostics display identity path. Используй перед explicit desktop capture по monitorId. |
| `windows.list_windows` | `read_only` | Возвращает live inventory top-level окон. По умолчанию показывает видимые рабочие окна; includeInvisible=true добавляет invisible и untitled windows для diagnostics и target resolution. |
| `windows.attach_window` | `session_mutation` | Выбирает live window target и прикрепляет его к текущей сессии. Attach требует стабильной identity окна, а не только совпавшего заголовка. |
| `windows.activate_window` | `os_side_effect` | Делает attached window usable target: при необходимости restore, затем попытка foreground focus и обязательная final live-state verification. Status done означает подтверждённый foreground usable state, а не просто попытку активации. |
| `windows.focus_window` | `os_side_effect` | Запрашивает foreground focus для explicit hwnd или attached window. В отличие от activate_window не делает restore и не подтверждает usability final-state. |
| `windows.capture` | `os_side_effect` | Выполняет capture выбранной цели и возвращает PNG + structured metadata. При scope=window target выбирается как explicit hwnd или attached window. При scope=desktop target выбирается как explicit monitorId, explicit hwnd, attached window или primary monitor. Все bounds и pixel sizes выражены в physical_pixels. |

### Deferred but declared

| Tool | Current outcome | Planned phase |
| --- | --- | --- |
| `windows.clipboard_get` | `unsupported` | roadmap stage 4 |
| `windows.clipboard_set` | `unsupported` | roadmap stage 4 |
| `windows.input` | `unsupported` | roadmap stage 5 |
| `windows.uia_snapshot` | `unsupported` | roadmap stage 6 |
| `windows.uia_action` | `unsupported` | roadmap stage 7 |
| `windows.wait` | `unsupported` | roadmap stage 8 |

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
- `artifacts/smoke/<run_id>/report.json`
- `artifacts/smoke/<run_id>/summary.md`
