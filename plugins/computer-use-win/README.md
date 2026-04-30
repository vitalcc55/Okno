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
- после action делать новый `get_app_state`, использовать explicit
  `observeAfter=true` на поддерживаемых actions или выполнять явную verify-step;
- не автоматизировать blocked targets.

## Ближайший known gap

- poor-UIA apps уже могут проходить screenshot-first navigation и subsequent
  actions;
- `type_text` теперь имеет explicit `allowFocusedFallback=true` path для уже
  focused poor-UIA text target: он требует `confirm=true`, fresh target-local
  focus proof и text-entry-like candidate (`edit` либо `document`/`custom` с
  tokenized text/input/edit/query/search-box hint). Это не разрешает ввод в произвольный
  focused clickable control; результат остаётся `verify_needed`/SendInput route
  без clipboard default и hidden focus guessing;
- `click`, `press_key`, `type_text`, `scroll` и `drag` теперь поддерживают
  explicit `observeAfter=true`: после committed `done` / `verify_needed`
  action result может включать nested `successorState`, новый short-lived
  `stateToken` и screenshot image block. Это снижает loop cost, но не меняет
  честный top-level action status; failed successor observe остаётся advisory
  `successorStateFailure`. Successor observe заново сопоставляет post-action
  live window и не переносит stale pre-action `windowId` в nested session.
- public instance continuity уже снижает churn: repeated unchanged
  `list_apps` snapshots переиспользуют прежний runtime-owned `windowId`;
  drift/replacement paths всё ещё fail-close, а `hwnd + processId` не стал
  public selector.
- `get_app_state` уже возвращает screenshot bytes в MCP result и локальный
  artifact, но если конкретный client не рендерит image block inline,
  screenshot preview hint остаётся отдельным UX hardening item, а не поводом
  убирать image-bearing observe result.
