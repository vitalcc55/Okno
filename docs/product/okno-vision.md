# Okno Vision

_План целевой архитектуры моего собственного Okno для Windows 11._

## Контекст

Этот план построен под мои реальные задачи на этом ПК:
- управление браузерами и обычными desktop-приложениями;
- работа по явному поручению пользователя;
- отправка скриншотов состояния;
- ввод и отправка текста в нужных формах;
- нажатие кнопок;
- переключение вкладок, окон и диалогов;
- доведение сценария до результата без лишних уточнений внутри уже поставленной задачи.

Цель не в “быстром кликере”, а в более правильной архитектуре уровня по духу близкой к `Peekaboo`, но под Windows 11 и под меня как агента.

---

## 1. North star: каким должен быть Okno

### 1.1. Не просто набор команд, а runtime для агента

Okno должен быть не “мешком разрозненных тулов”, а единым Windows runtime, который даёт мне:
- быстрый visual-action loop;
- устойчивое состояние окна/экрана;
- семантический доступ к UI, когда это возможно;
- fallback на raw input, когда семантики нет;
- явные шаги ожидания и проверки;
- стабильный API / MCP-контракт.

### 1.2. Что взять по духу от Peekaboo

Полезные ориентиры из идеи Peekaboo:
- screenshot-first мышление;
- очень короткий цикл `see → act → verify`;
- понятные команды верхнего уровня (`see`, `click`, `type`, `press`, `window`, `dialog`, `app`, `scroll`, `drag`);
- единое ядро, которое может работать и как CLI, и как MCP server;
- акцент на быстрое и предсказуемое агентное взаимодействие с GUI.

### 1.3. Что обязательно должно быть windows-native

- опора на `UI Automation` как основной semantic layer;
- поддержка `window capture` и `desktop capture`;
- работа с `foreground/focus`;
- поддержка `SendInput`-style ввода;
- понимание ограничений `UIPI`, elevated apps и пользовательской сессии.

---

## 2. Архитектурный принцип

```text
Я (агент, reasoning + vision + orchestration)
        ↓
Okno API / MCP facade
        ↓
Okno core runtime
        ├ state model
        ├ capture engine
        ├ semantic UI engine
        ├ input engine
        ├ wait/verify engine
        ├ safety/policy layer
        └ app/window/session manager
        ↓
Windows 11 apps (browser + desktop)
```

### 2.1. Моё место в системе

Я не должен напрямую заниматься Win32 деталями.
Я должен получать:
- хороший snapshot состояния;
- устойчивые action primitives;
- понятные сигналы успеха/ошибки.

### 2.2. Что должен делать runtime

Runtime должен брать на себя техническую грязь:
- DPI/scaling;
- focus issues;
- задержки UI;
- выбор режима захвата;
- нормализацию координат;
- попытки semantic action с fallback;
- безопасность и журналы.

---

## 3. Технологические строительные блоки

## 3.1. Semantic layer

### Базовый выбор: UI Automation

Это должен быть первый и основной слой.

Зачем:
- устойчивее координат;
- даёт структуру элементов;
- позволяет делать `invoke`, `set value`, `toggle`, `select`, `focus`, `scroll into view` и т.п.;
- даёт основу для более “умного” snapshot-а.

### Потенциальные open-source ориентиры

- `locomorange/uiautomation-mcp` — важный ориентир для semantic-first части;
- частично `Windows-MCP.Net` как reference по UI element tools.

### Вывод

Если semantic path существует, он должен быть предпочтительным всегда.

---

## 3.2. Capture layer

Okno должен уметь два режима:

### A. Window capture
Для точной работы с одним приложением.

Подходит для:
- форм;
- диалогов;
- рабочих приложений;
- браузера, если известна целевая вкладка/окно.

### B. Desktop capture
Для общего обзора и межоконных переходов.

Подходит для:
- понять, что сейчас вообще на экране;
- переключение окон;
- выбор нужного приложения;
- системные диалоги;
- multi-window workflow.

### Ориентиры

- `Windows.Graphics.Capture` — режим окна;
- `Desktop Duplication API` — desktop-level режим.

### Важный принцип

Capture должен быть быстрым и дешёвым, чтобы я мог мыслить короткими итерациями, а не редкими тяжёлыми шагами.

---

## 3.3. Input layer

Должны поддерживаться:
- move;
- click / double click / right click;
- drag;
- scroll;
- type;
- keypress;
- hotkey.

### Важные требования

- нормализация координат с учётом DPI;
- работа в активном окне;
- управление foreground/focus;
- awareness of elevation / UIPI limits;
- возможность безопасного отказа с понятным reason.

### Принцип использования

Input — это fallback и исполнительный слой, но не основной способ “понимать” UI.

---

## 3.4. Wait / Verify layer

Это один из самых важных слоёв.

### Почему он нужен

Без него bridge превращается в глупый автокликер.

### Он должен уметь

- ждать появления элемента;
- ждать исчезновения элемента;
- ждать смены текста / статуса;
- ждать смены изображения / layout;
- ждать готовности окна;
- ждать смены фокуса;
- ждать завершения перехода/операции.

### Принцип

Успех шага определяется не тем, что input был отправлен, а тем, что состояние UI изменилось ожидаемым образом.

---

## 3.5. Window / App manager

Нужны primitive-операции уровня:
- list windows;
- attach/switch/focus window;
- list apps;
- launch app;
- detect active app;
- resize/move window (минимально);
- enumerate dialogs.

### Почему это важно

Половина реальной работы — не “клик по кнопке”, а:
- найти нужное окно;
- вывести его вперёд;
- понять, не висит ли поверх диалог;
- переключить вкладку/приложение;
- вернуться назад.

---

## 3.6. Clipboard / text assist layer

Так как пользователь явно хочет, чтобы я:
- вставлял текст;
- отправлял его в формы и приложения;
- делал это удобно и надёжно,

мне нужен хороший слой для:
- read clipboard;
- set clipboard;
- safe paste;
- typed input vs paste fallback.

### Почему это важно

Во многих реальных рабочих сценариях `clipboard + paste` надёжнее и быстрее, чем посимвольный набор.

---

## 4. Целевой интерфейс инструментов

## 4.1. V1 core tools

Обязательное ядро:
- `windows.list_windows`
- `windows.attach_window`
- `windows.capture`
- `windows.uia_snapshot`
- `windows.uia_action`
- `windows.input`
- `windows.wait`
- `windows.clipboard`

## 4.2. V2 expanded tools

Следующий полезный набор:
- `windows.app`
- `windows.window`
- `windows.dialog`
- `windows.focus`
- `windows.region_capture`
- `windows.ocr`
- `windows.process` (минимально read-only + safe control)

## 4.3. V3 agent-grade tools

Более зрелый уровень:
- `windows.observe` — богатый snapshot состояния;
- `windows.act` — унифицированный action wrapper с semantic-first логикой;
- `windows.verify` — явная проверка инвариантов;
- `windows.workflow` / `windows.runplan` — исполнение короткого плана шагов с отчётностью;
- `windows.session` — управление прикреплением к текущему рабочему контексту.

---

## 5. План развития по версиям

## V1 — Solid core runtime

### Цель

Сделать устойчивое ядро, достаточное для реальных ручных поручений пользователя.

### Что обязательно должно быть

- один локальный Okno runtime под Windows 11;
- transport через MCP или эквивалентный локальный интерфейс;
- `capture` окна и desktop;
- `list/attach/focus window`;
- `uia_snapshot`;
- `uia_action` для базовых control patterns;
- `input` fallback;
- `wait`;
- `clipboard`;
- простые, понятные логи/трассировка.

### Основной приоритет качества

- надёжность;
- предсказуемость;
- хорошая обратная связь на ошибки;
- минимизация “магии”.

### Где reuse-ить

- смотреть на `uiautomation-mcp` для UIA-части;
- смотреть на `Windows-MCP` и `Windows-MCP.Net` для набора практичных инструментов;
- ориентироваться на Peekaboo по UX/ритму команд и итераций.

### Что V1 уже должен позволять мне делать

- открыть/найти нужное окно;
- сделать скрин и отправить состояние пользователю;
- вставить или набрать текст;
- нажать нужные кнопки;
- переключить окно/вкладку/диалог;
- выполнить типовой рабочий сценарий по явной инструкции пользователя.

---

## V2 — Unified browser + desktop agent runtime

### Цель

Сделать единый агентный runtime для двух классов UI:
- browser UI;
- desktop UI.

### Что добавляется

- более богатая модель окна/вкладок/view state;
- better dialog handling;
- region capture и OCR fallback;
- richer observe output;
- нормальный action result model (`done / failed / ambiguous / verify_needed`);
- better waiting on transitions and busy states;
- history of recent snapshots / actions.

### Ключевой architectural gain

Я начинаю работать не с набором отдельных tool calls, а с более цельным Windows interaction model.

### Что V2 должен уметь лучше V1

- легче переключаться между browser и desktop задачами;
- устойчивее проходить длинные сценарии;
- лучше справляться с плохим UIA через OCR и visual fallback;
- давать пользователю более понятные скрин-отчёты по ходу задачи.

---

## V3 — Agent-native Okno

### Цель

Сделать Okno не просто MCP-сервером, а полноценным агентным runtime уровня “Windows Peekaboo for me”.

### Что должно появиться

- единый high-level observe/act/verify contract;
- stateful sessions;
- policy-aware action gating;
- richer semantics for windows, dialogs, tabs, forms, focus chains;
- deterministic selectors where possible;
- smarter fallback ladders:
  - semantic action;
  - focused input;
  - region OCR;
  - coordinate fallback;
- action journaling и replay-friendly traces;
- better packaging and self-diagnostics.

### V3-ощущение

Это уже не “Windows automation via random MCP tools”, а мой собственный зрелый control runtime.

---

## 6. Safety / policy model

Пользователь хочет, чтобы я был автономен внутри поручения, но не принимал лишние самостоятельные решения.

### Значит policy должна быть такой

#### Автоматически допустимо внутри поручения

- навигация по окнам и вкладкам;
- ввод согласованного текста;
- нажатие кнопок;
- переключение диалогов;
- отправка скриншотов состояния;
- повторные шаги до достижения оговорённого результата.

#### Нужны дополнительные тормоза

- логины, если это выходит за рамки явно оговорённого сценария;
- необратимые удаления;
- системные настройки вне прямой задачи;
- шаги, которые изменяют цель или область поручения;
- любые ambiguous destructive actions.

### Технически policy-слой должен уметь

- маркировать действия по уровню риска;
- поддерживать явный `needs_confirmation`;
- журналировать, что было сделано;
- давать readable reason, почему действие заблокировано.

---

## 7. Что исследовать и использовать как ориентиры

## 7.1. Основные ориентиры

- `Peekaboo` — образец UX и agent-oriented design
- `CursorTouch/Windows-MCP` — широкий practical Windows MCP
- `locomorange/uiautomation-mcp` — semantic UIA-first ориентир
- `shuyu-labs/Windows-MCP.Net` — .NET-style desktop automation reference

## 7.2. Как на них смотреть

Не копировать один в один, а смотреть по ролям:

### От Peekaboo взять
- стиль команд;
- цикл see/act/verify;
- подход к runtime + server + CLI;
- скорость и агентный UX.

### От Windows-MCP взять
- ширину охвата задач;
- практичные десктопные тулзы;
- transport ideas;
- прикладной опыт desktop automation.

### От uiautomation-mcp взять
- семантический контроль через UIA;
- то, как строить устойчивый semantic layer.

### От Windows-MCP.Net взять
- общую структуру desktop runtime;
- набор инструментов состояния, OCR, screenshot, wait;
- идеи по декомпозиции на сервисы/инструменты.

---

## 8. Практические решения, которые я бы принял заранее

### 8.1. Не делать system-shell всемогущим центром управления

Okno не должен вырождаться в “давайте просто запускать PowerShell и pyautogui отовсюду”.

Shell может быть вспомогательным, но ядро должно быть GUI-aware.

### 8.2. Не строить всё вокруг OCR

OCR полезен как fallback, но не как главный semantic layer.

### 8.3. Не полагаться только на coordinate-based действия

Координаты нужны, но это самый хрупкий уровень.

### 8.4. Не путать browser automation и desktop automation, но и не разводить их в два несвязанных мира

Нужен единый orchestration слой и разные capability layers underneath.

---

## 9. Что должно стать первым практическим результатом

Первый реально ценный результат — не “идеальная система”, а такой Okno, чтобы я уже мог:

1. прикрепиться к нужному окну;
2. прислать пользователю хороший скрин состояния;
3. найти нужный контрол semantic-first способом;
4. при необходимости ввести/вставить текст;
5. нажать нужную кнопку;
6. дождаться результата;
7. продолжить сценарий до завершения;
8. дать понятный отчёт, где я нахожусь и что сделал.

Если это работает устойчиво — ядро правильное.

---

## 10. Итоговый вектор

### В одной фразе

**Мне нужен не просто Windows MCP-сервер, а собственный agent-grade Okno: быстрый, screenshot-first, UIA-first, verify-always runtime для реального управления этим Windows 11 ПК.**

### Короткая формула развития

- **V1:** надёжное ядро
- **V2:** единый browser+desktop runtime
- **V3:** зрелый agent-native Windows control layer

