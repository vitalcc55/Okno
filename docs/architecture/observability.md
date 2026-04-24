# Observability Architecture

## Почему так

`Okno` работает как `STDIO` MCP runtime, поэтому `stdout` нельзя использовать для обычных логов. Базовая observability-модель построена на двух локальных артефактах:

- `artifacts/diagnostics/<run_id>/events.jsonl` — канонический machine-readable event stream.
- `artifacts/diagnostics/<run_id>/summary.md` — human-readable journal того же запуска.
- `artifacts/diagnostics/<run_id>/captures/<capture_id>.png` — фактические PNG-доказательства для `windows.capture`; public capture payload carries the matching structured metadata, including `frameBounds` for window captures.
- `artifacts/diagnostics/<run_id>/launch/<launch_id>.json` — JSON-evidence для launch-family runtime tools: `launch-*.json` для `windows.launch_process` и `open-target-*.json` для `windows.open_target`, оба с factual result, materialized `artifact_path` и safe `failure_diagnostics` без raw executable path / args / working directory / full target disclosure.
- `artifacts/diagnostics/<run_id>/uia/<snapshot_id>.json` — JSON-evidence для public `windows.uia_snapshot`, включая успешные snapshot payloads и worker-process diagnostic artifacts при transport/process failure.
- `artifacts/diagnostics/<run_id>/wait/<wait_id>.json` — JSON-evidence для public `windows.wait`, включая request, resolved target, attempts и final status.
- `artifacts/diagnostics/<run_id>/input/input-*.json` — JSON-evidence для public `windows.input` runtime path, включая sanitized request summary, target summary, factual result, per-action factual results и safe `failure_diagnostics` без raw exception message, raw typed text, `key`/`keys` или future keyboard payloads.
- `artifacts/diagnostics/<run_id>/wait/visual/<visual_wait_artifact>.png` — optional baseline/current PNG-доказательства для shipped `visual_changed`, если best-effort evidence stage успел их материализовать.

Для текущего reporting-first rollout `okno.health` намеренно не materialize-ит отдельный diagnostics artifact и не пишет отдельный runtime event. Evidence для health readiness живёт в самом tool response и в `artifacts/smoke/<run_id>/report.json` / `summary.md`, чтобы не обещать лишний observability surface до отдельного contract change.

Поверх этого `scripts/smoke.ps1` создаёт отдельный smoke-report в `artifacts/smoke/<run_id>/report.json` и `summary.md`; current Package E smoke также доказывает click-first `windows.input` через helper textbox click, `input/input-*.json`, `input.runtime.completed`, post-action `wait(focus_is)` и fresh staged host acceptance.

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

Для tool-aware redaction базовый audit trail теперь использует единый marker set во всех чувствительных invocation/runtime paths:

- `redaction_applied`
- `redaction_class`
- `redacted_fields`
- `request_summary_suppressed` или `exception_message_suppressed`, когда fail-safe path намеренно не пишет raw значение
- `gate_decision`, `gate_risk_level`, `gate_guard_capability`, `gate_requires_confirmation`, `gate_dry_run_supported`, `gate_reason_codes` для internal gated execution boundary; public payload-shaped keys не должны делить с ними один namespace

## Каналы и anti-noise rules

| Канал | Всегда включён | Что пишет | Anti-noise правило |
| --- | --- | --- | --- |
| `tool.invocation.started/completed` | Да | Старт/завершение каждого MCP tool call, sanitized `request_summary`, internal `gate_*` markers и redaction markers при наличии | Не пишем внутренние step-by-step сообщения по `list_windows`/`attach`; public payload-shaped поля и internal gate metadata не должны конфликтовать по именам. Для `computer-use-win` completion trail теперь идёт через safe builders: допустимы `runtime_state`, `app_id`, `window_id`, `state_token_present`, `public_reason` и artifact hints, но raw `stateToken`, raw low-level `reason` и другие sensitive per-handler fields не должны materialize-иться в `events.jsonl`. |
| `session.attached` | Да | Только state transition session -> window | Повторное attach к тому же окну не логируется вторично |
| `display.identity.state_changed` | Да, при смене состояния | Typed diagnostics по `display_config_strong` vs `gdi_fallback` | Логируем только transition, а не каждый повторный вызов |
| `capture artifacts` | Для `windows.capture` | PNG в diagnostics run directory + metadata в `tool.invocation.completed` | Храним один PNG на один успешный capture call, без отдельного verbose event stream |
| `launch.runtime.completed` | Для factual `windows.launch_process` runtime path | Typed metadata + ссылка на launch JSON artifact в diagnostics run directory | Один итоговый runtime event на один factual runtime result после входа в live runtime path, если audit sink доступен; validation-only failures до `Process.Start(...)` остаются на existing completion audit trail и не materialize-ят launch artifact/event. Payload включает `status`, `decision`, `result_mode`, `failure_code`, `executable_identity`, `process_id`, `started_at_utc`, `has_exited`, `exit_code`, `main_window_observed`, `main_window_handle`, `main_window_observation_status`, `artifact_path`, `failure_stage` и `exception_type`. Raw executable path, args, working directory, env и raw `exception_message` intentionally не попадают ни в `events.jsonl`, ни в `summary.md`; summary line для launch содержит только safe identifiers (`executable_identity`, `process_id`, `result_mode`, `artifact_path`), а `artifact_write` и event-write failures не меняют factual launch result. |
| `open_target.runtime.completed` | Для factual `windows.open_target` runtime path | Typed metadata + ссылка на open-target JSON artifact в diagnostics run directory | Один итоговый runtime event на один factual shell-open result после входа в live runtime path, если audit sink доступен; validation-only failures и dry-run preview path остаются на existing completion audit trail и не materialize-ят runtime artifact/event. Payload включает `status`, `decision`, `result_mode`, `failure_code`, `target_kind`, `target_identity`, `uri_scheme`, `accepted_at_utc`, `handler_process_id`, `artifact_path`, `failure_stage` и `exception_type`. Raw full path, raw URL, query, fragment и raw `exception_message` intentionally не попадают ни в `events.jsonl`, ни в `summary.md`; summary line для open_target содержит только safe identifiers (`target_kind`, `target_identity` или `uri_scheme`, `handler_process_id`, `artifact_path`), а `artifact_write` и event-write failures не меняют factual open-target result. |
| `input.runtime.completed` | Для factual `windows.input` runtime path | Typed metadata + ссылка на input JSON artifact в diagnostics run directory | Один итоговый runtime event на один factual runtime result после входа в `IInputService`, если audit sink доступен; pre-gate invalid/blocked/needs-confirmation paths остаются на `tool.invocation.*` audit trail и не materialize-ят runtime artifact/event. Payload включает `status`, `decision`, `result_mode`, `failure_code`, `target_hwnd`, `target_source`, `completed_action_count`, `failed_action_index`, `action_types`, `coordinate_spaces`, `artifact_path`, `failure_stage`, `exception_type` и `committed_side_effect_evidence`. `click_dispatch_clean_failure`, `click_dispatch_partial_compensated` и `click_dispatch_partial_uncompensated` сохраняют Package B distinction между clean dispatch failure и partial committed side effect; raw `exception_message`, raw typed text, `key`/`keys` и future keyboard payloads intentionally не попадают ни в event, ни в artifact. `artifact_write` и event-write failures не downcast-ят factual input result. |
| `uia.snapshot.runtime.completed` | Для `windows.uia_snapshot` runtime path | Typed metadata + ссылка на JSON artifact в diagnostics run directory | Один итоговый runtime event на один snapshot, если audit sink доступен; payload включает `requested_depth`, `requested_max_nodes`, `node_count`, `truncated`, `depth_boundary_reached`, `node_budget_boundary_reached`, `artifact_path`, `diagnostic_artifact_path` и `failure_stage`, без пошагового tree-walk spam. Отсутствие event-а при проблемах audit sink не меняет factual snapshot result и расследуется по public payload + artifact path |
| `wait.runtime.completed` | Для `windows.wait` runtime path | Typed metadata + ссылка на wait JSON artifact и optional visual artifacts, если condition = `visual_changed` | Один итоговый runtime event на один wait call, если audit sink доступен; payload включает `condition`, `status`, `target_source`, `target_failure_code`, `attempt_count`, `elapsed_ms`, `artifact_path`, `matched_text_source`, `diagnostic_artifact_path`, `visual_evidence_status`, `visual_baseline_artifact_path` и `visual_current_artifact_path`; для internal `runtime_unhandled` / `tool_boundary_unhandled` / `artifact_write` failures event сохраняет `exception_type`, `failure_stage` и redaction markers, а raw `exception_message` intentionally не попадает в `events.jsonl`. Отсутствие event-а при проблемах audit sink не меняет factual wait result и расследуется по public payload + wait artifact |
| internal proof markers (`launch.preview.completed`, `open_target.preview.completed`, `wait.visual.baseline_captured`) | Только когда smoke/proof path требует дополнительной синхронизации | Best-effort runtime markers для dry-run preview branch и visual baseline readiness; не являются public feature surface и не имеют собственного artifact contract | Marker write не должен менять factual tool result. Smoke может опираться на них только в healthy observability environment и обязан трактовать их как internal investigation/proof signal, а не как public API |
| `smoke report` | По запросу через `scripts/smoke.ps1` | Init/list/call raw MCP payloads + сводка | Один report на один run, без verbose console flood |

Отсутствие отдельной строки для `okno.health` в таблице — намеренное текущее поведение: tool остаётся success-path readiness summary без dedicated artifact/event и без hidden enforcement.

## Trace strategy

В runtime используется `ActivitySource` с именем `Okno`. Пока мы не экспортируем traces наружу, но `trace_id/span_id` уже входят в событие, чтобы не ломать будущую интеграцию с OpenTelemetry-compatible pipeline.

## Investigation workflow

1. Прогнать `powershell -File scripts/smoke.ps1`.
2. Открыть `artifacts/smoke/<run_id>/summary.md` и проверить readiness digest из `okno.health`.
3. При необходимости посмотреть соседний `report.json`, `artifacts/diagnostics/<run_id>/summary.md`, `artifacts/diagnostics/<run_id>/captures/`, `artifacts/diagnostics/<run_id>/launch/`, `artifacts/diagnostics/<run_id>/uia/`, `artifacts/diagnostics/<run_id>/wait/` и `artifacts/diagnostics/<run_id>/input/`.
4. Для `windows.launch_process` сверить public `structuredContent.artifactPath` с JSON artifact и, если audit sink был здоров, с событием `launch.runtime.completed` в `events.jsonl`; если runtime event показывает `failure_stage=artifact_write`, launch artifact ожидаемо отсутствует и расследование идёт по safe markers `failure_stage` / `exception_type` без raw `exception_message`. При отсутствии runtime event из-за sink failure source of truth остаётся public payload + launch artifact. Для dry-run smoke допускается дополнительная проверка internal marker `launch.preview.completed`, но только как best-effort proof signal и не как public contract.
5. Для `windows.open_target` сверить public `structuredContent.artifactPath` с JSON artifact `launch/open-target-*.json` и, если audit sink был здоров, с событием `open_target.runtime.completed` в `events.jsonl`; для folder dry-run smoke допустима дополнительная проверка internal marker `open_target.preview.completed`, но только как best-effort proof signal и не как public contract. При отсутствии runtime event из-за sink failure source of truth остаётся public payload + open-target artifact.
6. Для `windows.input` сверить public `structuredContent.artifactPath` с JSON artifact `input/input-*.json` и, если audit sink был здоров, с событием `input.runtime.completed` в `events.jsonl`; при failed click dispatch обязательно проверить `failure_stage` и `committed_side_effect_evidence`, чтобы clean failure, partial compensated и partial uncompensated не выглядели одинаково. Если runtime event показывает `failure_stage=artifact_write`, input artifact ожидаемо отсутствует и расследование идёт по safe markers `failure_stage` / `exception_type` без raw `exception_message`; при отсутствии runtime event из-за sink failure source of truth остаётся public payload + input artifact.
7. Для `windows.uia_snapshot` сверить public `structuredContent.artifactPath` с JSON artifact и, если audit sink был здоров, с событием `uia.snapshot.runtime.completed` в `events.jsonl`. При отсутствии runtime event из-за sink failure source of truth остаётся public payload + JSON artifact.
8. Для `windows.wait` сверить public `structuredContent.artifactPath` с JSON artifact и, если audit sink был здоров, с событием `wait.runtime.completed` в `events.jsonl`; для `visual_changed` сначала проверить `lastObserved.visualEvidenceStatus`, а baseline/current PNG проверять только если runtime вернул соответствующие artifact paths. Internal marker `wait.visual.baseline_captured` допустим как smoke-only readiness signal, но остаётся best-effort и не должен трактоваться как обязательный public runtime event. Для `runtime_unhandled` и `tool_boundary_unhandled` internal `failure_diagnostics` остаются в wait artifact, а runtime event несёт только `failure_stage`, `exception_type` и redaction markers; для `artifact_write` wait artifact уже отсутствует по определению, поэтому расследование идёт по тем же runtime markers без raw `exception_message`.
9. Для `okno.health` не искать `artifactPath`: отсутствие dedicated health artifact/event в этом rollout является ожидаемым contract.
10. Для быстрого доступа к последним артефактам использовать `powershell -File scripts/investigate.ps1`.

## Осознанно отложено

- Внешний OTel exporter.
- Metrics backend.
- Более глубокие targeted capture hooks для UIA/input/wait слоёв сверх текущего observe slice.
