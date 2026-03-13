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
| `okno.health` | `ReadOnly` | Возвращает сводку состояния runtime и артефактов. |
| `okno.contract` | `ReadOnly` | Возвращает текущий tool contract runtime. |
| `okno.session_state` | `ReadOnly` | Возвращает текущий session snapshot. |
| `windows.list_windows` | `ReadOnly` | Перечисляет top-level окна Windows. |
| `windows.attach_window` | `SessionMutation` | Прикрепляет текущую сессию к выбранному окну. |
| `windows.focus_window` | `OsSideEffect` | Пытается перевести окно в foreground. |
| `windows.capture` | `OsSideEffect` | Снимает window или desktop monitor capture и возвращает PNG + metadata. |

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
