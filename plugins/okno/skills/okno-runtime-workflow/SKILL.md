---
name: "okno-runtime-workflow"
description: "Workflow для работы с продуктом Okno в этом репозитории: verify, smoke, diagnostics triage и безопасная работа поверх текущего MCP runtime без подмены существующей config.toml схемы."
---

# Okno Runtime Workflow

## Когда использовать

- Нужно работать с продуктом `Okno` в этом репозитории, а не с внутренним codename `WinBridge`.
- Нужно проверить текущий runtime, tool contract или smoke path.
- Нужно собрать evidence и быстро разобрать diagnostics artifacts.
- Нужен repo-local workflow поверх уже существующего MCP слоя.

## Важный контекст

- Этот skill рассчитан на plugin-local MCP server `okno`, который приходит вместе с plugin.
- Plugin-local `okno` не переписывает legacy home-level MCP server `windows`; это отдельные слои.
- Для repo-local работы в этом репозитории предпочитай `okno`, а `windows` считай legacy/compatibility server.
- Внутренние namespace, assembly names и пути в репозитории пока могут оставаться `WinBridge`, но продуктовое имя для user-facing surface здесь — `Okno`.

## Шаги

1. Если задача меняет runtime, server, tool contract, diagnostics или verification path, сначала запусти:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1
```

2. Если нужен свежий evidence pack для shipped observe/wait path, запусти:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

3. Если smoke или direct runtime flow упал, запусти:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/investigate.ps1
```

4. Для анализа результата проверь:
   - `artifacts/smoke/<run_id>/report.json`;
   - `artifacts/diagnostics/<run_id>/summary.md`;
   - `docs/generated/project-interfaces.md`;
   - `docs/generated/commands.md`.

5. Если менялся публичный contract, lifecycle tools или diagnostics schema, синхронизируй generated docs в том же цикле:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1
```

## Definition of done

- verify/smoke выполнены по необходимости;
- evidence pack или diagnostics path сохранены;
- при изменении contract/docs generated docs синхронизированы;
- user-facing контекст использует имя `Okno`, а не `WinBridge`, там где речь идёт именно о plugin surface.
