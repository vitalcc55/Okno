# Okno

[English](README.md) | [**Русский**](README.ru.md) | [简体中文](README.zh-CN.md)

> Windows-native MCP-рантайм для AI-агентов
>
> **Computer Use for Windows** — первая публичная возможность Okno для
> автоматизации Windows desktop applications поверх `MCP` / `STDIO`.

| Платформа | Транспорт | Возможность | Среда | Базовая модель выполнения |
| --- | --- | --- | --- | --- |
| Windows 11 | Локальный MCP поверх `STDIO` | `Computer Use for Windows` | C# / .NET 8 | UIA-семантика + проверка по screenshot |

## Зачем нужен Okno

Okno превращает реальные Windows desktop applications в управляемый рабочий
слой для AI-агентов. Он рассчитан на сценарии, где shell-команд,
автоматизации только внутри браузера или API приложения уже недостаточно.

Текущая публичная возможность, **Computer Use for Windows**, позволяет агенту
находить окна, получать состояние, готовое к действию, работать с UI и затем
подтверждать результат, а не считать любой отправленный клик или ввод текста
успехом по умолчанию.

Это не универсальный набор скриптовых утилит для Windows и не проект
автоматизации браузера, замаскированный под desktop. Это Windows-native
рантайм для работы AI-агентов с GUI, у которого текущий основной transport —
`MCP over STDIO`.

## Что Computer Use for Windows уже умеет

Сегодня публичный набор инструментов уже умеет:

- находить запущенные desktop apps и окна через `list_apps`;
- получать состояние окна со screenshot через `get_app_state`;
- возвращать accessibility tree, геометрию, `captureReference` и короткоживущий
  `stateToken`;
- выполнять `click`, `press_key`, `set_value`, `type_text`, `scroll`,
  `perform_secondary_action` и `drag`;
- возвращать `verify_needed`, если действие слабо доказано, вместо
  притворного semantic success;
- возвращать `successorState` на поддерживаемых действиях через
  `observeAfter=true`;
- работать и с приложениями, где UIA даёт сильную семантику, и с более
  сложными Qt / Electron / нестандартными GUI через ограниченные physical
  fallback paths.

## Как это устроено

Okno работает локально на Windows как MCP-рантайм. Текущий основной
product-ready transport — **локальный `STDIO`**, а наиболее удобный
поддержанный сценарий интеграции сейчас идёт через Codex plugin из этого
репозитория.

`Computer Use for Windows` — это текущий публичный слой возможностей. Внутри он
использует рантайм Okno и внутренние компоненты `WinBridge`, но наружу
публикует тихий и ограниченный операторский интерфейс, а не делает
low-level `windows.*` tools главной продуктовой историей.

Нормальный цикл работы выглядит так:

```text
list_apps -> get_app_state -> action -> verify
```

На практике это значит:

1. найти нужное окно;
2. получить свежее состояние со screenshot и accessibility data;
3. выбрать самый сильный доступный путь действия;
4. подтвердить результат через `observeAfter=true` или новый `get_app_state`.

## Чем Okno отличается

Okno держится на четырёх продуктовых правилах.

| Принцип | Что это значит на практике |
| --- | --- |
| Сильный semantic path | Если цель хорошо доказана, приоритет у UIA-backed действий. |
| Screenshot как часть доказательства | Screenshot нужен не для красоты, а как часть observation и verification. |
| Ограниченный physical fallback | Если UIA слабый, рантайм всё равно может действовать через guarded physical paths. |
| Честный результат | Для слабодоказанных действий возвращается `verify_needed`, а не фальшивый успех. |

За счёт этого Okno лучше подходит для реальной Windows GUI automation, чем
инструменты, которые умеют только слать координаты, и лучше подходит для
poor-UIA целей, чем инструменты, которые предполагают, что semantic
automation надёжна везде.

## Где Okno подходит лучше всего

Okno особенно уместен, если тебе нужны:

- локальная автоматизация Windows desktop applications для AI-агентов;
- Windows-native MCP-рантайм, а не инструмент только для браузера;
- Codex-friendly операторский интерфейс для реальных desktop applications;
- verification-oriented execution model для нестабильного или слабосемантического UI.

Okno не является главным выбором, если тебе нужны:

- browser-first DOM automation;
- one-click consumer distribution без локальной подготовки;
- полноценная enterprise RPA orchestration и low-code workflow tooling.

## Быстрый старт

Самый короткий поддержанный путь сегодня — **Codex на Windows** с локальным
plugin, который поставляется из этого репозитория.

### Что нужно заранее

- Windows 11
- Codex на Windows
- PowerShell
- доступ к сети, если установленной копии плагина при первом запуске
  понадобится подтянуть pinned runtime release

### 1. Склонировать репозиторий

```powershell
git clone https://github.com/vitalcc55/Okno.git
cd Okno
```

### 2. Установить локальный plugin из записи в marketplace репозитория

Точки входа в репозитории:

- [.agents/plugins/marketplace.json](.agents/plugins/marketplace.json)
- [plugins/computer-use-win](plugins/computer-use-win)
- [plugins/computer-use-win/.codex-plugin/plugin.json](plugins/computer-use-win/.codex-plugin/plugin.json)
- [plugins/computer-use-win/.mcp.json](plugins/computer-use-win/.mcp.json)

### 3. Перезапустить Codex или открыть новый thread

Установленный plugin запускается из Codex plugin cache, а не из корня
репозитория. Если в установленной копии уже есть валидированный runtime bundle,
launcher использует его напрямую. Если runtime bundle отсутствует или
повреждён, launcher берёт pinned runtime release, описанный в
[plugins/computer-use-win/runtime-release.json](plugins/computer-use-win/runtime-release.json),
проверяет SHA256 и `okno-runtime-bundle-manifest.json`, и только после этого
поднимает MCP host.

### 4. Пройти первый рабочий цикл

1. вызвать `list_apps`;
2. выбрать `windowId`;
3. вызвать `get_app_state(windowId=...)`;
4. выполнить действие;
5. подтвердить результат через `observeAfter=true` или новый `get_app_state`.

Для обычных MCP-клиентов по `STDIO` и для maintainer-сценария из исходников см.
[docs/runbooks/computer-use-win-install.md](docs/runbooks/computer-use-win-install.md).
Мейнтейнеры по-прежнему могут явно собрать plugin-local bundle командой
`scripts/codex/publish-computer-use-win-plugin.ps1`.

## Публичный набор инструментов

| Инструмент | Назначение |
| --- | --- |
| `list_apps` | Найти запущенные desktop applications и окна. |
| `get_app_state` | Вернуть состояние со screenshot, границами, токенами и accessibility data. |
| `click` | Активировать semantic target или подтверждённую точку. |
| `press_key` | Отправить явный keyboard input. |
| `set_value` | Использовать semantic value-setting path там, где он поддерживается. |
| `type_text` | Ввести текст через semantic path или guarded fallback typing path. |
| `scroll` | Выполнить scroll и по возможности подтвердить движение. |
| `perform_secondary_action` | Выполнить secondary semantic action вроде toggle или expand-collapse. |
| `drag` | Выполнить bounded drag с явным proof по source и destination. |

Ключевые поля результата:

- `windowId` — это public selector для текущего discovery state, а не вечный
  идентификатор окна.
- `stateToken` — короткоживущий proof-артефакт последнего observation state.
- `verify_needed` означает, что dispatch произошёл, но semantic outcome ещё
  нужно подтвердить наблюдением.
- `successorState` — это уже полученный post-action state, если
  `observeAfter=true` сработал успешно.

## Доверие, безопасность и границы

- Рантайм работает в **реальной** Windows desktop session.
- Physical mouse и keyboard input — общие ресурсы системы.
- Проект не делает вид, что даёт “второй независимый системный курсор”.
- Для weak-semantic или poor-UIA targets может понадобиться bounded physical fallback.
- Blocked или sensitive targets всё равно требуют явной policy discipline.
- Слабодоказанные действия надо воспринимать как `dispatch + verify`, а не как
  слепой успех.

## Карта документации

Если нужен не только front page:

- product docs: [docs/product/index.md](docs/product/index.md)
- product spec: [docs/product/okno-spec.md](docs/product/okno-spec.md)
- roadmap: [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md)
- product vision: [docs/product/okno-vision.md](docs/product/okno-vision.md)
- architecture docs: [docs/architecture/index.md](docs/architecture/index.md)
- public capability docs:
  [plugins/computer-use-win/README.md](plugins/computer-use-win/README.md)
- пути установки:
  [docs/runbooks/computer-use-win-install.md](docs/runbooks/computer-use-win-install.md)
- generated interfaces:
  [docs/generated/computer-use-win-interfaces.md](docs/generated/computer-use-win-interfaces.md)
- commands inventory: [docs/generated/commands.md](docs/generated/commands.md)

## Статус

Okno уже можно использовать сегодня как локальный Windows plugin/runtime для
Codex и как локальную MCP surface поверх `STDIO`.

Что уже выглядит сильно:

- публичная возможность уже shipped и устанавливается из source repo;
- для обычных MCP-клиентов уже определён release-backed runtime contract;
- runtime bundle и plugin install surface уже существуют;
- public contract, smoke path и verification loop реальны;
- проект уже давно вышел из research-prototype стадии.

Что пока честно остаётся правдой:

- установка всё ещё developer-oriented;
- путь установки плагина в Codex сегодня всё ещё опирается на checkout
  репозитория;
- GitHub runtime releases должны существовать, прежде чем сценарий установки
  plugin без локально собранного runtime станет основной публичной историей;
- one-click consumer distribution пока не является текущей формой продукта.

## Лицензия

Репозиторий распространяется под лицензией **GNU Affero General Public License
v3.0 or later** (`AGPL-3.0-or-later`).

Copyright © 2025–2026 Vlasov Vitaly

- [LICENSE](LICENSE)
- [LICENSES/AGPL-3.0-or-later.txt](LICENSES/AGPL-3.0-or-later.txt)
- [REUSE.toml](REUSE.toml)
