---
name: "okno-mcp-smoke"
description: "Повторяемый workflow для локальной проверки STDIO MCP runtime Okno: smoke-run, извлечение evidence и быстрая triage по artifacts."
---

# Okno MCP Smoke

## Когда использовать

- Нужно быстро проверить, что `STDIO` MCP runtime стартует и отвечает.
- Нужно подтвердить текущий tool contract после изменений в `src/WinBridge.Server` или `src/WinBridge.Runtime`.
- Нужно получить свежий evidence pack для расследования.

## Входы

- Рабочее дерево Okno.
- Успешная сборка `dotnet build`.

## Шаги

1. Запусти `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`.
2. Если smoke упал, сразу запусти `powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1`.
3. Открой последний `artifacts/smoke/<run_id>/report.json`.
4. Проверь:
   - есть ли `initialize` и `tools/list`;
   - есть ли `okno.health`;
   - есть ли `windows.list_windows`;
   - если count > 0, прошёл ли `windows.attach_window`.
5. Если tool contract менялся, обнови `docs/generated/project-interfaces.md` и `docs/generated/commands.md`.

## Definition of done

- smoke summary получен;
- latest report сохранён в `artifacts/smoke`;
- при изменении контракта docs синхронизированы.
