# Okno V1 Roadmap

_Пошаговый план реализации Okno V1: от первого рабочего ядра до готового V1._

## 0. Назначение roadmap

Этот документ отвечает не на вопрос **что** такое Okno V1, а на вопрос **в каком порядке его реально собирать**.

Цель roadmap:
- не расползтись в стороны;
- не начать со сложных nice-to-have вещей;
- как можно раньше получить рабочее ядро;
- постепенно наращивать возможности, не ломая основу.

Ключевой принцип:
**сначала надёжный минимальный контур, потом наращивание удобства и покрытия.**

---

## 1. Общая стратегия сборки

Okno V1 нужно строить не по слоям “сделать весь UIA, потом весь capture, потом весь input”, а по **вертикальным срезам**, где каждый этап даёт уже какую-то реальную полезность.

### Правильная стратегия

Каждый этап должен:
- добавлять одну законченную способность;
- быть проверяемым через реальный пользовательский сценарий;
- не требовать достроить ещё 10 подсистем, чтобы понять, работает ли ядро.

### Неправильная стратегия

- долго делать “идеальный framework” без первого рабочего цикла;
- откладывать capture или wait на потом;
- начинать с OCR, сети, remote-режимов, policy-движка и прочих расширений.

---

## 1.5. Текущий срез репозитория

По состоянию на `2026-03-19` репозиторий уже находится не в greenfield-точке, а в состоянии **bootstrap-платформы + реализованного observe/window контура с shipped `windows.uia_snapshot`**.

Что уже подтверждено текущим репо и локальным `verify`:

- `src/WinBridge.Server` поднимает локальный `STDIO` MCP host и экспортирует tool contract.
- `src/WinBridge.Runtime.Contracts` / `Session` / `Tooling` / `Diagnostics` уже составляют рабочий базовый runtime-контур.
- `src/WinBridge.Runtime.Windows.Display` и `src/WinBridge.Runtime.Windows.Shell` уже дают monitor/window inventory, attach/focus/activate semantics.
- `src/WinBridge.Runtime.Windows.Capture` уже реализует `windows.capture` с PNG artifact, metadata и audit evidence.
- `src/WinBridge.Runtime.Windows.UIA` больше не является только seam: `windows.uia_snapshot` уже реализован как shipped public slice с runtime/evidence/smoke/docs контуром.
- `Input` и `Clipboard` пока остаются подготовленными seams и deferred tools, а `windows.wait` уже доведён до shipped public slice.

Важно:

- этапы ниже сохраняются как **greenfield-описание** и объясняют, как собирать V1 с нуля;
- для **текущего репозитория** operational priority теперь задаётся таблицей ниже, а не старым порядком “clipboard -> input -> UIA”.

## 1.6. Операционный roadmap в терминах репозитория

Легенда статусов:

- `реализовано` — capability уже есть в runtime и подтверждается `build/test/smoke`;
- `частично` — capability реально работает, но покрывает только часть конечного среза;
- `только_seam` — в репо есть project/interface/deferred contract, но нет реализованного behavior;
- `запланировано` — отдельного runtime/tool slice пока нет.

| Приоритет | Repo / tool slice | Что сюда входит | Статус | Готовность | Волна |
| --- | --- | --- | --- | --- | --- |
| 01 | `src/WinBridge.Runtime.Contracts` | DTO, results, session/capture/window/display wire contracts | `частично` | `70%` | `База` |
| 02 | `src/WinBridge.Runtime.Tooling` | `ToolNames`, `ToolContractManifest`, lifecycle, safety class, contract export | `частично` | `60%` | `База` |
| 03 | `src/WinBridge.Server` | MCP host, `STDIO` transport, tool registration, contract export mode | `реализовано` | `95%` | `База` |
| 04 | `src/WinBridge.Runtime.Diagnostics` | audit JSONL, `summary.md`, trace/span ids, artifact layout | `частично` | `75%` | `База` |
| 05 | `src/WinBridge.Runtime.Session` + `okno.session_state` | attach context, session snapshot, session mutation | `реализовано` | `80%` | `База` |
| 06 | `src/WinBridge.Runtime.Windows.Display` + `windows.list_monitors` | monitor inventory, `monitorId`, display identity diagnostics | `реализовано` | `85%` | `База` |
| 07 | `src/WinBridge.Runtime.Windows.Shell` + `windows.list_windows` / `windows.attach_window` / `windows.activate_window` / `windows.focus_window` | window inventory, target resolution, attach/focus/activate loop | `частично` | `70%` | `База` |
| 08 | `src/WinBridge.Runtime.Windows.Capture` + `windows.capture` | desktop/window capture, PNG artifact, metadata, audit evidence | `реализовано` | `90%` | `База` |
| 09 | `src/WinBridge.Runtime.Windows.UIA` + `windows.uia_snapshot` | minimal semantic snapshot для explicit/attached/active window | `реализовано` | `80%` | `R1-следом` |
| 10 | `src/WinBridge.Runtime.Waiting` + `windows.wait` | публичный wait tool: window/focus/element/text/visual conditions | `реализовано` | `85%` | `R1-следом` |
| 11 | `okno.health` + runtime guard layer | reporting-first readiness baseline: desktop session, integrity/UIPI, capture/UIA/wait readiness и reusable shared `launch` allow/degraded/blocked model без hidden enforcement | `реализовано` | `85%` | `R1-следом` |
| 12 | `WinBridge.Runtime.Tooling` + `WinBridge.Server` + `WinBridge.Runtime.Diagnostics` | safety baseline: allow/deny flags, dry-run, false-success policy, redaction hooks и reusable shared launch-readiness policy | `реализовано` | `100%` | `R1-следом` |
| 13 | proposed `windows.launch_process` | явный запуск `.exe` / процесса с отдельной моделью ошибок | `запланировано` | `0%` | `R1-следом` |
| 14 | proposed `windows.open_target` | shell-open файла / URL / document target без смешения с process launch | `запланировано` | `0%` | `R1-следом` |
| 15 | `src/WinBridge.Runtime.Windows.Input` + `windows.input` (`click` first) | первый action slice: pre-focus, targeting, post-check contract | `только_seam` | `10%` | `R2-старт` |
| 16 | `src/WinBridge.Runtime.Windows.Clipboard` + `windows.clipboard_get` / `windows.clipboard_set` | clipboard read/write как отдельный slice | `только_seam` | `15%` | `R2-старт` |
| 17 | `src/WinBridge.Runtime.Windows.Input` + `windows.input` (`type`, `keypress`, `hotkey`, `paste`, `scroll`, `drag`) | расширение action coverage после `click` + `wait` + clipboard | `только_seam` | `10%` | `R2` |
| 18 | proposed `windows.dialog` | common dialogs: open/save/confirm, path input, accept/close flow | `запланировано` | `0%` | `R2` |
| 19 | proposed `windows.menu` / `windows.taskbar` / `windows.tray` | desktop surfaces beyond core window automation | `запланировано` | `0%` | `R2-R3` |
| 20 | `artifacts/diagnostics` + scripts + generated docs | retention/cleanup, scenario runner, help/resources, agent-facing docs pack | `частично` | `30%` | `R3` |
| 21 | proposed daemon / overlay / virtual desktop slices | background companion, visualizer, virtual desktop support | `запланировано` | `0%` | `R3-опция` |

Ключевые последствия для текущего execution order:

- не расширять `windows.input` вширь до реализованных `windows.uia_snapshot`, публичного `windows.wait` и базового слоя проверки среды / safety;
- держать `launch` разделённым на `windows.launch_process` и `windows.open_target`, а не смешивать их в один tool;
- сдвигать минимальный hardening влево: policy/deny/redaction/dry-run должны появляться до широкого destructive input.

---

## 2. Этапы реализации V1

## Этап 1. Skeleton runtime

### Цель

Поднять пустой, но правильный каркас Okno как локального .NET MCP runtime.

### Что делаем

- Создаём проект ядра на `C# / .NET 8+`.
- Определяем базовую структуру модулей:
  - session manager
  - window manager
  - capture service
  - UIA service
  - input service
  - wait/verify service
  - clipboard service
  - audit/log service
- Поднимаем базовый MCP/интерфейсный слой.
- Настраиваем единый формат ошибок и результатов.
- Настраиваем логирование.

### Что ещё НЕ делаем

- никакой серьёзной функциональности;
- никакого OCR;
- никакой сложной browser-specific логики;
- никакого remote transport.

### Артефакт этапа

Рабочий локальный runtime, который:
- стартует;
- принимает tool calls;
- логирует вызовы;
- возвращает stub/results в правильной форме.

### Критерий завершения

Есть запускаемый bridge с устойчивым контрактом и понятным внутренним каркасом.

---

## Этап 2. Window/session foundation

### Цель

Научить bridge понимать базовый оконный контекст Windows.

### Что делаем

Реализуем:
- `windows.list_windows`
- `windows.attach_window`
- `windows.focus_window`
- session state для attached target

### Что важно

- список окон должен быть пригоден для выбора человеком и агентом;
- attach должен быть устойчив к выбору по title / id / process;
- focus должен честно сообщать об успехе или проблеме.

### Почему это второй этап

Без устойчивого окна невозможно нормально строить ни capture, ни semantic actions, ни input.

### Артефакт этапа

Я уже могу:
- увидеть список окон;
- выбрать нужное;
- привязать текущую работу к нему;
- перевести его в foreground.

### Критерий завершения

Типовой сценарий “найди и сфокусируй нужное окно” выполняется стабильно.

---

## Этап 3. Первый полезный visual loop: capture

### Цель

Получить первый реально полезный цикл наблюдения: окно/экран → снимок → анализ.

### Что делаем

Реализуем:
- `windows.capture` в режиме `desktop`
- `windows.capture` в режиме `window`
- возврат image + metadata + local artifact path

### Что важно

- capture должен быть достаточно быстрым;
- metadata должна включать scope, bounds, title/hwnd (если применимо), timestamp;
- window capture должен работать в контексте attached window.
- для первой поставки `Windows.Graphics.Capture` идёт как основной backend;
- native fallback на screen copy допустим только для `desktop monitor` path, в том числе когда `Windows.Graphics.Capture` недоступен в текущей сессии, а не для `window` semantics;
- minimизированный `HWND` должен давать явный tool-level error, а не `done`.

### Почему это третий этап

Потому что без него я ещё слепой.
Как только появляется capture, у меня возникает первый настоящий visual-action фундамент.

### Артефакт этапа

Я уже могу:
- прикрепиться к окну;
- получить его скриншот;
- прислать пользователю состояние;
- использовать снимок как основу следующего шага.

### Критерий завершения

Сценарий “найти окно → сделать скрин окна → подтвердить пользователю состояние” работает стабильно.

---

## Этап 4. Clipboard и базовый text path

### Цель

Добавить первый практический способ вводить содержимое в рабочие приложения.

### Что делаем

Реализуем:
- `windows.clipboard_get`
- `windows.clipboard_set`
- `input.paste` или equivalent внутри `windows.input`
- базовый `type`

### Почему это нужно до полноценного input-пакета

Потому что в реальных рабочих сценариях пользователя текст — одна из главных задач.
Clipboard path часто даёт самую быструю практическую пользу.

### Артефакт этапа

Я уже могу:
- взять текст;
- положить его в clipboard;
- вставить в окно/форму;
- использовать type как запасной путь.

### Критерий завершения

В простом приложении или браузере можно вручную сфокусировать поле и успешно вставить/ввести нужный текст.

---

## Этап 5. Low-level input core

### Цель

Добавить базовые действия мышью и клавиатурой.

### Что делаем

Реализуем в `windows.input`:
- move
- click
- double_click
- right_click
- scroll
- keypress
- hotkey
- drag
- type
- paste

### Что особенно важно

- нормализация координат;
- единый sequence-based contract;
- понятные ошибки при проблемах фокуса/окна;
- отсутствие ложного ощущения “всё сработало”, если шаг не верифицирован.

### Почему сейчас

Потому что теперь у меня уже есть:
- окно;
- capture;
- clipboard/text path.

Это делает input не изолированным трюком, а частью работающего контура.

### Артефакт этапа

Я уже могу выполнять простые ручные сценарии полностью на input-уровне.

### Критерий завершения

Сценарий “сфокусировать окно → кликнуть в поле → вставить/набрать текст → нажать кнопку” выполняется без ручного вмешательства в середине.

---

## Этап 6. UIA snapshot

### Цель

Перейти от purely visual/coordinate работы к semantic-aware контуру.

### Что делаем

Реализуем:
- `windows.uia_snapshot`
- получение дерева/списка элементов для explicit/attached/active окна с precedence `explicit -> attached -> active`
- базовые поля элемента:
  - element_id
  - name
  - automation_id
  - control_type
  - bounding_rect
  - patterns

### Почему этот этап не раньше

Потому что к этому моменту уже есть работающее ядро и можно сравнивать semantic path с coordinate path на реальных задачах.

### Артефакт этапа

Я начинаю понимать не только “что видно на картинке”, но и “какие контролы реально есть в окне”.

### Критерий завершения

Для типового Windows-приложения snapshot даёт пригодную структуру, по которой можно найти хотя бы кнопки, поля, базовые элементы формы.

Этап закрыт через staged delivery: Package A зафиксировал target policy и typed groundwork, Package B добавил runtime/evidence слой, а Package C довёл public MCP handler, smoke и generated docs до shipped state.

---

## Этап 7. UIA actions

### Цель

Дать мне semantic-first путь действий.

### Что делаем

Реализуем `windows.uia_action` с поддержкой в V1:
- invoke
- focus
- set_value
- toggle
- select

### Что важно

- action должен возвращать не просто bool, а нормальный статус;
- `unsupported` должен быть честным исходом;
- semantic action не должен тайно падать в coordinate action без явного решения верхнего слоя.

### Почему именно теперь

Потому что snapshot без действия ещё не даёт semantic loop.
После этого этапа появляется ключевой принцип V1: **UIA-first**.

### Артефакт этапа

Я уже могу сначала пробовать семантическое действие, и только потом идти в fallback input.

### Критерий завершения

В типовом приложении хотя бы часть действий по формам и кнопкам выполняется через UIA без координатного хака.

---

## Этап 8. Wait / verify core

### Цель

Сделать систему по-настоящему агентной, а не просто исполнительной.

### Что делаем

Этап закрыт через staged delivery: Package A зафиксировал contract/target policy, Package B и C довели runtime/evidence policy, а Package D добавил public MCP handler, shipped lifecycle, L2 integration tests, L3 smoke и docs sync для `windows.wait`.

Итоговый shipped `windows.wait` поддерживает условия:
- active window matches
- element exists
- element gone
- text appears
- visual changed
- focus is

### Дополнительно

- сравнение current vs previous capture;
- простая модель timeout/ambiguous/failed;
- target policy `explicit -> attached -> active`;
- без hidden activation fallback и без auto-attach;
- один публичный tool `windows.wait`, а не zoo из `wait_for_*`.

### Почему этот этап критичен

До него bridge умеет действовать.
После него bridge начинает уметь **проверять, что действие реально сработало**.

### Артефакт этапа

Появляется нормальный `act → wait → verify` цикл.

### Критерий завершения

Сценарии больше не опираются на голые sleep там, где можно проверить реальное изменение UI.

---

## Этап 9. First complete workflow slice

### Цель

Собрать первый полный вертикальный сценарий от начала до конца.

### Что делаем

Проверяем целиком несколько сценариев:

#### Сценарий A — desktop form
- list windows
- attach window
- capture
- uia snapshot
- focus/set_value/click
- wait
- capture after

#### Сценарий B — browser task (window-centric)
- attach browser window
- capture
- input / paste
- tab switch or click
- wait
- capture after

#### Сценарий C — modal dialog
- detect current dialog/window
- focus
- click/invoke button
- wait for dialog gone

### Почему это отдельный этап

Потому что только на этом этапе видно, действительно ли все предыдущие куски складываются в рабочую систему.

### Артефакт этапа

Первый реально usable Okno V1 alpha.

### Критерий завершения

Минимум 3 типовых сценария пользователя проходят от начала до конца на одном и том же runtime без ручной подмены инструментов в середине.

---

## Этап 10. Stabilization pass

### Цель

Перед завершением V1 не добавлять новые большие возможности, а стабилизировать уже имеющееся.

### Что делаем

- чистим контракт инструментов;
- убираем лишние параметры;
- делаем сообщения об ошибках понятными;
- улучшаем logging/traces;
- чиним DPI/focus edge cases;
- чиним attach/capture/wait regressions;
- проверяем несколько приложений из двух классов:
  - browser;
  - desktop.

### Что НЕ делаем

- не начинаем новый большой OCR-слой;
- не добавляем remote HTTP и облачные режимы;
- не превращаем этап стабилизации в бесконечную стройку V2.

### Артефакт этапа

V1 release candidate.

### Критерий завершения

Система предсказуемо ведёт себя в тех сценариях, ради которых она и делалась.

---

## 3.5. Финальный этап V1 release

### Цель

Считать V1 завершённым не по “ощущению”, а по конкретным условиям.

### V1 считается готовым, если одновременно верно следующее

1. Runtime стабильно стартует локально.
2. Я могу выбрать и сфокусировать окно.
3. Я могу получить desktop/window capture.
4. Я могу получить UIA snapshot в типовом приложении.
5. Я могу выполнить semantic action там, где UIA доступен.
6. Я могу уйти в fallback input там, где UIA не помогает.
7. Я могу вставлять/вводить текст.
8. Я могу ждать и проверять результат, а не только кликать вслепую.
9. Я могу присылать пользователю подтверждающий screenshot после шага.
10. Есть хотя бы несколько стабильных end-to-end сценариев.

---

## 4. Порядок приоритета при нехватке времени

Если смотреть **от текущего состояния репозитория**, а не от пустого greenfield, приоритет должен быть таким:

1. `windows.uia_snapshot`
2. `windows.wait`
3. базовая проверка среды + safety baseline
4. `windows.launch_process`
5. `windows.open_target`
6. `windows.input` (`click`)
7. `windows.clipboard_get` / `windows.clipboard_set`
8. `windows.input` (`type`, `keypress`, `hotkey`, `paste`)
9. `windows.dialog`
10. `windows.menu` / `windows.taskbar` / `windows.tray`

### Почему именно так

Потому что базовый `window/display/capture` контур уже существует, а следующий риск лежит не в отсутствии ещё одного tool, а в отсутствии детерминированной цепочки `observe -> resolve -> wait -> act` без ложных success-path.

---

## 5. Что нельзя делать раньше времени

### Не лезть раньше времени в:
- глубокий OCR layer;
- remote/cloud режим;
- сложную политику доступа;
- гигантский zoo из 30–50 tools;
- специальную browser DOM интеграцию как обязательную часть V1;
- макрорекордер;
- системный shell как “универсальное решение всего”.

### Почему

Это всё может быть полезно позже, но на V1 почти гарантированно размоет фокус и замедлит появление первого реально рабочего ядра.

---

## 6. Практический принцип тестирования каждого этапа

Для каждого этапа должно быть три вопроса:

1. **Что нового я теперь реально могу сделать как агент?**
2. **Какой один живой сценарий это доказывает?**
3. **Какая следующая зависимость действительно нужна, чтобы двигаться дальше?**

Если на этап нельзя ответить через живой сценарий, значит он слишком абстрактный.

---

## 7. Вектор после V1

После завершения V1 логичное расширение — это V2:
- richer browser+desktop unification;
- OCR fallback;
- richer observe model;
- better dialog/tab/window abstractions;
- transport expansion.

Но только после того, как V1 уже стал реально usable.

---

## Итог в одной фразе

**Для текущего репозитория Okno V1 надо доводить не через расползание в ширину, а через детерминированную цепочку `observe -> resolve -> wait -> act`: на уже реализованном `window/display/capture` baseline сначала закрыть `windows.uia_snapshot`, затем `windows.wait` и environment/safety, потом `launch`, и только после этого расширять `windows.input`, clipboard и dialog-срезы.**
