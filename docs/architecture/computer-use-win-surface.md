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

## Что публикуется сейчас

Текущий shipped public subset для `computer-use-win`:

- `list_apps`
- `get_app_state`
- `click`

Это намеренно quiet operator loop:

```text
list_apps -> get_app_state -> click -> get_app_state
```

`type_text`, `press_key`, `scroll` и `drag` закреплены как следующий глобальный action wave, но пока не считаются shipped public implementation.

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
- approval/block policy и `list_apps` app grouping допускаются только при доказанной stable process identity; окна без канонического process identity не должны получать public appId на основе `hwnd-*` и fail-close-ятся до approval/observation path;
- risky action confirmation — отдельный product-facing шаг;
- risky action confirmation не должна зависеть только от английской UI: policy использует и multilingual label signals, и более стабильные `AutomationId`/process-family markers там, где они доступны;
- coordinate click считается low-confidence target path и требует explicit confirm, если target не доказан через semantic element из последнего `get_app_state`;
- `stateToken` несёт observation envelope, на котором был получен semantic state; downstream revalidation не должна тихо откатываться к более слабым defaults;
- `elementIndex` click не должен слепо доверять сохранённым bounds: перед dispatch runtime заново разрешает target через свежий UIA snapshot с тем же observation budget и fail-close-ит как `stale_state`, только если semantic match больше не доказуем;
- ordinary actions внутри already-approved app должны быть дешевле, чем low-level per-step friction;
- `get_app_state` разделяет critical observation и advisory enrichment: screenshot + accessibility tree определяют success/failure; expected advisory-unavailable path для playbook hints не имеет права downcast-ить успешный observation result, но unexpected provider/runtime bug всё ещё materialize-ится как truthful `observation_failed` с sanitized audit provenance;
- `get_app_state` публикует `stateToken` и commit-ит shared state только после полной успешной materialization public result; failed observation не должна оставлять ghost tokens или другие скрытые bounded-state commits;
- `get_app_state` не является observation-only read-only hint: approved/confirmed path может менять approval store, foreground state и attached/session state, поэтому public metadata не должна рекламировать его как pure read-only tool;
- malformed request shapes должны отсекаться на public boundary как `invalid_request`: explicit invalid `tool-surface-profile`, nested extra fields и schema-invalid `maxNodes` не должны уходить в widened surface или поздний `observation_failed`;
- public `click` contract должен совпадать в validator, `tools/list` schema и generated exports: допустимые `button`/`coordinateSpace`, обязательный `stateToken` и selector mode (`elementIndex` xor `point`) публикуются из того же owner-слоя, что и runtime enforcement;
- public `computer-use-win` action payload обязан emit-ить только product-owned `failureCode` и `reason`; low-level `windows.input` evidence остаётся допустимым в audit/evidence, но не протекает наружу как несанкционированный payload wording;
- public action result должен materialize-иться из явной lifecycle phase: `pre_dispatch_reject`, `pre_dispatch_after_activation`, `after_revalidation_before_dispatch`, `post_dispatch_factual_failure`, `success`; `refreshStateRecommended` выводится не из record-default, а из комбинации phase + public failure semantics: malformed pre-dispatch reject может оставаться `false`, а `state_required` / `stale_state` / `capture_reference_required` и любые structured outcomes после activation/revalidation обязаны честно требовать свежий `get_app_state`;
- click activation failure после уже принятого `stateToken` не должен materialize-иться как `blocked_target` и не должен выводить причину из `WasMinimized` / `IsForeground` без typed cause: policy block означает только intentional deny, а `ActivateWindowResult.failureKind` должен сохранять исходную причину (`missing_target`, `identity_changed`, `restore_failed_still_minimized`, `foreground_not_confirmed`), которая маппится в retriable activation/state failures (`missing_target`, `stale_state`, `target_minimized`, `target_not_foreground`) с phase `AfterActivationBeforeDispatch`;
- transient inability to prove stable process identity не должна materialize-иться как policy block: public `get_app_state` обязан различать `blocked_target` и retriable `identity_proof_unavailable`, чтобы approval/block semantics не подменяли technical proof failure;
- semantic click admissibility должна совпадать между public tree и runtime revalidation: runtime не должен dispatch-ить `elementIndex`, если fresh element больше не clickable или semantic fallback свёлся к слишком слабому proof;
- `stateToken` имеет bounded retention и short-lived stale-state discipline вместо неограниченного in-memory накопления;
- blocked targets должны отсеиваться на public surface до unsafe dispatch.
- plugin-local runtime install path должен оставаться integrity-safe: `runtime/win-x64` не используется как repair scratch space, publish/recovery materialize-ят bundle в side directories и handoff-ят canonical path только после completion proof по полному published runtime manifest; rollback source (`win-x64.backup-*`) не потребляется destructive move-ом до validated terminal state, а legacy/pre-manifest runtime без existing manifest fail-close-ится вместо генерации manifest из неподтверждённого partial state; second-order repair handoff failure не должен оставлять canonical path пустым, если last-known-good backup всё ещё может быть возвращён.

## Что не делать дальше

- не возвращать `computer-use-win` обратно в низкоуровневый `windows.*` narrative;
- не публиковать next-wave actions как implemented до реального runtime proof;
- не строить новый adapter-runtime поверх `Okno`, если тот же server/profile может дать нужный public surface;
- не разводить отдельный repo ради продукта, который должен оставаться частью этого репозитория.
- не возвращать public install path к repo-root hint, env fallback или `.tmp`-driven launch model.
