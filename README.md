# Computer Use for Windows

Computer Use for Windows — это Codex-native Windows desktop control plugin, построенный поверх внутреннего `Okno` / `WinBridge` engine. Проект развивается как product-ready `STDIO` local process для агентных сценариев `observe -> act -> verify`, где приоритетом являются надёжность, проверяемость и предсказуемое поведение.

Снаружи продуктовым front door должен быть `computer-use-win`, а `Okno` остаётся внутренним Windows-native engine и codename-слоем.

## Что делает проект

- перечисляет и выбирает окна Windows;
- поддерживает привязку текущей сессии к окну;
- возвращает window/desktop capture с metadata и PNG artifact;
- возвращает semantic UIA snapshot выбранного окна в control view;
- умеет ждать и подтверждать live UI condition вместо хрупкого `sleep`;
- возвращает консервативный readiness/guard snapshot среды через `okno.health`;
- даёт MCP tool contract для агентного вызова;
- сохраняет структурированные диагностические артефакты;
- предоставляет локальный control plane для build/test/smoke/investigation.

Текущий вертикальный срез уже подтверждает реальный flow:

1. `initialize`
2. `tools/list`
3. `okno.health`
4. `windows.list_windows`
5. `windows.attach_window`
6. `okno.session_state`
7. `windows.capture`
8. `windows.uia_snapshot`
9. `windows.wait`

## Реализованные фичи

| Фича | Что даёт агенту | Как агент вызывает |
| --- | --- | --- |
| Runtime health | Понять, что `Okno` поднят, какой transport/contract сейчас активен, где лежат diagnostics artifacts и какие guard domains / capability paths сейчас `ready`, `degraded`, `blocked` или `unknown` без hidden enforcement. | `okno.health` |
| Список окон | Увидеть top-level окна, выбрать `hwnd`, понять заголовок, процесс и bounds. | `windows.list_windows(includeInvisible=false)` |
| Attach к окну | Зафиксировать текущее рабочее окно как session target для следующих шагов. | `windows.attach_window(hwnd=<target_hwnd>)` |
| Session state | Проверить, к какому окну сейчас прикреплена сессия. | `okno.session_state()` |
| Observe current window | Получить новый visual state выбранного окна: `structuredContent`, `image/png` и локальный PNG artifact. | `windows.list_windows(...)` -> `windows.attach_window(hwnd=<target_hwnd>)` -> `windows.capture(scope="window")` |
| Observe explicit window | Снять окно напрямую по `hwnd`, не меняя attach-контекст. | `windows.capture(scope="window", hwnd=<target_hwnd>)` |
| Observe desktop monitor | Получить monitor-level capture для общего обзора или межоконного перехода. | `windows.capture(scope="desktop")` |
| Observe semantic window state | Получить UIA snapshot окна в control view: controls, иерархию, `automationId`, `controlType`, focus metadata и structured payload без image block. | `windows.uia_snapshot(hwnd=<target_hwnd>)` или `windows.attach_window(hwnd=<target_hwnd>)` -> `windows.uia_snapshot()` |
| Wait / verify live condition | Подтвердить, что окно действительно стало active, элемент появился или исчез, текст появился, focus перешёл на нужный control или visual state реально изменился. | `windows.wait(condition=\"active_window_matches\"|\"element_exists\"|\"element_gone\"|\"text_appears\"|\"focus_is\"|\"visual_changed\", ...)` |

Это уже позволяет агенту строить базовую цепочку `observe -> verify -> act-ready`, а не только делать screenshot/capture.

## Стек

- `C# / .NET 8`
- `ModelContextProtocol` C# SDK
- `xUnit`
- `PowerShell` scripts для локального control plane

## Transport policy

- Product-ready target сейчас только `STDIO` local process.
- HTTP/URL transport не является текущим рабочим режимом.
- HTTP будет рассматриваться только после готового и стабилизированного `STDIO`.

## Быстрый старт

### Требования

- Windows 11
- .NET SDK `8.0.401` или совместимый по `global.json`
- PowerShell

### Установка после скачивания

1. Клонировать или распаковать репозиторий.
2. Перейти в корень проекта.
3. Выполнить:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

### One-command local CI

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci.ps1
```

### Обновление generated docs из manifest

```powershell
powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1
```

## Codex plugin

В репозитории теперь есть один главный public-facing Codex plugin:

- marketplace: `.agents/plugins/marketplace.json`
- plugin root: `plugins/computer-use-win/`
- plugin MCP: `plugins/computer-use-win/.mcp.json`

Этот plugin публикует quiet operator surface:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

`Okno` остаётся внутренним engine и execution substrate под этим plugin surface.

Текущий product gap после shipped action wave:

- poor-UIA apps могут уже проходить screenshot-first navigation и coordinate/semantic actions;
- text entry без доказанного editable UIA proof теперь доступен только через
  explicit `allowFocusedFallback=true` + `confirm=true`: либо fresh
  target-local focus proof и text-entry-like candidate, либо explicit
  `point` в `coordinateSpace="capture_pixels"` из последнего screenshot state
  для coordinate-confirmed Class C path. Оба пути остаются без clipboard
  default и с честным `verify_needed`;
- successor-state / action+observe закрыт explicit `observeAfter=true` на
  поддерживаемых actions: result может включать nested `successorState`,
  новый short-lived `stateToken` и screenshot image block без optimistic
  semantic success; post-action observe заново сопоставляет live window и не
  переносит stale pre-action `windowId` в nested session;
- continuity/identity UX снижает churn без ослабления proof: repeated
  unchanged `list_apps` snapshots переиспользуют strict runtime-owned
  `windowId`, а drift/replacement paths всё ещё fail-close без наивного
  публичного `hwnd + processId`.

Важно: Codex запускает установленную local plugin copy из `~/.codex/plugins/cache/.../local`, поэтому перед первой установкой plugin, после изменения runtime/layout или после reinstall нужно заново materialize-ить plugin-local runtime bundle командой `powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1`, затем пересинхронизировать install/cache copy plugin и перезапустить Codex. Repo-root hint больше не входит в public install path `computer-use-win`.

## OpenAI interop

Computer Use for Windows не должен конкурировать с `shell`, `skills`, `MCP` или built-in `computer use` из OpenAI ecosystem. Для этого репозитория правильная модель такая:

- `shell` закрывает terminal/code side;
- `Okno` закрывает внутренний Windows desktop engine side;
- `computer-use-win` даёт публичный Codex-native operator surface;
- `skills` служат routing/procedure слоем;
- `MCP` остаётся transport/integration boundary;
- built-in OpenAI `computer use` остаётся внешним compatibility target, а не заменой локальному engine path.

Практический вывод:

- для текущего `Codex app/CLI/IDE` primary local path остаётся
  `shell + computer-use-win plugin + skills`, где `Okno` работает как
  внутренний engine;
- built-in OpenAI `computer use` не является блокером для текущего продукта и не требует перестройки core runtime;
- official OpenAI docs и sample repos подтверждают, что mature structured
  harness не нужно ломать ради built-in visual loop;
- official `images-vision` guidance подтверждает, что для spatially sensitive
  computer-use screenshots стоит сохранять full-fidelity/original detail либо
  делать явный coordinate remap после downscale;
- official `computer use` guidance отдельно фиксирует screenshot-first cycle:
  первый turn часто начинается со screenshot, а после action batch harness
  должен вернуть updated screenshot как first-class image input;
- official MCP guidance для Codex и Responses API дополнительно усиливает
  narrow-surface подход: keep tool list small, use allow-list thinking where
  appropriate and не смешивать workflow-control с public operator actions;
- compatibility work нужно закладывать через action/schema discipline будущего `windows.input` и отдельный adapter-слой поверх `Okno`, а не через смешение OpenAI-specific логики с `WinBridge.Runtime` или `WinBridge.Server`.

## Где читать дальше

- [docs/product/index.md](docs/product/index.md)
- [docs/architecture/index.md](docs/architecture/index.md)
- [docs/generated/computer-use-win-interfaces.md](docs/generated/computer-use-win-interfaces.md)
- [docs/architecture/openai-computer-use-interop.md](docs/architecture/openai-computer-use-interop.md)
- [docs/generated/commands.md](docs/generated/commands.md)
- [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md)
- [docs/runbooks/investigation.md](docs/runbooks/investigation.md)
