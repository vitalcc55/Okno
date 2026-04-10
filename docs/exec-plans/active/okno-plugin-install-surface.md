# ExecPlan: Okno plugin install surface

## Контекст

Repo plugin `plugins/okno/` уже согласован как source of truth, но Codex для local plugins загружает установленную copy из `~/.codex/plugins/cache/.../local`, а не напрямую из repo `source.path`. Значит install surface должен уметь находить реальный checkout репозитория после cache install и fail-closed объяснять, когда repo root hint не подготовлен.

## Цель

Сделать repo-side install surface `okno` пригодным для cache-installed запуска без возврата к tracked machine-specific absolute paths внутри `.mcp.json`.

## Границы

- Входит: plugin-local resolver/launcher, repo-side hint preparation script, README/docs sync, verification без правок home cache.
- Не входит: ручное редактирование `~/.codex/plugins/cache/...`, `~/.codex/config.toml`, restart Codex, reinstall plugin из UI/CLI.

## Repo changes

1. Launcher должен различать repo-local source checkout и cache-installed copy.
2. Plugin root должен принимать explicit repo-root hint из untracked file `.okno-repo-root.txt` или `OKNO_REPO_ROOT`.
3. Repo control plane должен иметь явный helper для генерации repo-root hint перед install/refresh.
4. README/generated commands/changelog должны объяснять install model и внешний follow-up.

## Acceptance criteria

- `plugins/okno/run-okno-mcp.ps1` больше не полагается только на relative path от plugin directory.
- `scripts/codex/write-okno-plugin-repo-root-hint.ps1` создаёт валидный hint file в repo source plugin.
- Cache-like plugin copy вне repo tree может resolve-ить repo root через hint file.
- Документация явно отделяет repo fix от home cache reinstall/restart шага.
- Acceptance считается закрытым только после proof для cache-installed copy, restart Codex/new thread и минимум одного read-only tool call (`okno.health` или `okno.contract`) уже из fresh-thread materialization path.

## Внешние шаги после repo fix

1. Запустить helper script из checkout репозитория.
2. Обновить install/cache copy plugin `okno` из repo marketplace.
3. Перезапустить Codex и открыть новый тред.
4. Подтвердить, что cached `.mcp.json` и cached plugin files содержат новый launcher/resolver/hint.
5. Из нового треда выполнить минимум один read-only tool call (`okno.health` или `okno.contract`) и зафиксировать, что materialized plugin surface реально поднялся после restart.
