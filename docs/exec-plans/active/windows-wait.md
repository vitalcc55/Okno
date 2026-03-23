# ExecPlan: windows.wait

Статус: done
Создан: 2026-03-19
Обновлён: 2026-03-23

## Goal

Спроектировать и подготовить shipped public slice `windows.wait`, который замыкает цепочку `observe -> resolve -> wait -> act` без ложных success-path и остаётся честным по отношению к live Windows state, MCP contract и evidence trail.

Что именно должен закрыть итоговый shipped slice:

- public tool `windows.wait` в `WindowTools` с честным `CallToolResult`, `structuredContent` и `isError` semantics;
- единый polling-first runtime service, который верифицирует live condition вместо `sleep`;
- target precedence `explicit -> attached -> active` без silent fallback из stale target;
- condition coverage V1: `active_window_matches`, `element_exists`, `element_gone`, `text_appears`, `visual_changed`, `focus_is`;
- evidence contract, достаточный для расследования `timeout`, `ambiguous` и `failed`.

Что закрывает текущий exec-plan:

- фиксирует boundary и semantics V1;
- задаёт file-level integration map;
- раскладывает delivery на пакеты без расползания в соседние capability slices.

## Non-goals

В текущий V1 намеренно **не** входят:

- `windows.uia_action` и любой semantic action rollout;
- широкое расширение `windows.input` за пределы будущих точек интеграции;
- event-daemon, background subscriber и event-first runtime как основной execution model;
- OCR и text detection вне UIA providers;
- taskbar, tray, menu и другие desktop surfaces вне top-level window scope;
- hidden activation, implicit focus stealing и любые side effects ради того, чтобы `wait` “сам себе помог”.

Follow-up после V1, но не внутри него:

- оценка event-assisted path для отдельных condition после shipped polling-first baseline;
- расширение `visual_changed` на desktop-scoped scenarios, если window-scoped baseline окажется стабильным;
- интеграция с `windows.uia_action` и `windows.input` как post-wait action loop, а не как часть этого slice.

## Current repo state

- `windows.wait` уже реализован как public tool в `src/WinBridge.Server/Tools/WindowTools.cs`, опубликован в `tools/list` и переведён в `Implemented` + `SmokeRequired` в `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`.
- `src/WinBridge.Runtime.Waiting/IWaitService.cs` и `PollingWaitService` являются единственным shipped V1 execution path для `windows.wait`; public handler больше не использует deferred stub и не держит legacy naming.
- В `src/WinBridge.Runtime.Windows.Shell/IWindowTargetResolver.cs` и `WindowTargetResolver.cs` уже добавлен capability-specific `ResolveWaitTarget(...)` с precedence `explicit -> attached -> active` и запретом на silent fallback из stale explicit/attached target.
- `windows.uia_snapshot` уже shipped и задаёт precedent для public MCP shape, control-view semantics и target precedence `explicit -> attached -> active`.
- `src/WinBridge.Runtime.Windows.Shell/WindowActivationService.cs` уже содержит polling + final live verification precedent, который можно переиспользовать как execution pattern, но не как готовую wait semantics.
- `src/WinBridge.Server/Tools/WindowTools.cs`, `tests/WinBridge.Server.IntegrationTests/*` и `scripts/smoke.ps1` уже задают expected shape для public observe tools: handler, `structuredContent`, text block, artifact checks и smoke-required registration.
- Worktree на момент создания плана чистый; текущий цикл не требует отката или согласования чужих незавершённых правок.

## Official constraints

- MCP `CallToolResult` задаёт обязательный `content`, опциональный `structuredContent` и `isError`; tool-originated errors для `windows.wait` должны возвращаться внутри result c `isError = true`, а не маскироваться под protocol-level success path.
- `ElementFromHandle` остаётся canonical root acquisition path для window-scoped UIA checks; V1 не строится от desktop root и не делает global descendant traversal как primary path.
- UI Automation tree имеет `raw`, `control` и `content` view; shipped V1 для `windows.wait` работает в `control view`, как и `windows.uia_snapshot`.
- `GetFocusedElement` является authoritative API для current UI focus, но focused element может стать unavailable между poll ticks; это требует retry + revalidation policy, а не оптимистичного success.
- `FindFirst` / `FindAll` по `TreeScope_Descendants` на desktop root несут performance risk; поиск должен стартовать от уже разрешённого window root или от узко определённого subtree.
- Для text waits нельзя полагаться на один provider-specific signal: V1 должен канонизировать `Name`, `ValuePattern` и `TextPattern`, не делая ложных предположений о доступности каждого pattern у всех controls.
- UIA event subscription остаётся evaluation path, а не V1 default: event-driven вариант сложнее по threading, handler lifetime и cleanup semantics, чем polling-first shipped slice.
- Ограничения `SetForegroundWindow` остаются прямым запретом на hidden activation fallback; `windows.wait` не может скрыто активировать окно ради выполнения `focus_is` или `active_window_matches`.

## Design contract

### Intent

`windows.wait` — это public `wait/verify` tool, который даёт агенту детерминированный способ проверить, что наблюдаемое состояние действительно наступило, прежде чем переходить к следующему действию.

Минимально полезный outcome:

- `done`, если live condition достигнуто и подтверждено тем же authoritative source;
- честный non-success result, если target недоказуем, condition не наступило за timeout или runtime не может отличить valid match от ambiguous/stale path.

### Boundary

- Tool class: `wait/verify`.
- MCP annotations для shipped V1:
  - `ReadOnly = false`
  - `Destructive = false`
  - `Idempotent = false`
  - `OpenWorld = true`
  - `UseStructuredContent = true`
- Tool не меняет session state и не делает OS mutation ради успеха condition.
- Tool может писать diagnostics artifact и audit events; это допустимый observability side effect, но именно из-за гарантированной записи artifacts tool не должен публиковаться как `ReadOnly = true`.
- Primary runtime dependencies:
  - `Windows.Shell` для foreground/active window resolution;
  - `Windows.UIA` для focus/element/text probes;
  - `Windows.Capture` для `visual_changed`;
  - `Runtime.Diagnostics` для audit/evidence.

### Target model

Public target precedence V1 совпадает с precedent `windows.uia_snapshot`:

- explicit `hwnd`, если он передан;
- attached window из session, если explicit target отсутствует;
- active foreground top-level window, если explicit и attached target отсутствуют.

Инварианты target resolution:

- `explicitHwnd <= 0` считается invalid explicit target и даёт явный failure path;
- stale explicit target не fallback-ится в attached/active;
- stale attached target не fallback-ится в active;
- active path допускается только как live resolution из одного актуального window inventory;
- ambiguous active target должен завершаться `ambiguous`, а не эвристическим выбором одного окна;
- `windows.wait` не делает auto-attach и не мутирует session snapshot.

### Condition matrix

| Wait condition | Authoritative source | Reuse existing slice? | Что считается success | Главный риск |
| --- | --- | --- | --- | --- |
| `active_window_matches` | foreground/live window snapshot | `Windows.Shell` | foreground window revalidated и совпадает с requested target/predicate | stale или ambiguous `HWND` |
| `focus_is` | `GetFocusedElement` + live element revalidation | `Windows.UIA` + shell focus semantics | focused element совпадает с target predicate после recheck | focus churn, `UIA_E_ELEMENTNOTAVAILABLE`, transient focus |
| `element_exists` | UIA subtree search от resolved window root | `Windows.UIA` | найден хотя бы один стабильный live match | expensive traversal, ambiguous selector |
| `element_gone` | UIA subtree search от resolved window root | `Windows.UIA` | после recheck live match отсутствует | stale tree, false negative на transient provider error |
| `text_appears` | `Name` / `ValuePattern` / `TextPattern` | `Windows.UIA` | canonical text source совпадает с expected predicate | provider variability, delayed text propagation |
| `visual_changed` | capture-derived fingerprint для того же target | `Windows.Capture` | baseline и current fingerprint различаются выше agreed threshold и recheck подтверждает change | noisy pixels, animation churn, over-trigger |

### Status/error model

Public status model V1:

- `done`: condition достигнуто и подтверждено тем же authoritative source на финальном poll tick.
- `timeout`: resolution и probes работали корректно, но condition не наступило до дедлайна.
- `ambiguous`: runtime обнаружил более одного валидного live candidate или не может честно различить target/match.
- `failed`: invalid request, stale explicit/attached target, unsupported environment или runtime/probe failure.

Contract rules:

- `isError = false` только для `done`;
- `isError = true` для `timeout`, `ambiguous` и `failed`;
- `timeout` не используется для invalid request или backend/probe exception;
- `ambiguous` не используется как prettier `failed`: этот статус допустим только когда runtime видит конкурирующие live candidates;
- `failed` не используется для “condition просто не наступило”.

Минимальные поля `WaitResult`:

- `status`
- `condition`
- `targetSource`
- `targetFailureCode`
- `elapsedMs`
- `attemptCount`
- `timeoutMs`
- `reason`
- `window`
- `matchedElement`
- `lastObserved`
- `artifactPath`

### Evidence contract

- Каждый вызов `windows.wait` пишет один JSON diagnostics artifact в `artifacts/diagnostics/<run_id>/wait/`.
- Artifact должен содержать request, resolved target, poll settings, attempt summary, final status, last observed probe snapshot и ссылки на capability-specific artifacts, если они создавались.
- Для `visual_changed` допускаются дополнительные image artifacts, но только как referenced evidence; сам `windows.wait` не должен без необходимости дублировать full `windows.capture` payload на каждом poll tick.
- Audit event должен быть capability-specific и отражать:
  - `condition`
  - `status`
  - `target_source`
  - `target_failure_code`
  - `attempt_count`
  - `elapsed_ms`
  - `artifact_path`
- Evidence path обязателен и для `timeout`/`ambiguous`, иначе расследование ложных ожиданий невозможно.

## Integration points

### Question-driven map

| Вопрос | Точка интеграции | Файлы |
| --- | --- | --- |
| Где живёт public tool? | MCP host / tool boundary | `src/WinBridge.Server/Tools/WindowTools.cs` |
| Где объявляется contract? | source of truth | `src/WinBridge.Runtime.Tooling/ToolNames.cs`, `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`, `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs` |
| Где живёт runtime seam? | wait service boundary | `src/WinBridge.Runtime.Waiting/IWaitService.cs` |
| Откуда брать active/focus/window state? | shell / display / session | `src/WinBridge.Runtime.Windows.Shell/*`, `src/WinBridge.Runtime.Session/*` |
| Откуда брать element/text state? | UIA runtime | `src/WinBridge.Runtime.Windows.UIA/*` |
| Откуда брать visual change? | capture runtime | `src/WinBridge.Runtime.Windows.Capture/*` |
| Где evidence и audit? | diagnostics | `src/WinBridge.Runtime.Diagnostics/*` |
| Где wiring? | DI / runtime | `src/WinBridge.Runtime/ServiceCollectionExtensions.cs` |
| Где L1/L2/L3 tests? | runtime / integration / smoke | `tests/WinBridge.Runtime.Tests/*`, `tests/WinBridge.Server.IntegrationTests/*`, `scripts/smoke.ps1` |

### File-level integration map

- `src/WinBridge.Runtime.Contracts/`
  - новые request/result/status/condition literals и typed summaries для `windows.wait`.
- `src/WinBridge.Runtime.Waiting/`
  - `IWaitService`, concrete polling implementation, condition probes, evidence writer, result mapper.
- `src/WinBridge.Runtime.Windows.UIA/`
  - focused-element probe, element search probe, text probe и shared canonicalization helpers.
- `src/WinBridge.Runtime.Windows.Capture/`
  - visual fingerprint source, threshold policy и capture-backed comparison helper.
- `src/WinBridge.Runtime.Windows.Shell/`
  - active window resolution, stale identity revalidation и potential `ResolveWaitTarget(...)` precedent.
- `src/WinBridge.Runtime.Diagnostics/`
  - audit event constants, artifact naming, write path и summary payload.
- `src/WinBridge.Runtime/`
  - DI registration для concrete wait service и auxiliary probes.
- `src/WinBridge.Server/Tools/WindowTools.cs`
  - public handler `Wait(...)`, request validation, `CallToolResult`.
- `src/WinBridge.Runtime.Tooling/`
  - lifecycle switch `Deferred -> Implemented`, descriptions, annotations, exporter truth.
- `tests/WinBridge.Runtime.Tests/`
  - L1 contract/policy/probe tests.
- `tests/WinBridge.Server.IntegrationTests/`
  - handler behavior, MCP annotations, structured payload, `isError` semantics.
- `scripts/smoke.ps1`
  - L3 end-to-end scenario с helper window и evidence assertions.
- `docs/generated/*`, `docs/product/*`, `docs/architecture/*`, `docs/CHANGELOG.md`
  - source-of-truth и generated sync после public rollout.

## Delivery packages

### Package A — contract + target policy

Статус: `done`

В объёме пакета:

- ввести typed `WaitRequest`, `WaitResult`, status/condition literals и target failure codes;
- зафиксировать public request shape вокруг одного tool `windows.wait`, а не zoo из `wait_for_*`;
- расширить shell/session resolution до `ResolveWaitTarget(...)` с precedence `explicit -> attached -> active`;
- определить honest MCP annotations и `isError` mapping;
- сохранить `windows.wait` в честном `Deferred/unsupported` до завершения runtime и test ladder.

Фактически закрыто в репозитории:

- добавлены typed `Wait*` contracts и capability-specific target resolution DTO;
- `IWaitService` и shell seam теперь компилируются против typed wait boundary;
- deferred contract для `windows.wait` остаётся честным `unsupported`, а manifest safety class синхронизирован с artifact-writing side effect.

Критерий завершения:

- contract и target policy больше не живут в устных договорённостях;
- runtime boundary компилируется против typed DTO, но public rollout ещё не включён.

### Package B — polling engine + core probes

Статус: `done`

В объёме пакета:

- реализовать `PollingWaitService` как единственный V1 execution path;
- закрыть `active_window_matches`, `element_exists`, `element_gone`, `text_appears`;
- ввести bounded poll loop, retry policy и final same-source revalidation;
- добавить JSON evidence artifact и capability-specific audit event;
- по-прежнему не переключать lifecycle в `Implemented`.

Критерий завершения:

- core conditions проходят L1 и не имеют silent fallback path;
- `timeout`/`ambiguous`/`failed` различаются по честным причинам, а не по удобству реализации.

Фактически закрыто в репозитории:

- `PollingWaitService` добавлен как единственный V1 execution path для runtime-only `windows.wait`;
- закрыты `active_window_matches`, `element_exists`, `element_gone`, `text_appears` без public handler rollout;
- wait boundary теперь пишет JSON evidence artifact в `artifacts/diagnostics/<run_id>/wait/` и capability-specific runtime audit event `wait.runtime.completed`;
- UIA slice получил минимальный live wait probe seam за process-isolated worker boundary с явным per-probe timeout budget от `PollingWaitService`, `ElementFromHandle`, `ControlViewCondition`, selector search и canonical text fallback `ValuePattern -> TextPattern -> Name`, а late runtime failures больше не маскируются под `timeout`;
- condition-specific UIA semantics больше не смешивают raw selector cardinality между разными waits: `element_exists` подтверждается только при непустом identity-overlap между candidate и recheck, `element_gone` продолжает polling пока live matches остаются, `text_appears` считает только text-qualified candidates, а ambiguous UIA outcome больше не публикует произвольный `matchedElement`;
- lifecycle `windows.wait` по-прежнему остаётся `Deferred/unsupported`, а Package C/D не затронуты.

### Package C — focus + visual hardening

Статус: `done`

В объёме пакета:

- добавить `focus_is` поверх focused-element probe с retry на unavailable element и final revalidation;
- добавить `visual_changed` через capture-backed fingerprint policy без per-tick PNG bloat;
- зафиксировать threshold/noise policy и блокирующие критерии для public rollout;
- подтвердить, что ни одно из этих условий не требует hidden activation или implicit action.

Критерий завершения:

- high-variance conditions имеют воспроизводимую runtime policy, закрыты L1 тестами и не дают drift на затронутых shared capture/contract boundaries;
- при нестабильности lifecycle не переключается в `Implemented`.

Фактически закрыто в репозитории:

- `focus_is` добавлен в runtime wait path поверх authoritative focused-element probe с process-isolated worker boundary, exact selector match только по текущему focused element, revalidation к window root через `ElementFromHandle(hwnd)`, bounded retry policy на transient unavailable focus state и корректной control-view parent lineage metadata;
- `visual_changed` добавлен в runtime wait path через window-scoped capture-backed compare без per-tick PNG bloat: visual probe больше не кодирует PNG и не тащит raw frame в orchestration contract, baseline фиксируется как domain state, comparison data materialize-ится только внутри visual probe, а baseline/current PNG materialization вынесена в отдельный best-effort evidence stage после подтверждённого change;
- threshold/noise semantics зафиксированы в коде и тестах: grayscale grid `16x16`, per-cell luma delta `>= 12`, geometry-change shortcut, effective success threshold вычисляется от populated cells с базовым ratio `16/256`, `WaitOptions` валидирует строго положительный poll interval, а `done` подтверждается только после гарантированного положительного confirmation gap и второго подряд candidate against the same baseline;
- evidence contract расширен ровно до runtime-only visual fields в wait artifact (`visual_difference_ratio`, `visual_difference_threshold`, `visual_evidence_status`, `visual_baseline_artifact_path`, `visual_current_artifact_path`) без nested payload reshaping;
- visual evidence stage теперь живёт на отдельном short budget `min(timeoutMs, VisualEvidenceBudgetMs)` и больше не понижает уже подтверждённый `done` до `timeout` или `failed`; late UIA probe downgrade по-прежнему опирается на worker-side completion timestamp, wait-specific visual probe не наследует скрытый `3s` capture cap, а optional PNG paths репортятся только если best-effort materialization успел завершиться;
- lifecycle `windows.wait` по-прежнему остаётся `Deferred/unsupported`, Package D не затронут.

### Package D — server rollout + smoke + docs sync

Статус: `done`

В объёме пакета:

- добавить public handler в `WindowTools`;
- перевести `ToolContractManifest` в `Implemented` и `SmokeRequired`, только если все шесть V1 conditions подтверждены;
- добавить L2 integration tests и L3 smoke scenario;
- синхронизировать generated docs, architecture docs, product docs и `CHANGELOG`.

Критерий завершения:

- `windows.wait` доступен через `tools/list`, проходит integration/smoke и не расходится с generated/docs truth.

Фактически закрыто в репозитории:

- `WindowTools.Wait(...)` заменил legacy deferred stub и теперь публикует final public schema `condition + selector + expectedText + hwnd + timeoutMs` без compatibility shim вокруг `until`;
- public handler резолвит target только через existing `ResolveWaitTarget(...)`, а tool-boundary failures больше не обходят canonical wait evidence path: terminal `failed` outcome теперь всё равно пишет wait artifact и `wait.runtime.completed`, после чего публикуется как прямой `WaitResult` в `structuredContent` + один `TextContentBlock`;
- `status -> isError` выровнен на MCP boundary: только `done` идёт с `isError = false`, а `timeout`, `ambiguous` и `failed` возвращаются как tool-level errors внутри `CallToolResult`;
- `ToolContractManifest`, `okno.contract`, exporter и `tools/list` больше не расходятся: `windows.wait` переведён в `Implemented`, включён в `SmokeRequired` и публикуется с final annotations;
- добавлены L2 integration tests на public handler, tools/list schema/annotations, sanitization и target policy handoff;
- `scripts/smoke.ps1` теперь реально гоняет `windows.wait` по `active_window_matches`, `element_exists`, `element_gone`, `text_appears`, `focus_is` и `visual_changed`, а `WinBridge.SmokeWindowHost` получил deterministic focus/visual fixture без hidden activation tricks;
- generated docs, source-of-truth docs и `docs/CHANGELOG.md` синхронизированы по фактическому shipped state.

## Test ladder

### L1. Runtime / contract tests

Обязательные сценарии:

- request/result defaults и parameter validation;
- precedence `explicit -> attached -> active`;
- stale explicit и stale attached не fallback-ятся;
- `active_window_matches` success / timeout / ambiguous;
- `element_exists` и `element_gone` на stable и ambiguous selector;
- `text_appears` с `Name`, `ValuePattern` и provider fallback;
- `focus_is` c retry/revalidation при unavailable focused element;
- `visual_changed` с threshold policy, noisy-change suppression, честным detection timeout и отдельной evidence-budget semantics;
- evidence builder и artifact naming;
- `status -> isError` mapping.

### L2. Server-side integration tests

Обязательные сценарии:

- `WindowTools.Wait(...)` возвращает `CallToolResult` с `structuredContent` и одним text block;
- annotations отражают real behavior: `readOnly=false`, `destructive=false`, `idempotent=false`, `openWorld=true`;
- `tools/list`, `okno.contract` и exporter не расходятся с lifecycle `windows.wait`;
- attached-session path и explicit target path ведут себя так же, как в runtime policy;
- non-success statuses дают `isError = true` и не маскируются под обычный text payload.

### L3. Smoke

Обязательные сценарии:

- attach helper window и дождаться `active_window_matches` после explicit activation step;
- подтвердить `element_exists` и `text_appears` на детерминированных helper controls;
- подтвердить `focus_is` на control, который helper явно переводит в focus;
- подтвердить `visual_changed` на helper window с предсказуемым visual transition;
- проверить creation JSON artifact и, если `visualEvidenceStatus = materialized`, referenced capture artifacts;
- подтвердить, что `windows.wait` не требует hidden activation и не ломает `STDIO` transport.

## Docs sync

При public rollout синхронизируются:

- этот exec-plan;
- `docs/architecture/observability.md`;
- `docs/product/okno-spec.md`;
- `docs/product/okno-roadmap.md`;
- `docs/generated/project-interfaces.md`;
- `docs/generated/project-interfaces.json`;
- `docs/generated/commands.md`;
- `docs/generated/test-matrix.md`;
- `docs/bootstrap/bootstrap-status.json`;
- `docs/CHANGELOG.md`.

Generated docs обновляются только после фактических `build/test/smoke`, а не по предположению о будущем shipped state.

## Rollback

- Если `focus_is` не проходит стабильность из-за provider churn, `windows.wait` не переводится в `Implemented`; partial public claim без этой condition запрещён.
- Если `visual_changed` даёт noisy false-positive path, приоритетный rollback — ужесточить threshold/recheck policy или временно удержать rollout, а не понизить success criteria.
- Если UIA search оказывается слишком дорогим, rollback — сузить search root и selector policy; desktop-root descendant traversal не допускается как emergency fallback.
- Если public handler или smoke показывают drift между runtime и generated docs, truth остаётся за lifecycle `Deferred` до полного выравнивания.
- Любой rollback должен сохранять honesty: лучше deferred tool и явный gap, чем shipped `windows.wait`, который периодически врёт о наступлении condition.

## Checklist

- [x] Goal сформулирован как shipped public slice, а не как “сделать waits”.
- [x] V1 scope и non-goals зафиксированы до implementation order.
- [x] `windows.wait` закреплён как polling-first tool.
- [x] Зафиксирован precedence `explicit -> attached -> active`.
- [x] Зафиксировано, что hidden activation fallback запрещён.
- [x] По каждой V1 condition есть authoritative source и owner slice.
- [x] Зафиксирована status model `done | timeout | ambiguous | failed`.
- [x] Зафиксировано, что `isError = false` только для `done`.
- [x] Evidence contract требует отдельный JSON artifact path.
- [x] L1/L2/L3 ladder и docs sync перечислены отдельно.
- [x] Rollback для `focus_is` и `visual_changed` описан явно.
- [x] Typed wait contracts и `ResolveWaitTarget(...)` оформлены в коде без premature runtime rollout.
- [x] Public contract `windows.wait` переведён в `Implemented`, а manifest safety class выровнен под artifact-writing side effect.
- [x] Package A реализован.
- [x] Package B реализован.
- [x] Package C реализован.
- [x] Package D реализован.
- [x] `windows.wait` переведён в `Implemented` и подтверждён smoke.
