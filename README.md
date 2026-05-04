# Computer Use for Windows

Computer Use for Windows — это локальный Codex plugin для управления Windows
desktop apps через screenshot-first, state-first loop:

```text
list_apps -> get_app_state -> action -> verify
```

Если коротко: этот репозиторий даёт тебе installable plugin
`computer-use-win`, который умеет находить окна, возвращать action-ready state
со screenshot и accessibility tree, а затем выполнять `click`, `press_key`,
`set_value`, `type_text`, `scroll`, `perform_secondary_action` и `drag`.

Внутри plugin использует `Okno` / `WinBridge` как Windows-native engine, но
наружу публикует тихий operator surface, а не низкоуровневые `windows.*`
engine tools.

## What You Get

- находить запущенные Windows apps и их окна через `list_apps`;
- получать screenshot-first app state через `get_app_state`;
- возвращать `stateToken`, `captureReference`, bounds и compact accessibility
  tree;
- выполнять `click`, `press_key`, `set_value`, `type_text`, `scroll`,
  `perform_secondary_action`, `drag`;
- для low-confidence actions возвращать `verify_needed`, а не притворяться
  semantic success;
- на поддерживаемых actions делать post-action observe через
  `observeAfter=true` и возвращать nested `successorState`;
- работать не только с strong-UIA apps, но и с poor-UIA / custom GUI через
  bounded screenshot-first + physical input paths.

Текущий public tool surface:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

Дополнительно:

- plugin уже умеет работать не только со strong-UIA apps, но и с poor-UIA /
  custom GUI через bounded screenshot-first + physical input paths;
- low-confidence actions не притворяются semantic success и честно отвечают
  `verify_needed`;
- поддерживаемые actions могут сразу вернуть fresh successor state через
  `observeAfter=true`.

## Install

Для текущего install-from-source path нужны:

- Windows 11;
- Codex на Windows;
- .NET SDK `8.0.401` или совместимый по [global.json](global.json);
- PowerShell.

### 1. Склонируй репозиторий

```powershell
git clone https://github.com/vitalcc55/Okno.git
cd Okno
```

### 2. Подготовь plugin-local runtime bundle

```powershell
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
```

Это materialize-ит self-contained runtime bundle в
[plugins/computer-use-win/runtime/win-x64](plugins/computer-use-win/runtime/win-x64).

### 3. Установи local plugin из repo marketplace

В репозитории уже есть local marketplace entry:

- [`.agents/plugins/marketplace.json`](.agents/plugins/marketplace.json)
- plugin id: `computer-use-win-local`
- plugin name: `computer-use-win`

Install surface самого plugin лежит здесь:

- [plugins/computer-use-win](plugins/computer-use-win)
- [plugins/computer-use-win/.codex-plugin/plugin.json](plugins/computer-use-win/.codex-plugin/plugin.json)
- [plugins/computer-use-win/.mcp.json](plugins/computer-use-win/.mcp.json)

### 4. Перезапусти Codex или открой новый thread

Установленная copy plugin запускается не из repo root, а из Codex plugin cache.
Если после install/reinstall surface выглядит stale, перезапусти Codex или
открой новый thread.

## Quick Start

Первый рабочий flow:

1. `list_apps`
2. выбрать окно по `windowId`
3. `get_app_state`
4. выбрать действие
5. `observeAfter=true` или новый `get_app_state`

Нормальный сценарий:

1. Вызвать `list_apps` и найти нужное окно.
2. Вызвать `get_app_state(windowId=...)`.
3. Посмотреть на screenshot, `bounds`, `stateToken`, `actions`,
   accessibility tree.
4. Если есть strong semantic target, предпочесть:
   - `set_value`
   - `perform_secondary_action`
   - `click(elementIndex=...)`
5. Если UIA слабый, использовать bounded physical path:
   - `click(point=..., confirm=true)`
   - `type_text(..., allowFocusedFallback=true, confirm=true)`
6. Для low-confidence path сразу получить новый state:
   - `observeAfter=true`
   - или отдельный `get_app_state`

Важные поля:

- `windowId` — public selector для текущего discovery snapshot, но не “вечный”
  идентификатор окна.
- `stateToken` — short-lived proof артефакт последнего observation state.
- `verify_needed` — не ошибка; это честный ответ, что dispatch произошёл, но
  semantic success нужно подтвердить наблюдением.
- `successorState` — уже полученный свежий state после action, если был
  `observeAfter=true`.

Poor-UIA / custom GUI:

Проект не строится вокруг предположения “UIA хороший везде”.

Для Qt / Electron / React / custom UI / weak-semantic surfaces текущая
стратегия такая:

- screenshot-first observation;
- semantic path, если proof сильный;
- bounded physical path, если proof слабый, но target достаточно локализован;
- no hidden clipboard default;
- no optimistic `done` только по факту dispatch;
- successor observation после low-confidence actions.

Для `type_text` сейчас уже есть:

- ordinary focused editable path;
- focused fallback через `allowFocusedFallback=true` + `confirm=true`;
- coordinate-confirmed path через explicit `point` в
  `coordinateSpace="capture_pixels"` из последнего screenshot state.

Это делает plugin практичнее для реальных desktop apps, но не отменяет того,
что physical input остаётся общим ресурсом Windows-сессии.

## Status

На сегодня проект уже **годится для release в git** и **уже пригоден для
реального использования как local Codex plugin**.

Что уже хорошо:

- это уже не research prototype;
- plugin install surface, self-contained runtime bundle и cache-installed proof
  в репозитории есть;
- plugin можно поставить и использовать с другого Windows ПК;
- public contract, install scripts и install-surface tests уже существуют.

Что пока не идеально:

- install UX ещё **developer-oriented**, а не polished consumer-grade
  one-click distribution;
- лучший поддержанный путь сейчас — **локальный checkout репозитория**;
- перед install/reinstall нужно materialize-ить plugin-local runtime bundle;
- широкая end-user distribution story пока слабее, чем сам runtime/plugin
  contract.

Честный итог:

- **да**, проект уже можно публиковать и использовать;
- **да**, его уже можно скачать как plugin source repo и запустить;
- **нет**, это ещё не самая удобная форма для массового пользователя, который
  ожидает “установил одним кликом и сразу всё готово”.

Ограничения и безопасность:

- plugin работает в **реальной** Windows desktop session;
- он может двигать системный курсор и отправлять клавиатурный ввод;
- built-in OpenAI `computer use` не заменяет этот plugin;
- project не пытается строить “второй системный курсор”;
- blocked targets и safety/policy boundaries всё ещё важны;
- install UX пока не consumer-grade и остаётся ближе к advanced local tool.

Если тебе нужна polished end-user distribution story, это ещё отдельный слой
работы поверх текущего source repo.

## Developer Links

Если тебе нужен не только plugin, а сам engine/runtime:

- продуктовый source of truth:
  - [docs/product/index.md](docs/product/index.md)
  - [docs/product/okno-spec.md](docs/product/okno-spec.md)
  - [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md)
  - [docs/product/okno-vision.md](docs/product/okno-vision.md)
- архитектура:
  - [docs/architecture/index.md](docs/architecture/index.md)
  - [docs/architecture/computer-use-win-surface.md](docs/architecture/computer-use-win-surface.md)
  - [docs/architecture/openai-computer-use-interop.md](docs/architecture/openai-computer-use-interop.md)
  - [docs/architecture/reference-research-policy.md](docs/architecture/reference-research-policy.md)
- generated maps:
  - [docs/generated/computer-use-win-interfaces.md](docs/generated/computer-use-win-interfaces.md)
  - [docs/generated/commands.md](docs/generated/commands.md)
  - [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md)
- plugin docs:
  - [plugins/computer-use-win/README.md](plugins/computer-use-win/README.md)
  - [plugins/computer-use-win/skills/computer-use-win/SKILL.md](plugins/computer-use-win/skills/computer-use-win/SKILL.md)

### Базовые команды разработчика

```powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1
```

One-command local CI:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci.ps1
```

Codex-side verification:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1
```

## Лицензия

Этот репозиторий лицензирован под **GNU Affero General Public License v3.0 or
later** (`AGPL-3.0-or-later`).

Copyright © 2025–2026 Власов Виталий Андреевич
<vital.cc55@gmail.com>

Полный текст лицензии:

- [LICENSE](LICENSE)
- [LICENSES/AGPL-3.0-or-later.txt](LICENSES/AGPL-3.0-or-later.txt)

REUSE-style metadata:

- C# files в `src/**/*.cs` и `tests/**/*.cs` используют
  `SPDX-FileCopyrightText` + `SPDX-License-Identifier`;
- repo-level metadata лежит в
  [REUSE.toml](REUSE.toml);
- header check:
  [scripts/check-csharp-license-headers.ps1](scripts/check-csharp-license-headers.ps1)
