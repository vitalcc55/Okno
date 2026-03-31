# Okno repo-local plugin

Этот plugin добавляет installable Codex surface для продукта `Okno` внутри текущего репозитория.

## Зачем он нужен

- дать аккуратный repo-local entry point в Codex plugin directory;
- упаковать workflow-слой под продуктовым именем `Okno`, а не под внутренним namespace `WinBridge`;
- не ломать текущую схему, где MCP lifecycle и запуск runtime живут отдельно от plugin metadata.

## Что plugin делает

- публикует repo-local installable plugin `okno`;
- публикует plugin-local MCP server `okno` через `.mcp.json`;
- добавляет bundled skill `okno-runtime-workflow`;
- использует repo marketplace в `.agents/plugins/marketplace.json`.

## MCP model

- plugin добавляет отдельный MCP server `okno`;
- server запускается plugin-local launcher script `run-okno-mcp.ps1`;
- launcher использует уже собранный `Okno.Server.dll` и не делает build в transport path;
- legacy home-level MCP server `windows` не переписывается и не заменяется этим plugin.

Важно:

- `.mcp.json` должен оставаться переносимым и не хранить machine-specific absolute paths до checkout;
- launcher сам вычисляет repo root относительно plugin directory;
- MCP server стартует через `powershell -NoProfile -NonInteractive`, чтобы profile output не ломал stdio transport.

Это сознательная модель: plugin даёт правильный repo-local MCP identity `okno`, а старый `windows` остаётся отдельным compatibility/local-profile слоем, пока он ещё нужен.

## Bundled skill

- `skills/okno-runtime-workflow/`

Этот skill предназначен для repo-local работы с `Okno`: verify, smoke, triage diagnostics и работа через plugin-local MCP server `okno`.

## Relation to OpenAI computer use

Этот plugin остаётся текущим локальным integration path для `Codex app/CLI/IDE` и не зависит от built-in OpenAI `computer use`.

Практическая модель такая:

- `shell` закрывает terminal/code workflows;
- plugin-local `okno` закрывает Windows-native desktop/runtime workflows;
- будущая `computer use`-совместимость, если понадобится, должна приходить отдельным adapter-слоем поверх `Okno`, а не через подмену plugin-local MCP surface.
