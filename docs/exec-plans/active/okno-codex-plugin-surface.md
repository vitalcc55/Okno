# Okno Codex Plugin Surface

## Goal

Добавить безопасный repo-local Codex plugin surface под продуктовым именем `Okno` без изменения существующего MCP runtime lifecycle и без конкурирующего источника истины для server startup/config.

## Constraints

- Не менять текущий `config.toml`-based MCP setup.
- Не подменять existing repo-local `.agents/skills/okno-mcp-smoke`.
- Plugin-level MCP должен публиковаться как отдельный server `okno`, а не как rewrite legacy `windows`.
- User-facing naming для plugin surface должно использовать `Okno`, а не `WinBridge`.

## Plan

1. Прочитать repo README и подтвердить product naming.
2. Добавить repo marketplace `.agents/plugins/marketplace.json`.
3. Создать plugin `plugins/okno/.codex-plugin/plugin.json`.
4. Добавить plugin-level `.mcp.json` и repo-owned launcher для `Okno.Server`.
5. Добавить bundled workflow-skill без дублирования существующего smoke skill по имени.
6. Обновить repo docs и changelog.
7. Проверить JSON structure и diff без изменения runtime config.

## Status

- Выполнено: repo-local marketplace добавлен.
- Выполнено: plugin `okno` добавлен.
- Выполнено: plugin-local MCP `okno` добавлен.
- Выполнено: bundled skill `okno-runtime-workflow` добавлен.
- Выполнено: README и CHANGELOG обновлены.
- Выполнено: JSON manifests валидированы через `ConvertFrom-Json`.

## Verification

- `powershell -ExecutionPolicy Bypass -File scripts/codex/bootstrap.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- `Get-Content .agents/plugins/marketplace.json -Raw | ConvertFrom-Json`
- `Get-Content plugins/okno/.codex-plugin/plugin.json -Raw | ConvertFrom-Json`
- `Get-Content plugins/okno/.mcp.json -Raw | ConvertFrom-Json`

## Notes

- Plugin публикует repo-local MCP `okno` поверх текущего репозитория.
- Legacy home-level `windows` остаётся отдельным compatibility слоем и не переписывается этим plugin.
