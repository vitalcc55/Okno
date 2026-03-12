# Runbook: Investigation

## Быстрый путь

1. Выполни `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`.
2. Выполни `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1`.
3. Открой последний `report.json` из `artifacts/smoke/<run_id>/`.

## Что смотреть первым

- В `summary.md` smoke run: protocol version, количество tool definitions, количество видимых окон, attached hwnd, путь к audit dir.
- В `artifacts/diagnostics/<run_id>/summary.md`: tool start/completion и `session.attached`.
- В `events.jsonl`: machine-readable event stream для автоматического анализа.

## Типовые сценарии

### `tools/list` пустой или недоступен

- Проверь, что tool classes помечены `McpServerToolType`.
- Прогони `dotnet build`.
- Сравни `scripts/smoke.ps1` report с `tests/WinBridge.Server.IntegrationTests`.

### smoke висит

- Проверь, закрывает ли harness stdin child-process.
- Сравни последний `artifacts/smoke/<run_id>/report.json` с актуальным `scripts/smoke.ps1`.

### runtime не видит окон

- Посмотри `windows.list_windows` payload в `report.json`.
- Убедись, что процесс работает в интерактивной Windows session.
