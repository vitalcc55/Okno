# Okno V1 Spec

_Почти техническое ТЗ для Okno V1 под Windows 11._

## 0. Назначение

`Okno V1` — это мой первый рабочий локальный runtime для управления **этим Windows 11 ПК** по поручению пользователя.

Он должен дать мне надёжный базовый контур для:
- наблюдения за экраном и окнами;
- получения семантического состояния UI там, где это возможно;
- ввода текста и нажатия кнопок;
- переключения окон и диалогов;
- выполнения коротких и средних рабочих сценариев в браузерах и desktop-приложениях;
- обязательной проверки результата после действий.

Это не “идеальная конечная система”, а **надёжное ядро V1**, на которое можно потом наращивать V2/V3.

---

## 1. Цели V1

## 1.1. Что V1 обязан уметь

### Наблюдение
- Снимать **скриншот всего рабочего стола**.
- Снимать **скриншот конкретного окна**.
- Возвращать мне снимок в пригодном для анализа виде.
- Уметь сообщать, какое окно активное.

### Контекст окна
- Список видимых окон.
- Фокусировка / переключение на нужное окно.
- Привязка текущей рабочей сессии к конкретному окну.

### Семантика UI
- Получать UIA snapshot для explicit, attached или active окна с precedence `explicit -> attached -> active`.
- Искать элементы по базовым признакам:
  - name,
  - automation id,
  - control type.
- Выполнять базовые UIA-действия:
  - invoke,
  - focus,
  - set value,
  - toggle/select (если доступно).

### Низкоуровневый fallback
- Click / double click / right click.
- Move / drag.
- Scroll.
- Type.
- Keypress / hotkey.

### Ожидание и проверка
- Ждать появления/исчезновения элемента.
- Ждать смены активного окна.
- Ждать смены визуального состояния области/окна.
- Ждать появления текста или изменения текста в доступном элементе.
- Возвращать мне статус: `done / timeout / ambiguous / failed`.

### Текст и clipboard
- Уметь вставлять текст через clipboard.
- Уметь читать/задавать clipboard.
- Уметь выбирать между `type` и `paste` по моей команде.

### Диалоги
- Обнаруживать модальные окна / поверхностные диалоги хотя бы на базовом уровне.
- Позволять фокусировать диалог и работать с ним как с отдельным окном.

---

## 1.2. Что V1 сознательно НЕ обязан уметь

- Полноценный DOM-aware browser mode.
- Глубокую OCR-подсистему как основной режим.
- Сложные макросы и запись/реплей.
- Автономное выполнение длинных планов внутри самого bridge.
- Сетевой remote-host режим.
- Управление несколькими независимыми пользовательскими сессиями.
- Сложный policy-engine с множеством ролей.
- Полноценную поддержку elevated/UAC-экзотики во всех вариантах.
- Универсальный API для файловой системы и shell как центральный слой.

Это всё может появиться позже, но не должно размывать V1.

---

## 2. Главные инженерные принципы V1

## 2.1. UIA-first

Основной путь действий:
1. сначала понять, можно ли решить шаг через UI Automation;
2. только если нельзя — использовать input по координатам.

## 2.2. Screenshot-first verification

После каждого важного действия V1 должен позволять мне:
- получить новый снимок;
- сравнить состояние до/после;
- принять решение о следующем шаге.

## 2.3. Verify-always

Успех действия — это не факт вызова input/API, а подтверждённое изменение состояния.

## 2.4. Один компактный контракт

V1 не должен давать 50 разношёрстных инструментов.
Нужен компактный, понятный набор primitives.

## 2.5. Локальность и предсказуемость

V1 должен быть локальным runtime на этом ПК, а не распределённой системой.

---

## 3. Выбор конкретного техстека

## 3.1. Язык и платформа

### Выбор для V1: **C# / .NET 8+**

### Почему именно так

- Windows-native среда.
- Хорошая интеграция с UI Automation и Win32 API.
- Удобнее для аккуратной работы с окнами, фокусом, типами, interop.
- Более естественная база для серьёзного Windows runtime, чем Python для долгоживущего ядра.
- Ближе по духу к “правильной архитектуре”, чем быстрый скриптовый мост.

### Почему не Python как основа V1

Python хорош для прототипов, но для моего V1 приоритет — не скорость наброска, а более правильная база.

Python можно использовать:
- для экспериментов;
- для быстрой разведки;
- для проверки гипотез.

Но **ядро V1** лучше строить на `.NET`.

---

## 3.2. Runtime-модель

### Выбор

Локальный **MCP server/runtime**, работающий в интерактивной Windows-сессии пользователя.

### Почему

- Мне нужен чёткий tool-контракт.
- MCP хорошо ложится на мою агентную модель.
- Это позволяет мне вызывать Okno как набор инструментов, а не как хаотичный shell.
- Позже это проще расширять до V2/V3.

### Транспорт для V1

#### Базовый транспорт: **STDIO**

Почему:
- проще поднять и стабилизировать;
- меньше сетевой и auth-сложности;
- хорошо подходит для локального runtime;
- проще отлаживать на раннем этапе.

#### Не в MVP V1

- Streamable HTTP как основной режим можно отложить на V2.

---

## 3.3. UI слой

### Основной semantic layer: **Microsoft UI Automation**

Использовать как базу для:
- snapshot дерева;
- поиска элементов;
- семантических действий.

### Обязательные функции V1

- Получение root/active window element.
- Обход дерева на разумную глубину.
- Фильтрация по `name`, `automation id`, `control type`.
- Действия через control patterns, когда доступны.

---

## 3.4. Capture слой

### Выбор для V1

#### Desktop capture
Использовать как базовый обзорный режим.

#### Window capture
Использовать как основной рабочий режим после attach к окну.

### Технический ориентир

- `Windows.Graphics.Capture` как приоритетный путь для capture окна.
- Первая реализация `window` и `desktop monitor` capture может идти через единый `Windows.Graphics.Capture` backend.
- Если `desktop monitor` target не выдаёт стабильный frame вовремя или `Windows.Graphics.Capture` недоступен в текущей сессии, допустим native fallback на screen copy без смены MCP-контракта.
- Для `window capture` неверный или minimизированный target должен давать явный tool-level error, а не screen-copy успех с чужими пикселями.

### Требования к результату capture

Каждый снимок должен по возможности сопровождаться метаданными:
- timestamp;
- scope (`desktop` / `window`);
- window title / hwnd, если есть;
- bounds;
- явный `coordinateSpace`;
- window DPI только там, где target действительно является окном.

---

## 3.5. Input слой

### Выбор

Win32 input primitives (`SendInput`-style модель) как fallback-слой.

### Что должно быть в V1

- `click`
- `double_click`
- `right_click`
- `move`
- `drag`
- `scroll`
- `type`
- `keypress`
- `hotkey`

### Что важно

- нормализация координат;
- работа только в явном контексте окна/экрана;
- понятные ошибки при проблемах с фокусом;
- awareness of integrity/focus issues.

---

## 3.6. Wait / verify слой

### Для V1 это MUST HAVE, не nice-to-have

Для V1 нужен один публичный tool ожидания: `windows.wait`.

Он должен закрывать условия:
- active window matches;
- element exists;
- element gone;
- text appears;
- visual changed;
- focus is.

Отдельный zoo из `wait_for_*` tools в V1 не нужен: это должны быть condition/mode внутри одного wait tool.

### Минимальная реализация проверки

- по UIA-состоянию;
- по заголовку/активному окну;
- по изменению изображения;
- по повторному capture.
- с target policy `explicit -> attached -> active`;
- без hidden activation fallback.

---

## 3.7. Clipboard слой

### Входит в V1

Потому что для реальных задач пользователя это практично и часто надёжнее, чем посимвольный набор.

Нужны:
- `windows.clipboard_get`
- `windows.clipboard_set`
- `paste` как операция внутри `windows.input`

---

## 4. Набор инструментов V1

Ниже — рекомендуемый минимальный контракт.

## 4.1. Window/session primitives

### `windows.list_monitors`
Возвращает:
- count;
- monitor id;
- friendly name;
- gdi device name;
- bounds / work area;
- diagnostics.identityMode;
- diagnostics.failedStage / errorCode / errorName для primary reason code, если есть fallback или partial degradation detail;
- isPrimary.

Примечание:
- список monitor targets должен отражать captureable desktop views текущей topology, а не “все физические outputs любой ценой”.

### `windows.list_windows`
Возвращает:
- hwnd/id;
- title;
- process name (если доступно);
- bounds;
- effectiveDpi;
- dpiScale;
- windowState;
- monitorId;
- monitorFriendlyName;
- isForeground;
- isVisible.

### `windows.attach_window`
Позволяет выбрать текущее рабочее окно:
- по hwnd/id;
- по regex/title;
- по process name.

Результат:
- attached target;
- success/failure reason.

### `windows.focus_window`
Явно поднимает окно в foreground.

### `windows.activate_window`
Явно делает окно usable target:
- для minimизированного окна сначала делает restore;
- затем пытается подтвердить foreground focus;
- используется как основной путь перед `window capture`, `input` и `wait`.

Если Windows не подтверждает foreground после restore, допустим честный `ambiguous`, а не ложный `done`.

---

## 4.2. Observation primitives

### `windows.capture`
Аргументы:
- `scope`: `desktop | window`
- `hwnd`: optional явная цель окна; для `window` capture выбирает само окно, для `desktop` capture выбирает monitor этого окна, если `monitorId` не задан
- `monitorId`: optional явная цель для `desktop` capture
- для `window` без `hwnd` используется attached window
- для `desktop` с `hwnd` и без `monitorId` используется monitor указанного окна
- для `desktop` с `monitorId` используется именно выбранный monitor без silent fallback
- для `desktop` без `monitorId` и без `hwnd` используется monitor attached window или primary monitor

Возвращает:
- image;
- metadata;
- local artifact path.
- monitor metadata.
- `coordinateSpace = physical_pixels`.
- `effectiveDpi` и derived `dpiScale` только для `window` capture.

### `windows.uia_snapshot`
Аргументы:
- `hwnd`: optional explicit top-level window target;
- если `hwnd` не передан, target policy: `attached -> active`;
- при explicit `hwnd <= 0` или stale explicit target tool возвращает `targetFailureCode = stale_explicit_target` без fallback;
- `depth`: bounded control-view depth, `>= 0`;
- `maxNodes`: bounded node budget, `1..1024`.

Invariant:
- `resolved target` определяет, какой `HWND` runtime пытается snapshot-ить;
- `window` в результате и artifact относится к metadata, которую runtime публикует после фактического snapshot path;
- эти два смысла не должны смешиваться в один stale descriptor.

Возвращает:
- `structuredContent` + один `TextContentBlock`, без image block;
- `status`, `reason`, `targetSource`, `targetFailureCode`;
- runtime-observed window metadata, если target удалось реально переobserve-ить во время snapshot;
- `window` отсутствует на unobserved failure path;
- отдельные поля `window` могут отсутствовать, если runtime их не наблюдал честно и не может подтвердить без stale fallback;
- requested depth/node budget и realized traversal metadata;
- `artifactPath` для JSON evidence в diagnostics run directory;
- root element/subtree с `element_id`, `name`, `automation_id`, `control_type`, `bounding_rect`, `patterns`.

---

## 4.3. Semantic action primitives

### `windows.uia_action`
Аргументы:
- `element_id`
- `action`
- optional `value`

Поддержать в V1:
- `invoke`
- `focus`
- `set_value`
- `toggle`
- `select`

Результат:
- `done | failed | unsupported | ambiguous`
- reason

---

## 4.4. Low-level action primitives

### `windows.input`
Аргументы:
- sequence of actions.

Поддержать:
- move;
- click;
- double_click;
- right_click;
- drag;
- scroll;
- type;
- keypress;
- hotkey;
- paste.

---

## 4.5. Wait/verify primitives

### `windows.wait`
Аргументы:
- `condition`
- nested `selector` object с полями `name`, `automationId`, `controlType` при необходимости;
- optional `expectedText` для `text_appears`;
- optional explicit target (`hwnd`);
- `timeoutMs`.

Target policy:
- `explicit -> attached -> active`
- без silent fallback из stale explicit/attached target;
- без hidden activation fallback.

Поддержать условия:
- active window matches;
- element exists;
- element gone;
- text appears;
- visual changed;
- focus is.

Результат:
- `done | timeout | ambiguous | failed`
- `structuredContent` + один `TextContentBlock`, без image block;
- `artifactPath` для JSON evidence в diagnostics run directory;
- для `visual_changed` `lastObserved.visualEvidenceStatus` сообщает итог evidence stage (`materialized | timeout | failed | skipped`), а `visualBaselineArtifactPath` / `visualCurrentArtifactPath` остаются optional referenced PNG artifacts.

---

## 4.6. Clipboard primitives

### `windows.clipboard_get`
### `windows.clipboard_set`

---

## 5. Внутренние компоненты V1

## 5.1. Session manager

Отвечает за:
- текущий attached window;
- текущий режим (`desktop` / `window`);
- last known snapshot metadata;
- history последних шагов.

## 5.2. Window manager

Отвечает за:
- перечисление окон;
- foreground/focus;
- поиск target window.

## 5.3. UIA service

Отвечает за:
- snapshot дерева;
- поиск элементов;
- semantic actions.

## 5.4. Capture service

Отвечает за:
- desktop capture;
- window capture;
- metadata.

## 5.5. Input service

Отвечает за:
- mouse/keyboard actions;
- coordinate normalization;
- безопасное исполнение последовательностей.

## 5.6. Wait/verify service

Отвечает за:
- polling/verification loop;
- state comparison;
- timeout handling.

## 5.7. Clipboard service

Отвечает за:
- get/set/paste.

## 5.8. Audit/log service

V1 должен вести понятный локальный лог:
- какие инструменты вызывались;
- над каким окном;
- какой был результат;
- были ли ошибки/таймауты.

Это нужно не для бюрократии, а для отладки и разборов “почему шаг не сработал”.

---

## 6. MVP-границы V1

## 6.1. Что считается “готовым V1”

V1 можно считать состоявшимся, если я стабильно умею делать такие сценарии:

### Сценарий A — desktop form
1. Найти нужное окно.
2. Переключиться в него.
3. Сделать скрин.
4. Найти поле/кнопку.
5. Вставить или набрать текст.
6. Нажать кнопку.
7. Дождаться изменения состояния.
8. Прислать пользователю подтверждающий скрин.

### Сценарий B — browser task через окно браузера
1. Найти окно браузера.
2. Переключиться на него.
3. Переключить вкладку или сфокусировать нужную область.
4. Ввести текст в форму.
5. Нажать кнопку/переключить вкладку.
6. Дождаться результата.
7. Прислать скрин состояния.

### Сценарий C — dialog handling
1. Обнаружить модальный диалог.
2. Сфокусировать его.
3. Нажать нужную кнопку или заполнить поле.
4. Проверить, что диалог исчез/изменился.

---

## 6.2. Что НЕ должно задерживать релиз V1

- Идеальный OCR.
- Универсальные стабильные селекторы для всех фреймворков.
- Поддержка всех edge-cases UIA.
- Remote dashboard.
- Полноценный browser DOM bridge.
- Макрорекордер.
- Сложная ACL/role policy model.

Если ядро уже даёт устойчивые рабочие сценарии, V1 должен считаться успешным.

---

## 7. Нефункциональные требования

## 7.1. Производительность

- Capture должен быть достаточно быстрым для коротких итераций.
- Snapshot и wait не должны превращать базовую задачу в мучительно медленный процесс.
- В нормальном сценарии агентный шаг должен ощущаться как “рабочий”, а не как “каждый клик вечность”.

## 7.2. Надёжность

- Лучше меньше инструментов, но надёжных.
- Лучше явный `unsupported`, чем фальшивый `done`.
- Лучше честный `ambiguous`, чем галочка без проверки.

## 7.3. Наблюдаемость

- У каждого шага должен быть trace/log.
- У ошибок должна быть причина.
- У таймаутов должен быть контекст.

## 7.4. Безопасность

- Не выполнять shell как замену GUI-слою по умолчанию.
- Не размывать границы поручения пользователя.
- Действия с высоким риском должны быть заметны и различимы.

---

## 8. Что взять из open source как ориентиры

## 8.1. Из `Peekaboo`
Взять:
- UX коротких GUI-команд;
- ритм `see → act → verify`;
- идею одного ядра с несколькими интерфейсами.

## 8.2. Из `uiautomation-mcp`
Взять:
- semantic-first подход;
- UIA-ориентированный способ работы с элементами.

## 8.3. Из `Windows-MCP`
Взять:
- practical tool coverage;
- snapshot / app / input идеи;
- понимание, как такой bridge используется в реальных agent workflows.

## 8.4. Из `Windows-MCP.Net`
Взять:
- структуру сервисов;
- разбиение на инструменты и домены;
- идеи screenshot/state/wait/UI tools.

---

## 9. Решения, которые уже можно считать принятыми

### Принято для V1
- Ядро на **C# / .NET 8+**.
- Локальный runtime на **Windows 11**.
- Базовый transport — **STDIO MCP**.
- **UIA-first**.
- **Window + desktop capture**.
- **Clipboard входит в MVP**.
- **Wait/verify входит в MVP**.
- Browser и desktop поддерживаются сразу, но через общий window-centric контур.

### Отложено на потом
- HTTP transport как основной.
- Богатый OCR layer.
- Полноценный browser DOM mode.
- Многомашинная/удалённая схема.
- Расширенный policy engine.

---

## 10. Итоговое определение V1

**Okno V1** — это локальный UIA-first Windows runtime на C#/.NET с MCP-интерфейсом, который даёт мне минимально достаточный и надёжный набор возможностей для наблюдения, выбора окна, семантических действий, coordinate fallback, ввода текста, clipboard-вставки, ожидания и проверки результата в браузерах и desktop-приложениях.

Если я с его помощью стабильно выполняю типовые рабочие сценарии пользователя и присылаю подтверждающие скрины состояния, значит V1 удался.
