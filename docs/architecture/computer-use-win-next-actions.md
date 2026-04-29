# Computer Use for Windows Next Actions

## Назначение

Этот документ фиксирует следующий целевой облик публичного `computer-use-win`
после закрытия базового цикла `list_apps -> get_app_state -> click`.

Цель не в том, чтобы переписать `Okno` под отдельный OpenAI runtime. Цель -
расширить уже существующий MCP-профиль так, чтобы он оставался похожим на
нативный visual computer-use loop Codex: сначала получить состояние окна, затем
выполнить ограниченное действие, затем снова получить состояние.

Official OpenAI docs и их sample repos подтверждают, что это правильная
граница для проекта:

- mature local harness не нужно перестраивать вокруг built-in `computer use`,
  если уже есть свой structured MCP/plugin path;
- structured integration предпочтительнее raw visual loop там, где она уже
  существует;
- action loop должен иметь один явный owner, а не быть размазан по множеству
  несвязанных helper paths;
- scenario proof и factual post-action verification важнее, чем один optimistic
  transcript действий;
- workflow control вроде `mark_done` или другого orchestration-only сигнала не
  должен смешиваться с public action tools.
- client-side MCP allow/deny lists (`allowed_tools`, `enabled_tools`,
  `disabled_tools`) полезны как operational narrowing layer, но не заменяют
  сам product boundary `computer-use-win`.

## Текущая опора

Сейчас shipped public surface намеренно мал:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

Это и есть текущая action-ready база. Все shipped действия продолжают
строиться поверх того же состояния:

- `windowId` выбирает конкретное окно только через published discovery snapshot;
- `stateToken` является короткоживущим proof object для последнего успешного
  observation;
- action path не должен использовать старое состояние без live revalidation;
- низкоуровневые `windows.*` tools остаются внутренним движком, а не
  пользовательским surface.

## Целевой публичный набор

Целевой набор `computer-use-win` стоит расширить до:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

`set_value` и `perform_secondary_action` нужно считать полноценными
Windows-native semantic additions, а не случайными расширениями. Они закрывают
важные сценарии, где грубый ввод мышью или клавиатурой хуже семантического
действия.

Это осознанное отличие от generic visual computer-use vocabulary. Official
OpenAI action families естественно включают `click`, `type`, `scroll`, `drag`
и key-like actions, но для Windows-native surface у нас есть сильное основание
держать ещё и:

- `set_value` как semantic-first путь для settable/editable controls;
- `perform_secondary_action` как product-owned secondary intent, а не как
  простой алиас для right-click.

## Порядок реализации

Эта wave закрыта целиком: `press_key`, `set_value`, `type_text`, `scroll`,
`perform_secondary_action` и `drag` уже shipped в public subset. `drag`
закрывался последним через отдельный prerequisite plan, потому что требовал
factual low-level dispatch proof, отдельный source/destination contract,
deterministic helper story и install/publication proof поверх уже shipped
click/scroll/type paths.

Текущий ближайший follow-up после wave closure не в новой action vocabulary, а
в quality gap текущего surface:

- poor-UIA apps уже проходят screenshot-first navigation;
- но text entry без editable UIA proof всё ещё правильно fail-close-ится;
- поэтому следующий узкий workstream должен смотреть в сторону
  keyboard-focus fallback для `type_text`, а не в сторону broad new actions.

## Контракт действий

Все новые действия должны требовать `stateToken`. Ни одно действие не должно
начинаться с implicit foreground guessing.

Отдельное правило для всей волны: action semantics не должны размываться в
workflow control. Public surface этой волны описывает только наблюдение и
действия над UI. Completion/flow-control сигналы, которые нужны demo/server
orchestration, должны жить вне action tools.

### `press_key`

Назначение: отправить клавишу или короткое повторение в уже подготовленное
окно.

Инварианты:

- принимает только state-backed target;
- перед dispatch заново проверяет, что окно всё ещё action-ready;
- опасные клавиши и сочетания проходят через confirmation policy;
- не выдаёт success, если низкоуровневый input runtime вернул factual failure
  или `verify_needed`.

### `set_value`

Назначение: установить значение у конкретного редактируемого элемента.

Инварианты:

- primary path - semantic element action через свежий UIA proof;
- этот action должен оставаться distinct from `type_text`, а не быть его
  красивым названием;
- target задаётся через `elementIndex`;
- если элемент больше не является settable, результат должен быть
  `stale_state` или `unsupported_action`, а не blind typing fallback;
- fallback через focus + typing допустим только как явно описанный low-trust
  branch и не должен тихо перетирать clipboard.

### `type_text`

Назначение: ввести текст в уже доказанную активную область.

Инварианты:

- если передан `elementIndex`, runtime заново доказывает тот же focused
  writable `edit` target, а не пытается скрыто перевести focus сам;
- если `elementIndex` не передан, typing допустим только когда stored state
  содержит ровно один доказанный focused writable `edit` element;
- `type_text` не должен молча поглощать use cases для `set_value`; если control
  является явно settable через semantic path, предпочтителен именно
  `set_value`;
- clipboard/paste не используется как default shortcut;
- whitespace-only text является валидным payload, если contract явно разрешает
  text insertion;
- writable proof должен опираться на UIA pattern/read-only semantics, а не на
  один только `ControlType=edit`;
- public `type_text` v1 по умолчанию завершает action как `verify_needed`, а
  не как optimistic `done`.

### `scroll`

Назначение: прокрутить область или окно.

Инварианты:

- semantic path через UIA scroll pattern предпочтительнее wheel input;
- coordinate fallback требует fresh capture geometry proof;
- result не должен быть `ok`, если runtime не смог доказать dispatch или
  factual outcome хотя бы как `verify_needed`;
- scroll не должен менять selector/session ownership.

Текущее shipped `scroll` v1 уже следует этим инвариантам: `elementIndex`
использует fresh-revalidated `ScrollPattern`, а `point` path остаётся явным
wheel fallback с `confirm` gate и default `verify_needed`.

### `perform_secondary_action`

Назначение: выполнить вторичное действие над элементом. Это не должно быть
просто переименованным right-click.

Инварианты:

- primary path - semantic action из свежего UIA tree;
- right-click/context-menu path допустим только как fallback с явным
  reobserve-after-action requirement;
- action должен возвращать `unsupported_action`, если у элемента нет
  достаточно сильного semantic proof.

Текущее shipped `perform_secondary_action` v1 сохраняет только semantic часть
этих инвариантов: tool публикуется для strong UIA secondary affordance
`toggle`, требует fresh `elementIndex` proof и намеренно не принимает
context-menu/right-click fallback, пока нет отдельного evidence pack для
такого path.

### `drag`

Назначение: выполнить drag между двумя доказанными точками или элементами.

Инварианты:

- source и target revalidate-ятся отдельно;
- coordinate drag всегда high-risk и должен требовать explicit confirmation;
- drag не должен появляться раньше, чем click/scroll/type paths уже имеют
  общий lifecycle owner;
- после dispatch всегда рекомендуется fresh `get_app_state`.

Текущий `drag` v1 уже shipped: runtime отдельно пере-подтверждает source и
destination endpoints, element endpoints проходят fresh UIA revalidation,
coordinate endpoints требуют explicit confirmation и geometry proof, а factual
Win32 dispatch идёт через `move -> button_down -> move/path -> button_up`.
Generic drag path не претендует на semantic postcondition proof и поэтому по
умолчанию возвращает `verify_needed` с рекомендацией fresh `get_app_state`.

## Отдельный курсор агента

На Windows не стоит пытаться делать второй настоящий системный курсор основой
архитектуры. Системная мышь является общей для пользователя и процессов, а
`SendInput` работает через общий поток ввода. Это значит, что постоянное
движение настоящей мыши агентом будет конфликтовать с пользователем.

Правильная модель:

- настоящий курсор пользователя остаётся пользователю;
- `computer-use-win` продолжает работать через snapshot + action loop;
- локальный "курсор агента" рисуется отдельным overlay/sidecar UI;
- overlay показывает intent и progress, но не является source of truth для
  dispatch;
- если действие всё же идёт через глобальный input, это остаётся low-tier
  fallback, а не основа.

Такой cursor layer стоит проектировать отдельным ExecPlan после первых новых
action tools. Он не должен менять MCP payload shape.

## Windows-реальность

UIA у внешних приложений часто бедный. Поэтому future design не должен
полагаться только на semantic tree.

Нужна смешанная лестница:

1. semantic UIA action, если есть сильный proof;
2. fresh capture geometry + coordinate action;
3. app-specific adapters там, где они появятся;
4. глобальный input только как последний уровень.

Важнее всего сохранить честность результата. Если runtime не может доказать
успех, он должен возвращать `verify_needed` или structured failure, а не
оптимистичный `ok`.

Отсюда следует ещё одно практическое правило для реализации: каждый новый
public action должен иметь не только unit/integration tests, но и маленький
канонический scenario proof в живом runtime. Для этой волны недостаточно
доказать только dispatch path; нужен короткий e2e loop с factual post-state.

Отдельное правило для screenshot-first paths: если внешний client/adaptor
когда-нибудь downscale-ит screenshot перед model turn, координаты должны
явно remap-иться обратно в original geometry basis. Reduced screenshot space не
может считаться destructive-dispatch truth model сам по себе.

## Что не делать

- не публиковать новые tools как implemented до runtime proof и smoke coverage;
- не возвращать public surface к низкоуровневым `windows.*`;
- не делать clipboard/paste скрытым default для text paths;
- не ослаблять `windowId` / `stateToken` semantics ради удобства;
- не смешивать overlay cursor с протоколом действий;
- не превращать public action tools в workflow-control layer;
- не копировать browser-centric assumptions из sample apps буквально в
  Windows desktop runtime;
- не начинать с `drag` как с первой новой action wave.

## Источники

- `docs/architecture/computer-use-win-surface.md`
- `docs/architecture/openai-computer-use-interop.md`
- official OpenAI docs:
  - `https://developers.openai.com/api/docs/guides/images-vision`
  - `https://developers.openai.com/api/docs/guides/tools-computer-use`
  - `https://developers.openai.com/cookbook/examples/mcp/mcp_tool_guide`
  - `https://developers.openai.com/api/docs/guides/tools-connectors-mcp`
  - `https://developers.openai.com/learn/docs-mcp`
  - `https://developers.openai.com/codex/mcp`
  - `https://developers.openai.com/codex/app/windows`
- official OpenAI sample repos:
  - `https://github.com/openai/openai-cua-sample-app`
  - `https://github.com/openai/openai-testing-agent-demo`
- Microsoft Win32 input/cursor documentation: `SetCursorPos`, `SendInput`,
  extended window styles.
- OpenAI Codex Computer Use documentation: visual computer use is appropriate
  when Codex needs to inspect or operate an app visually; structured plugin/MCP
  integrations remain preferred when available.
