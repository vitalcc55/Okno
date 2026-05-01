---
name: "computer-use-win"
description: "Workflow для работы с Computer Use for Windows в этом репозитории: каждый GUI turn начинать с get_app_state, предпочитать element-targeted actions, использовать observeAfter или повторно наблюдать state после действий и не автоматизировать blocked targets."
---

# Computer Use for Windows

## Когда использовать

- Нужно работать с публичным Codex surface `computer-use-win`, а не с внутренними `windows.*` engine tools.
- Нужно пройти Windows GUI workflow через `list_apps -> get_app_state -> action`.
- Нужно держать Computer Use turn discipline и app approvals.

## Важный контекст

- Этот skill рассчитан на plugin-local MCP server `computer-use-win`.
- Внутри plugin работает `Okno` engine, но снаружи user-facing product name — `Computer Use for Windows`.
- Каждый GUI turn начинай с `get_app_state`.
- Предпочитай `elementIndex` над coordinate click, если index доступен.
- После action используй `observeAfter=true` на поддерживаемых actions, делай повторный `get_app_state` или явную verify-step, а не гадай по результату.
- `type_text(allowFocusedFallback=true)` используй только с `confirm=true`: либо для уже focused poor-UIA text target с fresh target-local proof, либо с explicit `point`/`coordinateSpace` из последнего screenshot state для coordinate-confirmed fallback. Это не generic ввод в любой focused clickable control и не hidden clipboard path.
- Для coordinate-confirmed Class C path вызывай `type_text` явно:
  `point={x,y}`, `coordinateSpace="capture_pixels"`, `allowFocusedFallback=true`,
  `confirm=true`, `observeAfter=true`; считай `verify_needed` dispatch-only
  результатом и проверяй видимый текст через `successorState`/image block или
  новый `get_app_state`. Send/Enter делай отдельным подтверждённым шагом после
  visible-text proof.
- Не автоматизируй terminal apps, сам Codex и другие blocked targets.
- Все shell-команды ниже предполагают, что current working directory уже находится в корне этого репозитория.

## Шаги

1. Для публичного surface ориентируйся на loop:

```text
list_apps -> get_app_state -> click -> get_app_state
list_apps -> get_app_state -> action(observeAfter=true) -> successorState
```

2. Если задача меняет runtime, server, tool contract, diagnostics или verification path, сначала запусти:

```powershell
.\scripts\codex\verify.ps1
```

3. Если нужен свежий evidence pack для shipped runtime path, запусти:

```powershell
.\scripts\smoke.ps1
```

4. Если smoke или direct runtime flow упал, запусти:

```powershell
.\scripts\investigate.ps1
```

5. Для анализа результата проверь:
   - `artifacts/smoke/<run_id>/report.json`;
   - `artifacts/diagnostics/<run_id>/summary.md`;
   - `docs/generated/project-interfaces.md`;
   - `docs/generated/commands.md`.

6. Если менялся публичный contract, lifecycle tools или diagnostics schema, синхронизируй generated docs в том же цикле:

```powershell
.\scripts\refresh-generated-docs.ps1
```

7. Если менялся installed `computer-use-win` public surface, после publish/cache sync докажи установленную копию:

```powershell
.\scripts\codex\prove-computer-use-win-cache-install.ps1
```

## Definition of done

- verify/smoke выполнены по необходимости;
- evidence pack или diagnostics path сохранены;
- при изменении contract/docs generated docs синхронизированы;
- user-facing контекст использует имя `Computer Use for Windows`, а `Okno` упоминается как внутренний engine.
