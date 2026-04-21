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

## 1. Принципы roadmap

- roadmap описывает **user-facing и repo-facing slices**, а не подробные этапы их кодинга;
- статусы опираются на текущий repo state, `ToolContractManifest`, `project-interfaces`, tests и smoke, а не на старые намерения;
- если shipped surface расходится с roadmap, правится roadmap;
- если slice важен для Codex/OpenAI use case, это должно отражаться в порядке его доставки;
- OpenAI `computer use` для нас не replacement, а compatibility target: roadmap должен помогать строить тихий, понятный, agent-friendly Windows runtime.

## 2. Как читать статусы

- `реализовано` — slice уже есть в shipped runtime surface и подтверждается current build/test/smoke/docs.
- `частично` — slice реально существует, но покрывает только часть конечной продуктовой области.
- `декларировано` — есть contracts / seams / deferred tool surface, но shipped behavior ещё нет.
- `запланировано` — отдельного slice в repo пока нет.

Процент готовности нужен как грубая инженерная оценка ширины покрытия, а не как точная метрика.

## 3. Текущее состояние репозитория

По состоянию на `2026-04-20` проект уже давно не находится в фазе ранней заготовки.

Что фактически уже есть:

- локальный `STDIO` MCP runtime;
- shipped observe baseline: `list_monitors`, `list_windows`, `attach`, `focus`, `activate`, `capture`;
- shipped semantic/readiness baseline: `windows.uia_snapshot`, `windows.wait`, `okno.health`;
- shipped launch family: `windows.launch_process`, `windows.open_target`;
- shipped click-first action layer: `windows.input` для `move`, `click`, `double_click` и `click(button=right)` с smoke/fresh-host proof;
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
| 09 | `plugins/computer-use-win` + `src/WinBridge.Server/ComputerUse` | public-facing Codex operator surface `list_apps`, `get_app_state`, `click` поверх внутреннего Okno engine, отдельный publication profile и self-contained plugin-local install artifact | `частично` | `55%` | `R2-следом` |
| 10 | `src/WinBridge.Runtime.Windows.Input` + public Computer Use action wave (`type_text`, `press_key`, `scroll`, `drag`) | следующая глобальная action wave для `computer-use-win`; сейчас только planning/touchpoints, не shipped implementation | `декларировано` | `10%` | `R2-следом` |
| 11 | proposed `windows.region_capture` | narrow visual crop by explicit region or capture-derived target area for verify-after-action, low-noise visual proof and future OCR fallback bridge | `запланировано` | `0%` | `R2` |
| 12 | `src/WinBridge.Runtime.Windows.Clipboard` + `windows.clipboard_get` / `windows.clipboard_set` | explicit clipboard read/write surface как отдельный slice | `декларировано` | `15%` | `R2` |
| 13 | `src/WinBridge.Runtime.Windows.UIA` + `windows.uia_action` | semantic action layer поверх shipped `uia_snapshot` и gate/readiness foundation | `декларировано` | `10%` | `R2` |
| 14 | proposed `windows.dialog` | common dialogs: open/save/confirm, path input, accept/close flow | `запланировано` | `0%` | `R2` |
| 15 | proposed `windows.surface_lifecycle` | claim/reconcile/close only owned shell/window/dialog surfaces after `launch_process` / `open_target`; fail-closed на reused unowned surface | `запланировано` | `0%` | `R2-R3` |
| 16 | proposed `windows.menu` / `windows.taskbar` / `windows.tray` | desktop surfaces beyond core window automation | `запланировано` | `0%` | `R2-R3` |
| 17 | `scripts/*` + `docs/generated/*` + smoke/verify control plane | bootstrap/build/test/smoke/refresh-generated-docs/ci, generated surface sync, deterministic local proof loop | `частично` | `70%` | `Операции` |
| 18 | proposed `daemon` / `overlay` / `virtual desktop` / richer shell modes | background companion, visualizer, virtual desktop support, deeper shell/runtime modes | `запланировано` | `0%` | `R3+` |

## 5. Ближайший порядок доставки

Текущий practical order такой:

1. public Computer Use action wave: `type_text`, `press_key`, `scroll`, `drag`
2. app approvals hardening + risky action confirmation
3. app playbooks expansion
4. `windows.region_capture`
5. `windows.clipboard_get` / `windows.clipboard_set`
6. `windows.uia_action`
7. `windows.dialog`
8. `windows.surface_lifecycle`
9. `windows.menu` / `windows.taskbar` / `windows.tray`

Почему именно так:

- reference repos показывают, что зрелые runtimes почти всегда быстро приходят к app/window/input/dialog/menu families;
- official OpenAI `computer use` loop делает input vocabulary и quiet action semantics важнее, чем поздние shell niceties;
- reference repos и текущий `observe/capture` stack показывают, что narrow `region_capture` даёт более дешёвый verify-after-action loop и полезен как мост к visual fallback, не размывая capture family в OCR/browser subsystem;
- уже shipped `launch_process` и `open_target` закрыли start/open baseline, поэтому next product value теперь в action layer;
- `surface_lifecycle` важен, но без clipboard/dialog и broad action coverage он не даст полноценный teardown path.

## 6. OpenAI / Codex Alignment

Проект строится так, чтобы быть максимально удобным для Codex и в целом для OpenAI agent loops.

Это означает:

- tool surface должен быть **не шумным** и semantically clear;
- capture, wait, launch/open и input должны оставаться отдельными понятными primitives, а не сваливаться в один “do anything” tool;
- `windows.input` нужно проектировать vocabulary-compatible с типовым `computer use` action family:
  - `move`
  - `click`
  - `double_click`
  - `drag`
  - `scroll`
  - `type`
  - `keypress`
- `windows.capture` и `windows.wait` должны оставаться отдельными explicit steps;
- narrow follow-up вроде `windows.region_capture` должен усиливать visual proof после actions, но не превращать visual stack в primary OCR-first mode раньше времени;
- `windows.launch_process` и `windows.open_target` должны оставаться split;
- future OpenAI adapter — это отдельный слой поверх `Okno`, а не причина размывать core runtime contract.

## 7. Что roadmap сознательно не делает

Roadmap не должен:

- превращаться в подробный implementation checklist;
- хранить исторический narrative, который уже не совпадает с текущим repo state;
- дублировать exec-plans;
- маскировать declared/deferred slice под “почти готово”, если shipped behavior ещё нет;
- строить порядок работ вокруг internal purity вместо product usefulness для agent loops.

## 8. Что нельзя размывать раньше времени

- не расширять `windows.input` вширь за пределы уже shipped `click`-first contract без отдельного proof;
- не смешивать `windows.launch_process` и `windows.open_target`;
- не прятать attach/focus/cleanup как hidden side effect launch/open tools;
- не решать cleanup reused shell surfaces внутри `windows.open_target`;
- не тащить broad OCR/browser/remote/daemon work раньше core action layer;
- не ослаблять typed result/evidence model ради convenience shortcuts.
- не раздувать `windows.region_capture` в broad OCR/browser subsystem раньше узкого verify-after-action use case.

## 9. Verification policy

Для каждого shipped slice сохраняется один и тот же инженерный контур:

- `L1`: unit / contract / validator tests
- `L2`: server/integration tests
- `L3`: real smoke через живой `STDIO` runtime
- docs sync: `project-interfaces`, `commands`, `observability`, `CHANGELOG`, relevant exec-plan

Roadmap поднимает status только после фактического завершения этого контура.

## 10. Итог в одной фразе

`Okno` уже нужно развивать не как “первую версию”, а как shipped Windows-native agent runtime: держать capability map честным, следующий delivery order узким и понятным, а все новые slices проверять через реальный Codex/OpenAI use case, а не через внутреннюю красоту архитектуры.
