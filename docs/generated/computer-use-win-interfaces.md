# Project Interfaces Map

## Runtime interfaces

### MCP over STDIO

- transport: `STDIO`
- protocol baseline: MCP `2025-11-25`
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
| `list_apps` | `session_mutation` | — | Возвращает running Windows apps для Computer Use for Windows. Публичный operator surface группирует visible window instances по app approval identity, публикует selectable `windows[]` с runtime-owned opaque `windowId` и заменяет latest published selector snapshot для следующего `get_app_state`. |
| `get_app_state` | `os_side_effect` | — | Начинает или продолжает app use session и возвращает action-ready состояние конкретного window target: screenshot, compact accessibility tree, stateToken, captureReference и warnings. Primary reusable selector — `windowId` из latest `list_apps`; `hwnd` остаётся explicit low-level/debug path и не минтит новый public selector, если target не совпал с current published snapshot. stateToken публикуется только если capture и accessibility tree построены успешно; observation failure отвечает structured `failed` без session commit. |
| `click` | `os_side_effect` | — | Кликает по elementIndex или pixel coordinates из последнего app state. При наличии elementIndex runtime сначала пере-подтверждает target через свежий UIA snapshot; coordinate click остаётся low-confidence path и требует explicit confirm. Optional observeAfter=true materialize-ит successorState + screenshot после committed action. |
| `press_key` | `os_side_effect` | — | Нажимает named key literal или modifier combo в текущий app session. Bare printable text сюда не входит и должен идти через type_text. Optional observeAfter=true materialize-ит successorState + screenshot после committed action. |
| `set_value` | `os_side_effect` | — | Семантически устанавливает text или number value у конкретного элемента из последнего app state через ValuePattern/RangeValuePattern без hidden typing fallback. |
| `type_text` | `os_side_effect` | — | Печатает текст в текущий app session. По умолчанию требует focused editable proof; explicit allowFocusedFallback=true требует confirm=true и fresh target-local focus proof для poor-UIA fallback, оставаясь SendInput/verify_needed path без hidden clipboard fallback. Optional observeAfter=true materialize-ит successorState + screenshot после committed action. |
| `scroll` | `os_side_effect` | — | Скроллит app session по elementIndex или point из последнего app state. Optional observeAfter=true materialize-ит successorState + screenshot после committed action. |
| `perform_secondary_action` | `os_side_effect` | — | Выполняет product-owned secondary action над semantic target из последнего app state. |
| `drag` | `os_side_effect` | — | Делает drag gesture в app session по element indices или coordinates из последнего app state. Optional observeAfter=true materialize-ит successorState + screenshot после committed action. |

### Deferred but declared

| Tool | Current outcome | Planned phase | Policy |
| --- | --- | --- | --- |

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
- `artifacts/diagnostics/<run_id>/computer-use-win/action-*.json`
- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png`
- `artifacts/diagnostics/<run_id>/launch/<launch_id>.json`
- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json`
- `artifacts/diagnostics/<run_id>/wait/<wait_id>.json`
- `artifacts/diagnostics/<run_id>/input/input-*.json`
- `artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png`
- `artifacts/smoke/<run_id>/report.json`
- `artifacts/smoke/<run_id>/summary.md`
