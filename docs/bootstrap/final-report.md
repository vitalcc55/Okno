# Okno Bootstrap Report

## Что теперь есть

- Верхнеуровневый `AGENTS.md` и durable docs в `docs/`.
- Рабочий solution на `C# / .NET 8` с thin `Runtime` composition root и отдельными runtime-проектами для `Contracts`, `Tooling`, `Diagnostics`, `Session`, `Windows.Shell` и будущих capability seams.
- `STDIO` MCP server с реальным tool contract.
- Observability vertical slice: `events.jsonl`, `summary.md`, smoke `report.json`.
- Канонический локальный control plane через `scripts/*.ps1`.
- Зафиксирован transport policy: product-ready только `STDIO` local process; HTTP не входит в текущий delivery scope.
- Source of truth для tool contract переведён в `ToolNames` + `ToolContractManifest`, а `project-interfaces.json/md` теперь перегенерируются через `scripts/refresh-generated-docs.ps1`.

## Подтверждённый end-to-end flow

Подтверждён сценарий:

1. `initialize`
2. `tools/list`
3. `okno.health`
4. `windows.list_windows`
5. `windows.attach_window`
6. `okno.session_state`

Последний подтверждённый smoke run:

- smoke run id: `20260312T193352097`
- visible windows: `15`
- attached hwnd: `67130`

## Что осознанно отложено

- Реальные UIA/capture/input/clipboard service implementations.
- Rich architectural boundary tests.
- Внешние telemetry exporters.

## Следующий естественный milestone

Этап 2 roadmap: усилить `window/session foundation`, затем добавить stage-3 capture без пересборки harness-слоя с нуля.
