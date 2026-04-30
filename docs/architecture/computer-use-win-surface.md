# Computer Use for Windows Surface

## Зачем нужен этот документ

Этот документ фиксирует product boundary для `computer-use-win` внутри текущего репозитория:

- что является публичным Codex plugin surface;
- что остаётся внутренним `Okno` / `WinBridge` engine;
- как эти слои должны развиваться без второго runtime и без отдельного репозитория.

## Ключевая граница

`computer-use-win` — это публичный Codex plugin и operator-facing MCP surface.  
`Okno` — это внутренний Windows-native engine и execution substrate.

Правильная модель:

```text
Codex
  -> plugin `computer-use-win`
    -> MCP profile `computer-use-win`
      -> internal Okno engine services
        -> capture / UIA / input / wait / launch / open
```

Неправильная модель:

- второй отдельный runtime ради public plugin;
- отдельный репозиторий для `computer-use-win`;
- смешение product-facing operator tools и low-level `windows.*` как одного шумного public surface.

Official OpenAI docs и sample repos поддерживают именно эту границу: когда у
продукта уже есть зрелый structured harness, его не нужно ломать ради
built-in `computer use`. Правильнее сохранить quiet plugin/MCP surface и
расширять его action vocabulary поверх внутреннего engine.

## Что публикуется сейчас

Текущий shipped public subset для `computer-use-win`:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

Это намеренно quiet operator loop:

```text
list_apps -> get_app_state -> (click | press_key) -> get_app_state
list_apps -> get_app_state -> (click | press_key | set_value) -> get_app_state
list_apps -> get_app_state -> click -> get_app_state -> type_text -> get_app_state
list_apps -> get_app_state -> scroll -> get_app_state
list_apps -> get_app_state -> perform_secondary_action -> get_app_state
list_apps -> get_app_state -> drag -> get_app_state
list_apps -> get_app_state -> action(observeAfter=true) -> successorState
```

Именно этот loop является текущим product UX. Workflow control, scenario
completion и demo-orchestration сигналы не должны смешиваться с public
operator tools.

С точки зрения official OpenAI screenshot-first guidance важно ещё одно:

- первый turn может запросить screenshot до первого action batch;
- после action batch harness должен вернуть updated screenshot как first-class
  image input;
- поэтому `get_app_state` и action results с successful `observeAfter=true` в
  `computer-use-win` должны оставаться image-bearing observe steps, а не
  деградировать в path-only metadata surface.

Текущий repo уже следует этой линии частично: `get_app_state` публикует
structured metadata и screenshot bytes в MCP result, а `artifactPath`
остаётся operator/debug trail. Если конкретный Codex/client UI не рендерит
этот image block inline и оператору всё ещё нужен отдельный `view_image`, это
следует считать operator UX gap, а не доказательством того, что screenshot
loop в runtime не работает.

Текущая discovery model после `Stage 4`:

- `list_apps` сохраняет top-level `apps[]` как app-level approval/policy groups;
- каждый app entry публикует `windows[]` со всеми selectable visible window instances;
- primary public selector для instance targeting — runtime-owned opaque `windowId`, а не детерминированный хэш live HWND signals;
- `hwnd` остаётся explicit low-level/debug selector и не является единственным публичным semantic selector;
- `appId` остаётся approval/session identity и не используется как ambiguous execution selector для `get_app_state`.
- `windowId` намеренно discovery-scoped: если runtime не может доказать continuity с исходным discovery snapshot, `get_app_state(windowId)` fail-close-ится и требует свежий `list_apps`.
- attached refresh path намеренно слабее `windowId`: `get_app_state` без explicit selector должен выдерживать обычный post-action title/layout drift того же окна, пока instance continuity всё ещё доказуема.
- `list_apps` больше не является pure read-only observation hint: вызов выдаёт новые runtime-owned selectors, заменяет latest published selector snapshot и обновляет bounded server-side catalog, от которого зависит следующий `get_app_state(windowId)`.
- replacement snapshot invalidates previous discovery selectors immediately: старые `windowId` больше не являются product-valid selectors после нового `list_apps`, включая empty publication, даже если storage entry ещё живёт до TTL/overflow cleanup.
- explicit `hwnd` и attached fallback не минтят новый reusable `windowId`: они переиспользуют selector из current published snapshot при strict match, иначе public `session.windowId` отсутствует/null и следующий refresh должен идти через `hwnd`, attached session или свежий `list_apps`.
- `get_app_state` не публикует pre-activation `windowId` после side-effecting activation без повторного strict proof. Если activation изменила `WindowState`, bounds, monitor metadata или другие discovery-snapshot поля, success payload и stored state оставляют `session.windowId` absent/null вместо возврата selector, который следующий `get_app_state(windowId)` сразу reject-нет.

Текущая action wave полностью поднята в shipped public subset: `press_key`,
`set_value`, `type_text`, `scroll`, `perform_secondary_action` и `drag` уже
перешли в implemented public profile после полного contract/runtime/test/docs
proof.
Текущий `press_key` v1 намеренно узкий: только named keys и modifier combos,
`repeat` ограничен диапазоном `1..10`, а shortcut-базы `A-Z` / `0-9`
диспетчатся как invariant virtual keys, а не как layout-sensitive text input.
Текущий `set_value` v1 использует semantic set path через `ValuePattern` /
`RangeValuePattern` и не деградирует в blind typing fallback. Текущий
`type_text` v1 остаётся lower-confidence input path: он печатает только в
focused writable `edit` target, который заново подтверждён через fresh UIA
snapshot и UIA read-only semantics; для poor-UIA targets есть explicit
`allowFocusedFallback=true` branch, который требует `confirm=true`, fresh
target-local focus proof и всё равно остаётся dispatch-only `verify_needed`.
Этот path не использует clipboard/paste как default shortcut и не возвращает
optimistic `done`. Текущий `scroll` v1 предпочитает semantic `ScrollPattern`
для `elementIndex` target, не меняет selector/session ownership и допускает
coordinate wheel fallback только через explicit `point` + `confirm` path с
fresh geometry proof; semantic success возвращает `done`, а wheel fallback
остается `verify_needed`. Текущий `perform_secondary_action` v1 тоже остаётся
semantic-only: он публикуется только для strong UIA secondary affordance
`toggle`, требует fresh `elementIndex` proof и не деградирует в
context-menu/right-click fallback. Текущий `drag` v1 требует separate source и
destination proof, принимает `fromElementIndex|fromPoint` и
`toElementIndex|toPoint`, использует один Windows-native input runtime вместо
второго dispatch layer, требует explicit confirmation для coordinate endpoints
и по умолчанию завершает generic path как `verify_needed`, а не optimistic
`done`.
`click`, `press_key`, `type_text`, `scroll` и `drag` дополнительно принимают
explicit `observeAfter=true`: после committed `done`/`verify_needed` action
runtime best-effort возвращает nested `successorState` того же product payload
family, новый short-lived `stateToken` и updated screenshot image block.
Успешный successor state делает `refreshStateRecommended=false`, потому что
fresh state уже embedded в action result. Top-level action status при этом
остаётся factual: `verify_needed` не становится optimistic `done`, а failed
successor observe materialize-ится как advisory `successorStateFailure` без
переписывания action outcome.

## Ближайший product gap

Первые два post-wave gap уже закрыты как bounded Stage 1/Stage 2 slices:

- screenshot-first navigation в poor-UIA apps уже работает;
- semantic и coordinate actions поверх такого navigation path уже работают;
- text entry без доказанного editable UIA proof теперь доступен только через
  explicit `allowFocusedFallback=true` + `confirm=true`, fresh focus proof,
  без clipboard default и с честным `verify_needed`, а не fake semantic
  success;
- low-confidence post-action loop теперь может быть дешевле через
  `observeAfter=true` на `click`, `press_key`, `type_text`, `scroll` и `drag`.

Feedback после shipped wave всё ещё оставляет две соседние follow-up зоны:

- public instance continuity UX: safety model правильно держит `windowId`
  discovery-scoped и fail-closed, но следующий UX шаг должен уменьшить churn и
  лишние `list_apps` loops без ослабления continuity proof и без наивного
  перехода к `hwnd + processId` как public selector;
- screenshot preview UX: для screenshot-first debugging/operator review нужен
  более бесшовный путь к current screenshot, если client UI не показывает
  MCP image block автоматически; это можно решать через preview hint /
  renderable image field / explicit include-image mode, но не ценой отказа от
  first-class image content или от локального artifact trail.

## Что остаётся внутренним engine surface

Эти tools и services остаются внутренним execution substrate:

- `windows.capture`
- `windows.input`
- `windows.wait`
- `windows.uia_snapshot`
- `windows.launch_process`
- `windows.open_target`
- `okno.health`
- `okno.contract`

Они нужны для engine, diagnostics, verification и repo development, но не являются главным product UX `computer-use-win`.

## Publication profile

Тот же `Okno.Server.dll` должен уметь публиковать разные surfaces через explicit profile selection:

- default/internal profile: `windows-engine`
- public plugin profile: `computer-use-win`

`plugins/computer-use-win/run-computer-use-win-mcp.ps1` обязан явно стартовать server с:

```text
--tool-surface-profile computer-use-win
```

Для публичного install artifact это означает:

- launcher стартует plugin-local apphost `runtime/win-x64/Okno.Server.exe`;
- `computer-use-win` не ищет repo root и не trampoline-ится в checkout;
- `.tmp/.codex/artifacts/local` и staged bundle preparation остаются verification/dev control plane, но не product install path;
- install copy должна быть самодостаточным runtime payload для shipped subset.
- launcher и recovery completion proof должны опираться на полный runtime bundle manifest, а не на hand-written sentinel file list.

## Safety model

Для `computer-use-win` продуктовая safety model отличается от low-level `windows.input`:

- app approval — product-facing gate;
- canonical app/process identity нормализуется к одному bare process name без `.exe`, чтобы block policy, playbooks и appId не drift-или между собой;
- approval/block policy и `list_apps` app grouping допускаются только при доказанной stable process identity; окна без канонического process identity или без достаточной live instance identity не должны получать public app/window selectors и fail-close-ятся до approval/observation path;
- public discovery обязан различать app-level approval key и window-level execution target: `appId` группирует policy, а `windowId` выбирает конкретный visible instance без foreground guessing;
- `windowId` не должен silently retarget-ить replacement window: selector разрешается только через runtime-owned discovery catalog и strict discovery proof, а не через повторное вычисление из текущих live свойств окна;
- следующий UX layer не должен пытаться лечить selector churn наивным
  `hwnd + processId`: reused HWND и replacement window всё ещё реальны, поэтому
  удобство нужно наращивать через runtime-owned continuity/publication model, а
  не через ослабление identity proof;
- public metadata для `list_apps` должна отражать stateful selector issuance: `readOnlyHint=true`, `idempotentHint=true` и `destructiveHint=false` для этого tool больше недопустимы, пока selector issuance живёт внутри `list_apps`, потому что новый snapshot может инвалидировать старые selectors;
- continuity proof для attached refresh не совпадает с `windowId` proof: session refresh допускает обычный post-action drift (`Title`, `Bounds`, `WindowState`, monitor metadata), но не должен пропускать stable-identity replacement;
- risky action confirmation — отдельный product-facing шаг;
- risky action confirmation не должна зависеть только от английской UI: policy использует и multilingual label signals, и более стабильные `AutomationId`/process-family markers там, где они доступны;
- coordinate click считается low-confidence target path и требует explicit confirm, если target не доказан через semantic element из последнего `get_app_state`;
- `stateToken` несёт observation envelope, на котором был получен semantic state; downstream revalidation не должна тихо откатываться к более слабым defaults;
- explicit runtime state model для public loop остаётся компактным, но жёстким: `attached`, `approved`, `observed`, `stale`, `blocked`;
- approval не заменяет fresh observation: без нового live proof state не становится action-ready только потому, что app уже одобрена;
- stale/blocked path не может быть silently promoted в successful action-ready state без нового live proof;
- `stateToken` не должен quietly переживать replacement того же HWND: если continuity observed window больше не доказуема по observed-state proof, downstream action path materialize-ится как `stale_state`, а не retarget-ится на новый live window;
- observed-state proof намеренно отделён от strict discovery proof: ordinary title/layout drift того же окна не должен заранее убивать `stateToken`, потому что semantic target всё равно повторно валидируется через fresh UIA snapshot перед dispatch;
- observed-state proof для confirmed coordinate click строже attached refresh: path без fresh UIA semantic revalidation не должен становиться action-ready только по instance continuity, если live geometry/capture proof уже устарели;
- missing capture proof не является window-continuity failure: если live target всё ещё совпадает, `capture_pixels` click должен доходить до action contract и materialize-иться как `capture_reference_required`, а не как `stale_state`;
- `elementIndex` click не должен слепо доверять сохранённым bounds: перед dispatch runtime заново разрешает target через свежий UIA snapshot с тем же observation budget и fail-close-ит как `stale_state`, только если semantic match больше не доказуем;
- ordinary actions внутри already-approved app должны быть дешевле, чем low-level per-step friction;
- снижение per-step friction должно идти через better successor-state /
  action+observe shaping и более сильный visual follow-up loop, а не через
  optimistic `done` там, где semantic outcome не доказан;
- `get_app_state` разделяет critical observation и advisory enrichment: screenshot + accessibility tree определяют success/failure; expected advisory-unavailable path для playbook hints не имеет права downcast-ить успешный observation result, но unexpected provider/runtime bug всё ещё materialize-ится как truthful `observation_failed` с sanitized audit provenance;
- `get_app_state` публикует `stateToken` и commit-ит shared state только после полной успешной materialization public result; failed observation не должна оставлять ghost tokens или другие скрытые bounded-state commits;
- `get_app_state` не является observation-only read-only hint: approved/confirmed path может менять approval store, foreground state и attached/session state, поэтому public metadata не должна рекламировать его как pure read-only tool;
- malformed request shapes должны отсекаться на public boundary как `invalid_request`: explicit invalid `tool-surface-profile`, nested extra fields и schema-invalid `maxNodes` не должны уходить в widened surface или поздний `observation_failed`;
- public `click` contract должен совпадать в validator, `tools/list` schema и generated exports: допустимые `button`/`coordinateSpace`, обязательный `stateToken` и selector mode (`elementIndex` xor `point`) публикуются из того же owner-слоя, что и runtime enforcement;
- action layer должен оставаться маленьким и semantically clear: `set_value`
  и `perform_secondary_action` допустимы как Windows-native semantic additions,
  но broad tool zoo, orchestration-only tools и “do anything” surface сюда не
  входят;
- если внешний client/adaptor когда-нибудь начнёт downscale-ить screenshots,
  coordinate actions обязаны remap-иться обратно в original geometry basis;
  resized screenshot space само по себе не является source of truth для
  destructive dispatch;
- public `computer-use-win` action payload обязан emit-ить только product-owned `failureCode` и `reason`; low-level `windows.input` evidence остаётся допустимым в audit/evidence, но не протекает наружу как несанкционированный payload wording;
- `tool.invocation.started` и `tool.invocation.completed` для `computer-use-win` используют safe audit payload builders и redaction classes: trail может нести `runtime_state`, `app_id`, `window_id`, `state_token_present`, `public_reason` и artifact hints, но не raw `stateToken`, не raw key literal, не raw semantic value и не raw low-level `reason`;
- public action result должен materialize-иться из явной lifecycle phase: `pre_dispatch_reject`, `pre_dispatch_after_activation`, `after_revalidation_before_dispatch`, `post_dispatch_factual_failure`, `success`; `refreshStateRecommended` выводится не из record-default, а из комбинации phase + public failure semantics: malformed pre-dispatch reject может оставаться `false`, а `state_required` / `stale_state` / `capture_reference_required` и любые structured outcomes после activation/revalidation обязаны честно требовать свежий `get_app_state`;
- `observeAfter=true` не меняет runtime truth model action outcome: nested
  `successorState` является новым observation proof, а не доказательством
  semantic success; если action committed, но post-action observe failed,
  top-level action payload остаётся `done`/`verify_needed`/`failed` по
  factual dispatch result, а successor failure публикуется отдельно как
  advisory `successorStateFailure`;
- click activation failure после уже принятого `stateToken` не должен materialize-иться как `blocked_target` и не должен выводить причину из `Window == null`, `WasMinimized` / `IsForeground`, nullable resolver result или `reason`: policy block означает только intentional deny, а `ActivateWindowResult.failureKind` обязателен для всех `failed` / `ambiguous` activation payloads и должен сохранять source-owned cause (`missing_target`, `identity_changed`, `identity_proof_unavailable`, `restore_failed_still_minimized`, `foreground_not_confirmed`), которая маппится в retriable activation/state failures (`missing_target`, `stale_state`, `identity_proof_unavailable`, `target_minimized`, `target_not_foreground`) с phase `AfterActivationBeforeDispatch`;
- transient inability to prove stable process identity не должна materialize-иться как policy block: public `get_app_state` обязан различать `blocked_target` и retriable `identity_proof_unavailable`, чтобы approval/block semantics не подменяли technical proof failure;
- semantic click admissibility должна совпадать между public tree и runtime revalidation: runtime не должен dispatch-ить `elementIndex`, если fresh element больше не clickable или semantic fallback свёлся к слишком слабому proof;
- `stateToken` имеет bounded retention и short-lived stale-state discipline вместо неограниченного in-memory накопления;
- blocked targets должны отсеиваться на public surface до unsafe dispatch.
- plugin-local runtime install path должен оставаться integrity-safe: `runtime/win-x64` не используется как repair scratch space, publish/recovery materialize-ят bundle в side directories и handoff-ят canonical path только после completion proof по полному published runtime manifest; rollback source (`win-x64.backup-*`) не потребляется destructive move-ом до validated terminal state, а legacy/pre-manifest runtime без existing manifest fail-close-ится вместо генерации manifest из неподтверждённого partial state; second-order repair handoff failure не должен оставлять canonical path пустым, если last-known-good backup всё ещё может быть возвращён.
- runtime-owned selector catalog не должен публиковать `windowId`, которые уже неразрешимы на ожидаемый следующий шаг loop: весь `list_apps` batch обязан пережить overflow eviction как единый generation, даже если batch больше nominal `maxEntries`.
- latest published discovery batch должен переживать не только собственный `Materialize(...)`, но и следующий follow-up issuance (`TryIssue` из explicit `hwnd` / attached fallback), пока новый `list_apps` snapshot не заменил предыдущий published batch.
- selector product-validity определяется current published discovery snapshot, а не физическим наличием entry в bounded catalog storage; TTL/overflow остаются retention mechanics и не должны решать, можно ли использовать selector прошлого snapshot.
- public `session.windowId` не должен смешивать selector и ephemeral execution identity: если target был выбран через `hwnd` / attached fallback без current published selector, payload не публикует `windowId`, а observability может нести отдельный internal `execution_target_id`.
- любой public `windowId`, публикуемый после activation/focus side effect, должен быть заново revalidated против current published discovery snapshot; pre-side-effect selector нельзя переносить в success payload, stored state или audit, если post-activation live window уже не проходит тот же strict proof, которым `TryResolveWindowId` принимает selector.
- install-surface freshness gate должен учитывать не только project/src tree, но и repo-root build/analyzer config inputs вроде `.editorconfig`, `.globalconfig`, `*.globalconfig`, `Directory.Build.rsp`, `Directory.Build.props`, `Directory.Packages.props` и `global.json`.

## Что не делать дальше

- не возвращать `computer-use-win` обратно в низкоуровневый `windows.*` narrative;
- не публиковать next-wave actions как implemented до реального runtime proof;
- не строить новый adapter-runtime поверх `Okno`, если тот же server/profile может дать нужный public surface;
- не разводить отдельный repo ради продукта, который должен оставаться частью этого репозитория.
- не добавлять workflow-control или demo-only tools в public action surface;
- не возвращать public install path к repo-root hint, env fallback или `.tmp`-driven launch model.
