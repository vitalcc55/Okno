---
name: "computer-use-win"
description: "Use when an agent needs to operate Windows apps through the public Computer Use for Windows surface for the first time: discover apps, fetch screenshot-first state, choose the right action, interpret results, and stay inside the shipped safety/verification model."
---

# Computer Use for Windows

## Overview

Этот skill нужен не для разработки `Okno`, а для **быстрого onboarding-а
нового агента**, который впервые столкнулся с публичным plugin surface
`computer-use-win`.

Главная модель работы:

```text
list_apps -> get_app_state -> action -> verify
```

Важно:

- работай через публичный surface `computer-use-win`, а не через внутренние
  `windows.*` engine tools;
- считай продукт **screenshot-first и state-first**;
- не путай successful dispatch с semantic success;
- для low-confidence actions используй `observeAfter=true` или отдельный
  повторный `get_app_state`.

## Когда использовать

- Нужно управлять Windows GUI через plugin `computer-use-win`.
- Нужно выбрать окно, получить screenshot/state и выполнить действие.
- Нужно понять, какой tool использовать: `click`, `set_value`, `type_text`,
  `press_key`, `scroll`, `perform_secondary_action` или `drag`.
- Нужно интерпретировать `verify_needed`, `successorState`,
  `successorStateFailure`, `windowId`, `stateToken` и другие product-level
  поля.

## Что доступно сейчас

Публичный surface состоит из 9 tools:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

Поддержка `observeAfter=true` сейчас есть у:

- `click`
- `press_key`
- `type_text`
- `scroll`
- `drag`

Semantic-only actions без `observeAfter`:

- `set_value`
- `perform_secondary_action`

## С чего начинать

### 1. Найди приложение

Сначала вызови `list_apps`.

Используй его, чтобы:

- найти нужный app group;
- выбрать конкретное окно из `windows[]`;
- взять `windowId` как основной public selector.

Не считай `windowId` вечным идентификатором.
Он discovery-scoped, хотя repeated unchanged snapshots now try to reuse it.

### 2. Получи action-ready state

Затем вызови `get_app_state`.

Ищи в результате:

- `session`
- `stateToken`
- `capture`
- `accessibilityTree`
- `warnings`
- screenshot image content

Если клиент не показывает screenshot inline, используй `artifactPath` как
operator/debug fallback, но source of truth для action planning всё равно
`get_app_state`.

### 3. Выбери действие

Используй такую лестницу:

1. semantic action, если proof сильный;
2. explicit physical path, если UIA слабый, но target хорошо локализован;
3. после low-confidence action сразу получай новый state через
   `observeAfter=true` или отдельный `get_app_state`.

## Как выбирать tool

### `click`

Используй:

- `elementIndex`, если semantic target доступен;
- `point` + `confirm=true`, если нужен coordinate path.

Предпочитай `elementIndex`.
Coordinate click — low-confidence path.

### `set_value`

Это preferred semantic path для settable controls.

Используй его, когда:

- control действительно выглядит settable;
- в tree/affordance есть strong semantic signal;
- ты хочешь value change, а не физическую имитацию печати.

Не заменяй `set_value` на `type_text`, если semantic path выглядит правдоподобным.

### `type_text`

Это tool для текста, но у него **несколько режимов**.

#### Обычный путь

Используй обычный `type_text`, когда:

- есть focused editable proof;
- или есть сильный focused writable target.

#### Focused fallback

Используй:

- `allowFocusedFallback=true`
- `confirm=true`

только если UIA proof слабый, но runtime всё ещё может честно доказать
target-local focus boundary для text-entry-like target.

Это не generic ввод в любое focused окно.

#### Coordinate-confirmed fallback

Используй explicit `point` только для poor-UIA / top-level-focus paths, когда
другого честного text-entry proof нет.

Форма:

```json
{
  "stateToken": "<latest token>",
  "point": { "x": 386, "y": 805 },
  "coordinateSpace": "capture_pixels",
  "text": "Тест MPC",
  "allowFocusedFallback": true,
  "confirm": true,
  "observeAfter": true
}
```

Правила:

- `point` берётся из **последнего** screenshot/app state;
- coordinate space для этой ветки — `capture_pixels`;
- `screen` для этой ветки не использовать;
- это dispatch-only path, обычно с `verify_needed`;
- hidden clipboard/paste здесь нет.

### `press_key`

Используй для:

- `Enter`
- `Tab`
- навигационных клавиш
- modifier combos

Не используй его для произвольного печатного текста, если нужен именно text insertion.

### `scroll`

Используй:

- semantic scroll path, если target семантически понятен;
- `point` + `confirm=true`, если нужен coordinate wheel path.

Если сразу нужен новый visual state, добавляй `observeAfter=true`.

### `perform_secondary_action`

Это semantic secondary action.

Не думай о нём как о generic right-click alias.

Если target poor-UIA и нужен именно физический context-menu style path,
ожидай, что этот tool может быть не тем, что тебе нужно.

### `drag`

Считай `drag` low-confidence physical action.

Хорошая практика:

- делай его только на свежем state;
- после него используй `observeAfter=true` или отдельный `get_app_state`.

## Как читать результат

### `done`

Более сильный результат, но не общий default для coordinate/poor-UIA paths.

### `verify_needed`

Это **нормальный** результат для low-confidence actions.

Он не означает “tool сломан”.

Он означает:

- dispatch произошёл;
- но semantic outcome не доказан автоматически.

Если при этом есть `successorState`, то:

- fresh state уже получен;
- ещё один немедленный `get_app_state` может быть не нужен;
- но top-level action всё равно не притворяется semantic `done`.

### `failed`

Обычно означает:

- stale state;
- missing target;
- blocked/integrity/foreground problem;
- invalid request;
- weak proof, который runtime честно не смог поднять до action-ready path.

### `approval_required`

Не обходи это “хитрым” fallback path.
Это отдельный product gate.

### `blocked`

Не автоматизируй blocked targets.

## Нормальные loops

### Базовый loop

```text
list_apps -> get_app_state -> click -> get_app_state
```

### Ускоренный post-action loop

```text
list_apps -> get_app_state -> action(observeAfter=true) -> successorState
```

### Poor-UIA typing loop

```text
list_apps
-> get_app_state
-> screenshot-first navigation
-> type_text(allowFocusedFallback=true, confirm=true, observeAfter=true)
-> successorState or get_app_state
```

### Coordinate-confirmed poor-UIA typing

```text
list_apps
-> get_app_state
-> choose capture_pixels point from screenshot
-> type_text(point, allowFocusedFallback=true, confirm=true, observeAfter=true)
-> successorState
-> press_key(Enter, observeAfter=true) if send/submit is still needed
```

## Что не делать

- Не используй внутренние `windows.*` tools как обычный product path.
- Не считай `hwnd + processId` публичным selector.
- Не думай, что `verify_needed` = failure.
- Не используй `type_text` как hidden clipboard tool.
- Не отправляй coordinate text input в `screen` coordinates.
- Не автоматизируй blocked targets.
- Не считай `observeAfter=true` semantic proof сам по себе.
- Не считай screenshot preview gap признаком отсутствия screenshot-first runtime path.

## Быстрый troubleshooting

- Если `windowId` перестал работать: повтори `list_apps`, потом `get_app_state`.
- Если `stateToken` stale: получи новый `get_app_state`.
- Если coordinate path не проходит: проверь, что point взят из последнего
  screenshot state и что target ещё в тех же bounds.
- Если `type_text` reject-ится: проверь, что ты не пытаешься использовать
  focused fallback без `confirm=true`.
- Если после action нет inline screenshot: ищи `successorState`, image content
  block и `artifactPath`.

## Для maintainers репозитория

Это не основной сценарий skill, но если ты меняешь сам plugin/runtime:

- runtime/server/verification changes:

```powershell
.\scripts\codex\verify.ps1
```

- свежий shipped evidence:

```powershell
.\scripts\smoke.ps1
```

- расследование падений:

```powershell
.\scripts\investigate.ps1
```

- refresh generated docs:

```powershell
.\scripts\refresh-generated-docs.ps1
```

- proof для cache-installed plugin copy:

```powershell
.\scripts\codex\prove-computer-use-win-cache-install.ps1
```

Если меняешь public contract, diagnostics schema или plugin-local install
surface, делай docs/generated sync в том же цикле.
