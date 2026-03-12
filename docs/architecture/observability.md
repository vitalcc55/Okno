# Observability Architecture

## Почему так

`Okno` работает как `STDIO` MCP runtime, поэтому `stdout` нельзя использовать для обычных логов. Базовая observability-модель построена на двух локальных артефактах:

- `artifacts/diagnostics/<run_id>/events.jsonl` — канонический machine-readable event stream.
- `artifacts/diagnostics/<run_id>/summary.md` — human-readable journal того же запуска.

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
| `smoke report` | По запросу через `scripts/smoke.ps1` | Init/list/call raw MCP payloads + сводка | Один report на один run, без verbose console flood |

## Trace strategy

В runtime используется `ActivitySource` с именем `Okno`. Пока мы не экспортируем traces наружу, но `trace_id/span_id` уже входят в событие, чтобы не ломать будущую интеграцию с OpenTelemetry-compatible pipeline.

## Investigation workflow

1. Прогнать `powershell -File scripts/smoke.ps1`.
2. Открыть `artifacts/smoke/<run_id>/summary.md`.
3. При необходимости посмотреть соседний `report.json` и `artifacts/diagnostics/<run_id>/summary.md`.
4. Для быстрого доступа к последним артефактам использовать `powershell -File scripts/investigate.ps1`.

## Осознанно отложено

- Внешний OTel exporter.
- Metrics backend.
- Targeted capture для UIA/capture/input слоёв, которых пока нет в bootstrap slice.
