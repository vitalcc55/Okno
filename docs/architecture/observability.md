# Observability Architecture

## Почему так

`Okno` работает как `STDIO` MCP runtime, поэтому `stdout` нельзя использовать для обычных логов. Базовая observability-модель построена на двух локальных артефактах:

- `artifacts/diagnostics/<run_id>/events.jsonl` — канонический machine-readable event stream.
- `artifacts/diagnostics/<run_id>/summary.md` — human-readable journal того же запуска.
- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png` — фактические PNG-доказательства для `windows.capture`.
- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json` — JSON-evidence для public `windows.uia_snapshot`, включая успешные snapshot payloads и worker-process diagnostic artifacts при transport/process failure.
- `artifacts/diagnostics/<run_id>/wait/<wait_id>.json` — JSON-evidence для public `windows.wait`, включая request, resolved target, attempts и final status.
- `artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png` — optional baseline/current PNG-доказательства для shipped `visual_changed`, если best-effort evidence stage успел их материализовать.

Для текущего reporting-first rollout `okno.health` намеренно не materialize-ит отдельный diagnostics artifact и не пишет отдельный runtime event. Evidence для health readiness живёт в самом tool response и в `artifacts/smoke/<run_id>/report.json` / `summary.md`, чтобы не обещать лишний observability surface до отдельного contract change.

Поверх этого `scripts/smoke.ps1` создаёт отдельный smoke-report в `artifacts/smoke/<run_id>/report.json` и `summary.md`.

Текущий продуктовый приоритет тоже вытекает из этого решения: сначала доводим до product-ready именно `STDIO` local process. HTTP transport остаётся следующим этапом и не должен размывать текущий observability contract.

## Каноническая схема событий

Каждая запись `events.jsonl` сериализуется в `snake_case` и содержит стабильные поля:

- `schema_version`
- `timestamp_utc`
- `service`
- `environment`
- `severity`
- `event_name`
- `message_human`
- `run_id`
- `trace_id`
- `span_id`
- `tool_name`
- `outcome`
- `window_hwnd`
- `data`

Версионирование схемы сейчас ведётся через `AuditConstants.SchemaVersion = 1.0.0`.

## Каналы и anti-noise rules

| Канал | Всегда включён | Что пишет | Anti-noise правило |
| --- | --- | --- | --- |
| `tool.invocation.started/completed` | Да | Старт/завершение каждого MCP tool call | Не пишем внутренние step-by-step сообщения по `list_windows`/`attach` |
| `session.attached` | Да | Только state transition session -> window | Повторное attach к тому же окну не логируется вторично |
| `display.identity.state_changed` | Да, при смене состояния | Typed diagnostics по `display_config_strong` vs `gdi_fallback` | Логируем только transition, а не каждый повторный вызов |
| `capture artifacts` | Для `windows.capture` | PNG в diagnostics run directory + metadata в `tool.invocation.completed` | Храним один PNG на один успешный capture call, без отдельного verbose event stream |
| `uia.snapshot.runtime.completed` | Для `windows.uia_snapshot` runtime path | Typed metadata + ссылка на JSON artifact в diagnostics run directory | Один итоговый runtime event на один snapshot; payload включает `requested_depth`, `requested_max_nodes`, `node_count`, `truncated`, `depth_boundary_reached`, `node_budget_boundary_reached`, `artifact_path`, `diagnostic_artifact_path` и `failure_stage`, без пошагового tree-walk spam |
| `wait.runtime.completed` | Для `windows.wait` runtime path | Typed metadata + ссылка на wait JSON artifact и optional visual artifacts, если condition = `visual_changed` | Один итоговый runtime event на один wait call; payload включает `condition`, `status`, `target_source`, `target_failure_code`, `attempt_count`, `elapsed_ms`, `artifact_path`, `matched_text_source`, `diagnostic_artifact_path`, `visual_evidence_status`, `visual_baseline_artifact_path` и `visual_current_artifact_path`; для internal `runtime_unhandled` / `tool_boundary_unhandled` / `artifact_write` failures event дополнительно сохраняет `exception_type` и `exception_message`, без per-tick spam |
| `smoke report` | По запросу через `scripts/smoke.ps1` | Init/list/call raw MCP payloads + сводка | Один report на один run, без verbose console flood |

Отсутствие отдельной строки для `okno.health` в таблице — намеренное текущее поведение: tool остаётся success-path readiness summary без dedicated artifact/event и без hidden enforcement.

## Trace strategy

В runtime используется `ActivitySource` с именем `Okno`. Пока мы не экспортируем traces наружу, но `trace_id/span_id` уже входят в событие, чтобы не ломать будущую интеграцию с OpenTelemetry-compatible pipeline.

## Investigation workflow

1. Прогнать `powershell -File scripts/smoke.ps1`.
2. Открыть `artifacts/smoke/<run_id>/summary.md` и проверить readiness digest из `okno.health`.
3. При необходимости посмотреть соседний `report.json`, `artifacts/diagnostics/<run_id>/summary.md`, `artifacts/diagnostics/<run_id>/captures/`, `artifacts/diagnostics/<run_id>/uia/` и `artifacts/diagnostics/<run_id>/wait/`.
4. Для `windows.uia_snapshot` сверить public `structuredContent.artifactPath` с JSON artifact и событием `uia.snapshot.runtime.completed` в `events.jsonl`.
5. Для `windows.wait` сверить public `structuredContent.artifactPath` с JSON artifact и событием `wait.runtime.completed` в `events.jsonl`; для `visual_changed` сначала проверить `lastObserved.visualEvidenceStatus`, а baseline/current PNG проверять только если runtime вернул соответствующие artifact paths. Для `runtime_unhandled` и `tool_boundary_unhandled` internal `failure_diagnostics` доступны и в wait artifact, и в runtime event; для `artifact_write` wait artifact уже отсутствует по определению, поэтому расследование идёт только по `wait.runtime.completed` с `failure_stage = artifact_write`, `exception_type` и `exception_message`.
6. Для `okno.health` не искать `artifactPath`: отсутствие dedicated health artifact/event в этом rollout является ожидаемым contract.
7. Для быстрого доступа к последним артефактам использовать `powershell -File scripts/investigate.ps1`.

## Осознанно отложено

- Внешний OTel exporter.
- Metrics backend.
- Более глубокие targeted capture hooks для UIA/input/wait слоёв сверх текущего observe slice.
