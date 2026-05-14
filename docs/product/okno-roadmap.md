# Okno Roadmap

_Живой delivery roadmap проекта: текущий capability map, порядок ближайшей поставки и product-facing приоритеты._

## 0. Назначение roadmap

Этот документ нужен не для того, чтобы расписывать implementation stages за агентов.

Его задача проще и практичнее:

- держать один **живой capability map** по реальному состоянию репозитория;
- фиксировать **порядок доставки slices**, а не внутреннюю механику их реализации;
- показывать, что уже shipped, что ещё только declared, и что действительно идёт следующим;
- удерживать проект в продуктовой логике `observe -> launch/open -> wait -> act -> verify`, а не в бесконечном internal framework work.

Детальные design/implementation steps должны жить в exec-plans, а не в roadmap.

При этом roadmap обязан держать не только ближайший queue, но и полный вектор
продукта. Если long-horizon направление уже понятно по repo state, product
vision, active workstreams и reference research, его нужно фиксировать здесь как
будущий slice и правильную хронологию, а не оставлять знанием только в чатах
или локальных заметках.

Для текущего long-horizon направления два companion implementation-facing
документа уже подготовлены заранее и должны читаться как будущие опорные
exec-plans, а не как немедленный queue:

- `docs/exec-plans/planned/okno-agent-native-runtime-leadership.md`
- `docs/exec-plans/planned/okno-native-visual-performance.md`

## 1. Принципы roadmap

- roadmap описывает **user-facing и repo-facing slices**, а не подробные этапы их кодинга;
- статусы опираются на текущий repo state, `ToolContractManifest`, `project-interfaces`, tests и smoke, а не на старые намерения;
- если shipped surface расходится с roadmap, правится roadmap;
- если slice важен для Codex/OpenAI use case, это должно отражаться в порядке его доставки;
- `Okno` развивается как один непрерывный продукт: roadmap не разводит
  внутренние волны в отдельные пользовательские family/поколения, кроме release
  versioning и фактических shipped/deferred boundaries;
- OpenAI `computer use` для нас не replacement, а compatibility target: roadmap должен помогать строить тихий, понятный, agent-friendly Windows runtime.

## 2. Как читать статусы

- `реализовано` — slice уже есть в shipped runtime surface и подтверждается current build/test/smoke/docs.
- `частично` — slice реально существует, но покрывает только часть конечной продуктовой области.
- `декларировано` — есть contracts / seams / deferred tool surface, но shipped behavior ещё нет.
- `запланировано` — отдельного slice в repo пока нет.

Процент готовности нужен как грубая инженерная оценка ширины покрытия, а не как точная метрика.

## 3. Текущее состояние репозитория

По состоянию на `2026-05-14` проект уже давно не находится в фазе ранней заготовки.

Что фактически уже есть:

- локальный `STDIO` MCP runtime;
- shipped observe baseline: `list_monitors`, `list_windows`, `attach`, `focus`, `activate`, `capture`;
- shipped semantic/readiness baseline: `windows.uia_snapshot`, `windows.wait`, `okno.health`;
- shipped launch family: `windows.launch_process`, `windows.open_target`;
- shipped click-first action layer: `windows.input` для `move`, `click`, `double_click` и `click(button=right)` с smoke/fresh-host proof;
- shipped public Codex-facing operator surface: plugin/profile `computer-use-win` с `list_apps`, `get_app_state`, `click`, `press_key`, `set_value`, `type_text`, `scroll`, `perform_secondary_action`, `drag`;
- shipped `observeAfter=true` successor-state path для выбранных public actions;
- observability уже несёт часть execution facts (`dispatch_path`, `risk_class`,
  `fallback_used`, `observe_after_requested`, `successor_state_available`);
- advisory app playbooks уже существуют для небольшого набора известных apps;
- shared safety/gating/redaction/evidence foundation;
- sequential verification loop `build -> test -> smoke -> refresh-generated-docs -> verify`.

То есть roadmap ниже — это уже roadmap **полноценного продукта**, а не “первой демки”.

## 4. Capability Map

| Slice | Repo / tools | Что реально покрыто сейчас | Статус | Готовность | Волна |
| --- | --- | --- | --- | --- | --- |
| 01 | `src/WinBridge.Runtime.Contracts` + `Tooling` + `Server` + `Diagnostics` | MCP host, contract export, execution policy, gated boundary, audit/evidence, programmatic tool registration | `частично` | `85%` | `База` |
| 02 | `src/WinBridge.Runtime.Session` + `src/WinBridge.Runtime.Windows.Shell` + `okno.session_state` / `windows.list_windows` / `windows.attach_window` / `windows.activate_window` / `windows.focus_window` | session snapshot, live window inventory, attach/focus/activate, target resolution | `частично` | `80%` | `База` |
| 03 | `src/WinBridge.Runtime.Windows.Display` + `windows.list_monitors` + `windows.capture` | monitor identity, desktop/window capture, PNG artifacts, capture evidence | `реализовано` | `90%` | `База` |
| 04 | `src/WinBridge.Runtime.Windows.UIA` + `windows.uia_snapshot` | explicit/attached/active semantic snapshot, artifact + runtime evidence | `реализовано` | `85%` | `Ядро` |
| 05 | `src/WinBridge.Runtime.Waiting` + `windows.wait` | window/focus/element/text/visual waits, runtime evidence, smoke-proven conditions | `реализовано` | `90%` | `Ядро` |
| 06 | `okno.health` + runtime guard layer + safety baseline | readiness snapshot, shared gate, dry-run/confirmation model, redaction-first launch/input/clipboard baseline | `реализовано` | `95%` | `Ядро` |
| 07 | `src/WinBridge.Runtime.Windows.Launch` + `windows.launch_process` | direct process launch через `ProcessStartInfo`, preview, factual result modes, launch artifacts | `реализовано` | `90%` | `Ядро` |
| 08 | `src/WinBridge.Runtime.Windows.Launch` + `windows.open_target` | shell-open для `document` / `folder` / `url(http/https)`, safe preview, factual result, open-target artifacts | `реализовано` | `90%` | `Ядро` |
| 09 | `plugins/computer-use-win` + `src/WinBridge.Server/ComputerUse` | public-facing Codex operator surface `list_apps`, `get_app_state`, `click`, `press_key`, `set_value`, `type_text`, `scroll`, `perform_secondary_action`, `drag` поверх внутреннего Okno engine, runtime-owned strict `windowId` continuity reuse для unchanged discovery snapshots, отдельный publication profile и self-contained plugin-local install artifact | `частично` | `95%` | `R2-следом` |
| 10 | `src/WinBridge.Runtime.Windows.Input` + public Computer Use action wave (`press_key`, `set_value`, `type_text`, `scroll`, `perform_secondary_action`, `drag`) | текущая global action wave для `computer-use-win`; весь целевой action set уже shipped в public callable surface, а `drag` больше не остаётся deferred: runtime/input path materialize-ит separate source/destination proof, factual move/down/move/up dispatch, helper smoke и install/publication proof | `реализовано` | `93%` | `R2-следом` |
| 11 | `plugins/computer-use-win` + poor-UIA `type_text` fallback follow-up | explicit `allowFocusedFallback=true` fallback for poor-UIA text-entry-like targets after screenshot-first navigation: focused path with fresh target-local focus proof, plus coordinate-confirmed `capture_pixels` point path for top-level-only Qt/custom UI focus, always `confirm=true`, no raw screen-coordinate typing, no arbitrary focused-clickable typing, no hidden previous-click reuse, no clipboard default and public `verify_needed` semantics. Repo/helper proof and cache-installed Telegram product acceptance are complete; result honesty remains screenshot-confirmed dispatch, not semantic `done`. | `реализовано` | `100%` | `R2-следом` |
| 12 | `plugins/computer-use-win` + successor-state/action+observe follow-up | explicit `observeAfter=true` post-action reobserve path для `click`, `press_key`, `type_text`, `scroll` и `drag`: nested `successorState`, updated screenshot image block, новый short-lived `stateToken`, factual top-level action status и advisory `successorStateFailure` без optimistic semantic proof | `реализовано` | `100%` | `R2-следом` |
| 13 | `plugins/computer-use-win` + physical execution policy hardening | explicit execution facts for semantic vs expected-physical vs fallback-physical paths, risky physical confirmation, shared physical-input lease/policy, user-interference/foreground integrity semantics, target-local proof discipline and clearer proof-weighted action results without widening the public tool surface | `запланировано` | `0%` | `R2` |
| 14 | `plugins/computer-use-win` + app playbooks / capability hints | shipped advisory app instructions for a few known apps today; next step is broader app playbooks plus lightweight capability hints/memory so the runtime can distinguish strong-semantic, poor-UIA expected-physical, dialog-like, browser-like and terminal-like targets without widening the public tool surface | `частично` | `25%` | `R2` |
| 15 | proposed `windows.region_capture` | narrow visual crop by explicit region or capture-derived target area for verify-after-action, ROI/frame-digest proof, low-noise visual successor evidence and future bounded OCR/visual fallback bridge | `запланировано` | `0%` | `R2` |
| 16 | `src/WinBridge.Runtime.Windows.Clipboard` + `windows.clipboard_get` / `windows.clipboard_set` | explicit clipboard read/write surface как отдельный slice; clipboard stays an explicit shared resource and must not flow back into hidden `type_text` defaults | `декларировано` | `15%` | `R2` |
| 17 | `src/WinBridge.Runtime.Windows.UIA` + `windows.uia_action` | semantic action layer поверх shipped `uia_snapshot` и gate/readiness foundation: invoke/value/toggle/select/scroll patterns, fresh element revalidation and executor-grade proof for UIA-friendly targets | `декларировано` | `10%` | `R2` |
| 18 | proposed `windows.dialog` | common dialogs: open/save/confirm, path input, accept/close flow | `запланировано` | `0%` | `R2` |
| 19 | proposed `windows.surface_lifecycle` | claim/reconcile/close only owned shell/window/dialog surfaces after `launch_process` / `open_target`; fail-closed на reused unowned surface | `запланировано` | `0%` | `R2-R3` |
| 20 | proposed `windows.menu` / `windows.taskbar` / `windows.tray` | desktop surfaces beyond core window automation | `запланировано` | `0%` | `R2-R3` |
| 21 | `scripts/*` + `docs/generated/*` + smoke/verify control plane | bootstrap/build/test/smoke/refresh-generated-docs/ci, generated surface sync, deterministic local proof loop and the minimal proof-smoke / benchmark baseline harness needed to validate near-term action-proof slices before the later full benchmark suite exists | `частично` | `70%` | `Операции` |
| 22 | proposed `daemon` / `overlay` / `virtual desktop` / richer shell modes | background companion, visualizer, virtual desktop support, deeper shell/runtime modes | `запланировано` | `0%` | `R3+` |
| 23 | `plugins/computer-use-win` + proof envelope / semantic successor proof | structured action results with `execution facts`, `action receipt`, `recommended recovery`, successor-state-backed semantic proof and honest `verify_needed` when accepted proof is absent; strengthens current action tools instead of adding a competing action family | `запланировано` | `0%` | `R2-R3` |
| 24 | proposed stable target identity / lease substrate | runtime-owned target identity above legacy `elementIndex`: target proof digest, stale/ambiguous fail-close behavior, target lease semantics and stronger continuity across observe -> act -> verify loops while keeping the current public operator surface stable | `запланировано` | `0%` | `R3` |
| 25 | proposed bounded OCR / visual proof provider | narrow OCR/visual provider for region-level verification and poor-UIA proof, bounded by capture references and policy gates; not a broad OCR-first discovery subsystem | `запланировано` | `0%` | `R3` |
| 26 | proposed local browser/Electron substrate | local-only browser/Electron evidence/executor lane for targets that genuinely need it; no separate browser tool zoo in the main product path and no remote endpoint trust by default | `запланировано` | `0%` | `R3` |
| 27 | proposed controlled terminal surface | explicit terminal-like observation/action substrate with prompt/output proof and risk classification; terminal remains a controlled surface, not a hidden shell shortcut inside GUI actions | `запланировано` | `0%` | `R3+` |
| 28 | `tests/*` + `samples/*` + benchmark/proof suite | full product proof contour for Windows Computer Use scenarios: stale-state blocking, wrong-click rate, DPI/multi-monitor, poor-UIA fallback, dialogs, safety gates and later browser/terminal slices, built on top of the earlier minimal proof-smoke harness from the operations contour | `запланировано` | `0%` | `Операции/R3` |
| 29 | proposed native visual performance substrate | repo-facing acceleration for raw frame analysis, successor visual proof and future region-level verification using Rust first and C++/WinRT only if capture acquisition proves to be the bottleneck; starts only after region/crop geometry contract and benchmark baseline are fixed; no native-specific public tools | `запланировано` | `0%` | `Операции/R2-R3` |

## 5. Ближайший порядок доставки

Текущий practical order такой:

1. `computer-use-win` physical execution policy hardening + execution facts envelope
2. minimal proof-smoke / benchmark baseline for current action-proof slices
3. app playbooks expansion + lightweight capability hints
4. `windows.region_capture`
5. `windows.clipboard_get` / `windows.clipboard_set`
6. `windows.uia_action`
7. proof envelope / semantic successor proof
8. native visual benchmark baseline + region/crop geometry contract
9. native visual performance substrate for `windows.wait` / `observeAfter` / `windows.region_capture`
10. `windows.dialog`
11. `windows.surface_lifecycle`
12. `windows.menu` / `windows.taskbar` / `windows.tray`
13. stable target identity / lease substrate
14. bounded OCR / visual proof provider
15. local browser/Electron substrate
16. controlled terminal surface
17. public benchmark / proof suite hardening
18. `daemon` / `overlay` / `virtual desktop` / richer shell modes

Почему именно так:

- reference repos показывают, что зрелые runtimes почти всегда быстро приходят к app/window/input/dialog/menu families;
- official OpenAI `computer use` loop делает input vocabulary и quiet action semantics важнее, чем поздние shell niceties;
- для poor-UIA / weak-semantic приложений physical mouse/keyboard path уже стал first-class execution mode, а не редким исключением; после shipped screenshot-first hardening следующий логичный шаг — не новый tool zoo, а общий policy/observability слой, который честно различает semantic path, expected physical path и fallback physical path;
- Windows не даёт проекту поддерживаемую модель второго независимого системного курсора в том же интерактивном desktop, поэтому roadmap не должен уходить в “second cursor” research. Вместо этого physical input надо делать explicit, measured, guarded and successor-verified внутри текущего state/action/observe loop;
- текущая observability уже partially показывает `dispatch_path`, `risk_class`, `fallback_used`, `observe_after_requested` и `successor_state_available`, но следующему workstream нужен более цельный execution-fact envelope и единая physical-input policy, прежде чем расширять breadth;
- minimal proof-smoke нельзя откладывать до позднего benchmark wave: текущим
  slices `physical policy`, `region_capture`, `uia_action` и `proof envelope`
  уже нужен ранний measurable harness для stale-state blocking, foreground
  blocking, risky physical confirmation и successor observation;
- poor-UIA text-entry implementation path закрыт как bounded `type_text` fallback slice через explicit `allowFocusedFallback=true` + `confirm=true`: focused path требует fresh target-local focus proof, а coordinate-confirmed path принимает explicit `capture_pixels` point из последнего screenshot state для top-level-only Qt/custom UI, без raw screen-coordinate typing, hidden previous-click reuse, clipboard default и optimistic `done`; real Telegram/cache-installed product acceptance пройден как Stage 8 proof через ordinary plugin tools;
- successor-state / action+observe gap тоже закрыт explicit `observeAfter=true` path: честный `verify_needed` больше не обязан автоматически означать полный следующий `get_app_state`, если runtime уже вернул nested `successorState` и screenshot image block;
- следующий шаг по current surface — не разрастание vocabulary, а более строгий
  proof envelope: `execution facts`, `action receipt`, `recommended recovery`
  и successor-state-backed semantic proof поверх уже shipped tools;
- live product feedback по `windowId` churn уже закрыт strict selector reuse: repeated unchanged `list_apps` snapshots сохраняют прежний runtime-owned `windowId`, а drift/replacement paths всё ещё fail-close без перехода к наивному public id на базе `hwnd + processId`;
- shipped fallback сохраняет boundary: screenshot-first navigation в poor-UIA apps работает, text entry без editable proof допускается только с explicit focused proof или coordinate-confirmed point proof, а clipboard, OCR, region_capture или broad shell hacks остаются отдельными later slices;
- advisory app instructions уже есть, но capability memory/profile layer пока отсутствует; поэтому app playbooks лучше расширять после того, как physical execution policy и execution facts станут единообразными;
- reference repos и текущий `observe/capture` stack показывают, что narrow `region_capture` даёт более дешёвый verify-after-action loop и полезен как мост к visual fallback, не размывая capture family в OCR/browser subsystem;
- stable target identity / lease substrate должен прийти после current
  policy/region/UIA work и усилить уже существующий surface, а не ломать
  текущий `stateToken + elementIndex` contract раньше времени;
- native visual performance не должен стартовать как isolated Rust experiment:
  сначала фиксируется region/crop geometry contract и benchmark baseline, и
  только потом raw frame analysis уходит в native substrate;
- native visual acceleration должна рассматриваться как repo-facing substrate для
  `windows.wait`, `observeAfter` и `windows.region_capture`, а не как отдельная
  user-facing feature family;
- уже shipped `launch_process` и `open_target` закрыли start/open baseline, поэтому next product value теперь в action layer;
- OCR/visual, browser/Electron и terminal lanes допустимы только как bounded
  later substrates после core action/policy/proof work, а не как ранний broad
  subsystem;
- benchmark/proof suite должен стать частью product truth, потому что лидерство
  по Windows Computer Use surface должно подтверждаться pass-rate, wrong-click
  rate, stale-state fail-close и safety gates, а не только описанием в docs;
- `surface_lifecycle` важен, но без clipboard/dialog и broad action coverage он не даст полноценный teardown path.

## 6. OpenAI / Codex Alignment

Проект строится так, чтобы быть максимально удобным для Codex и в целом для OpenAI agent loops.

Это означает:

- tool surface должен быть **не шумным** и semantically clear;
- capture, wait, launch/open и input должны оставаться отдельными понятными primitives, а не сваливаться в один “do anything” tool;
- текущий Codex-facing product path идёт через `computer-use-win` plugin/profile поверх внутреннего Okno engine;
- `windows.input` и соседние `windows.*` slices должны усиливать этот product path как внутренний substrate, а не конкурирующий public UX;
- future work should keep the public Codex-facing operator surface continuous:
  текущие `list_apps`, `get_app_state`, `click`, `press_key`, `set_value`,
  `type_text`, `scroll`, `perform_secondary_action`, `drag` остаются основным
  action vocabulary, а deeper proof layers усиливают их изнутри;
- screenshot-first navigation и explicit physical input для weak-semantic targets должны считаться допустимым и ожидаемым execution mode, а не только awkward fallback after semantic failure; но такой physical path должен быть явно измеримым, policy-controlled и подтверждаемым successor observation;
- `windows.input` нужно проектировать vocabulary-compatible с типовым `computer use` action family:
  - `move`
  - `click`
  - `double_click`
  - `drag`
  - `scroll`
  - `type`
  - `keypress`
- `windows.capture` и `windows.wait` должны оставаться отдельными explicit steps;
- built-in `computer use` guide отдельно нормализует screenshot-first cycle:
  первый turn часто начинается со screenshot, после action batch harness
  возвращает updated screenshot, а значит `get_app_state`/capture-first loops
  и shipped `observeAfter=true` successor-state path должны оставаться
  first-class image paths, а не path-only metadata wrappers;
- следующий policy слой должен уметь отличать как минимум:
  - semantic/UIA execution,
  - expected physical execution for poor-UIA targets,
  - fallback physical execution after weak or failed semantic proof,
  не превращая это в новый public tool family;
- если future external/client loop downscale-ит screenshots, координаты должны remap-иться обратно в original geometry basis; `captureReference` и future screenshot-first flows нельзя трактовать как free-form resized image space без coordinate discipline;
- narrow follow-up вроде `windows.region_capture` должен усиливать visual proof после actions, но не превращать visual stack в primary OCR-first mode раньше времени;
- native visual acceleration может оптимизировать внутренний screenshot/grid/diff/crop analysis, но `captureReference`, screenshot image blocks и координаты должны оставаться в original geometry basis без отдельного resized coordinate space;
- browser/Electron, terminal и future identity/lease substrates должны сначала
  усиливать текущий operator surface, а не появляться как competing public UX;
- `windows.launch_process` и `windows.open_target` должны оставаться split;
- отдельный OpenAI-native adapter, если когда-нибудь понадобится, остаётся отдельным будущим слоем поверх `Okno`; текущий активный путь не через него, а через `computer-use-win`.

## 7. Что roadmap сознательно не делает

Roadmap не должен:

- превращаться в подробный implementation checklist;
- хранить исторический narrative, который уже не совпадает с текущим repo state;
- дублировать exec-plans;
- маскировать declared/deferred slice под “почти готово”, если shipped behavior ещё нет;
- строить порядок работ вокруг internal purity вместо product usefulness для agent loops.

## 8. Что нельзя размывать раньше времени

- не расширять `windows.input` вширь за пределы уже shipped `click`-first contract без отдельного proof;
- не разводить продукт на внутренние family names/поколения: усиливается один
  непрерывный shipped surface, а не несколько параллельных продуктов;
- не смешивать `windows.launch_process` и `windows.open_target`;
- не прятать attach/focus/cleanup как hidden side effect launch/open tools;
- не решать cleanup reused shell surfaces внутри `windows.open_target`;
- не тащить broad OCR/browser/remote/daemon work раньше core action layer;
- не ослаблять typed result/evidence model ради convenience shortcuts.
- не раздувать `windows.region_capture` в broad OCR/browser subsystem раньше узкого verify-after-action use case.
- не добавлять broad `observe_world` / `discover_entities` / `act` public surface
  раньше, чем текущий `computer-use-win` action surface получит достаточную
  proof depth;
- не превращать native visual acceleration в отдельную public tool family;
- не скрывать terminal/shell execution внутри GUI action semantics;
- не строить roadmap вокруг “второго курсора” или driver-level HID tricks: shared system cursor/input stream остаются product reality и должны описываться policy/observability слоями, а не обходиться красивым narrative.

## 9. Verification policy

Для каждого shipped slice сохраняется один и тот же инженерный контур:

- `L1`: unit / contract / validator tests
- `L2`: server/integration tests
- `L3`: real smoke через живой `STDIO` runtime
- docs sync: `project-interfaces`, `commands`, `observability`, `CHANGELOG`, relevant exec-plan

Для performance-substrate work добавляется `L0`: замерить current managed path,
native path и fallback path на representative frame sizes до повышения статуса.

Для proof-first Computer Use slices дополнительно обязательны product gates:

- stale-state action must fail closed;
- wrong-window / wrong-foreground action must fail closed;
- risky physical path must expose dispatch facts и confirmation semantics;
- committed action должен вернуть либо successor observation, либо explicit
  successor failure;
- `done` требует accepted proof; иначе результат остаётся `verify_needed`;
- benchmark/proof traces должны позволять восстановить state lineage, dispatch
  path, policy decision и recovery hint.

Roadmap поднимает status только после фактического завершения этого контура.

## 10. Итог в одной фразе

`Okno` уже нужно развивать не как “первую версию”, а как shipped Windows-native agent runtime: держать capability map честным, следующий delivery order узким и понятным, а все новые slices проверять через реальный Codex/OpenAI use case, а не через внутреннюю красоту архитектуры.
