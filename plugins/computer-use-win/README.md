# Computer Use for Windows plugin

Этот plugin — главный public-facing Codex surface для продукта `Computer Use for Windows`.

Внутри он использует `Okno` / `WinBridge` как Windows-native engine, но наружу публикует quiet operator surface, а не низкоуровневые `windows.*` engine tools.

## Что plugin делает сейчас

- публикует installable plugin `computer-use-win`;
- поднимает plugin-local MCP server `computer-use-win`;
- даёт public operator surface:
  - `list_apps`
  - `get_app_state`
  - `click`
  - `press_key`
  - `set_value`
  - `type_text`
  - `scroll`
  - `perform_secondary_action`
  - `drag`
- добавляет bundled skill `computer-use-win`;
- использует repo marketplace в `.agents/plugins/marketplace.json`.

## MCP model

- plugin стартует через `powershell -NoProfile -NonInteractive`;
- launcher `run-computer-use-win-mcp.ps1` стартует только plugin-local runtime bundle `runtime/win-x64/Okno.Server.exe`;
- public profile выбирается явно через `--tool-surface-profile computer-use-win`;
- low-level `windows.*` surface остаётся внутренним execution substrate и не является главным product UX.
- structured plugin/MCP integration остаётся preferred local path там, где она
  уже есть; plugin не пытается быть raw visual shim поверх built-in OpenAI
  computer use.
- если оператору нужен ещё более узкий client-side surface, это лучше делать
  через Codex MCP config (`enabled_tools` / `disabled_tools`), а не через
  размножение plugin профилей или отдельный runtime.

## Runtime publish

Перед install/reinstall plugin или после изменения runtime/server layout подготовь plugin-local runtime bundle:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
``` 

После этого обнови install/cache copy plugin и перезапусти Codex. Установленный plugin должен запускаться только из собственной install copy и больше не зависит от repo root hint или `.tmp/.codex/artifacts/local`.

## Skill

- `skills/computer-use-win/`

Skill требует state-first discipline:

- каждый GUI turn начинать с `get_app_state`;
- считать `stateToken` короткоживущим proof-артефактом вместе с его observation envelope, а не долговечной session cache;
- expected advisory-unavailable path для playbook hints не должен ломать observation result; unexpected provider/runtime bug всё ещё materialize-ится как truthful `observation_failed`;
- предпочитать `elementIndex` над coordinate click;
- использовать coordinate click только с явным `confirm`, если semantic element не доказан;
- не смешивать action tools с workflow-control semantics;
- после action делать новый `get_app_state` или явную verify-step;
- не автоматизировать blocked targets.

## Ближайший known gap

- poor-UIA apps уже могут проходить screenshot-first navigation и subsequent
  actions;
- но `type_text` без editable UIA proof по-прежнему fail-close-ится;
- следующий узкий follow-up здесь — bounded keyboard-focus fallback без
  clipboard default и без hidden focus guessing.
- после этого следующий UX follow-up — successor-state / action+observe path,
  чтобы честный `verify_needed` не требовал полного ручного reobserve после
  каждого low-confidence action.
- `windowId` safety semantics не нужно размывать до `hwnd + processId`, но
  continuity UX нужно усиливать отдельно, чтобы агент меньше тратил turns на
  повторные `list_apps` loops.
- `get_app_state` уже возвращает screenshot bytes в MCP result и локальный
  artifact, но если конкретный client не рендерит image block inline,
  screenshot preview hint остаётся отдельным UX hardening item, а не поводом
  убирать image-bearing observe result.
