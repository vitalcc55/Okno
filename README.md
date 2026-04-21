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

`Okno` остаётся внутренним engine и execution substrate под этим plugin surface.

Важно: Codex запускает установленную local plugin copy из `~/.codex/plugins/cache/.../local`, поэтому перед первой установкой plugin, после перемещения checkout или после изменения plugin layout нужно обновить repo-root hint командой `powershell -ExecutionPolicy Bypass -File scripts/codex/write-computer-use-win-plugin-repo-root-hint.ps1`, затем пересинхронизировать install/cache copy plugin и перезапустить Codex.

## OpenAI interop

Computer Use for Windows не должен конкурировать с `shell`, `skills`, `MCP` или built-in `computer use` из OpenAI ecosystem. Для этого репозитория правильная модель такая:

- `shell` закрывает terminal/code side;
- `Okno` закрывает внутренний Windows desktop engine side;
- `computer-use-win` даёт публичный Codex-native operator surface;
- `skills` служат routing/procedure слоем;
- `MCP` остаётся transport/integration boundary;
- built-in OpenAI `computer use` остаётся внешним compatibility target, а не заменой локальному engine path.

Практический вывод:

- для текущего `Codex app/CLI/IDE` primary local path остаётся `shell + Okno MCP/plugin`;
- built-in OpenAI `computer use` не является блокером для текущего продукта и не требует перестройки core runtime;
- compatibility work нужно закладывать через action/schema discipline будущего `windows.input` и отдельный adapter-слой поверх `Okno`, а не через смешение OpenAI-specific логики с `WinBridge.Runtime` или `WinBridge.Server`.

## Где читать дальше

- [docs/product/index.md](docs/product/index.md)
- [docs/architecture/index.md](docs/architecture/index.md)
- [docs/generated/computer-use-win-interfaces.md](docs/generated/computer-use-win-interfaces.md)
- [docs/architecture/openai-computer-use-interop.md](docs/architecture/openai-computer-use-interop.md)
- [docs/generated/commands.md](docs/generated/commands.md)
- [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md)
- [docs/runbooks/investigation.md](docs/runbooks/investigation.md)
