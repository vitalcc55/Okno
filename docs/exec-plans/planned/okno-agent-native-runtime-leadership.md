# ExecPlan: Okno agent-native runtime leadership

Status: `planned`  
Date: `2026-05-14`

## 1. Goal

Сделать `Okno` бесспорно сильным Windows-native agent runtime не через
разрастание public tool zoo, а через более строгий и быстрый контур:

```text
observe -> target proof -> guarded dispatch -> successor observe -> semantic proof
```

Результат должен быть один и целостный продукт, а не семейство внутренних
surfaces/поколений. Release versioning остаётся единственным местом, где
используются версии продукта.

## 2. Product boundary

Текущий public Codex-facing operator surface остаётся основным:

```text
list_apps
get_app_state
click
press_key
set_value
type_text
scroll
perform_secondary_action
drag
```

Этот plan не про то, чтобы срочно заменить его на новую action vocabulary.
Он про то, чтобы усилить этот surface изнутри:

- richer execution facts;
- stronger target proof;
- successor-state-backed semantic proof;
- stable recovery hints;
- bounded later substrates for poor-UIA, browser-like and terminal-like targets.

## 3. Non-goals

- не создавать новый `do anything` tool;
- не заменять shipped `stateToken + elementIndex` contract до отдельного
  migration decision;
- не превращать OCR в primary discovery subsystem;
- не превращать browser/Electron lane в отдельный public product раньше времени;
- не прятать terminal/shell shortcuts внутри GUI actions;
- не размывать launch/open separation;
- не ослаблять typed result/evidence model ради convenience.

## 4. Delivery mapping to roadmap

| Roadmap slice | Workstream here | Why it exists |
| --- | --- | --- |
| 13 physical execution policy hardening | execution facts, risky confirmation, physical policy | сделать physical path explicit и доказуемым |
| 14 app playbooks / capability hints | target capability classification | отличать strong-semantic vs poor-UIA vs dialog/browser/terminal-like |
| 15 region_capture | target-local visual proof | сделать verify-after-action дешевле и точнее |
| 17 windows.uia_action | semantic executor substrate | сделать UIA path preferred для UIA-friendly apps |
| 18 windows.dialog | dialog-specific semantics | убрать случайные generic flows на common dialogs |
| 19 surface_lifecycle | ownership model | корректный teardown и fail-close на reused surfaces |
| 23 proof envelope / semantic successor proof | action receipt + semantic diff + recovery | public result becomes reconstructable |
| 24 stable target identity / lease substrate | entity/lease internals | stronger continuity without surface breakage |
| 25 bounded OCR / visual proof | poor-UIA verification | OCR stays bounded and proof-oriented |
| 26 local browser/Electron substrate | browser lane | internal evidence/executor path only |
| 27 controlled terminal surface | terminal lane | terminal remains controlled and explicit |
| 28 benchmark/proof suite | measurable leadership | лидерство доказывается, а не декларируется |

### Entry conditions for active implementation

Этот plan может переходить из `planned` в `active` только при соблюдении
следующих условий:

1. roadmap и companion exec-plans уже живут в canonical repo state;
2. первым implementation slice остаётся `computer-use-win physical execution
   policy hardening`, а не OCR/CDP/terminal breadth;
3. первый PR package обязан включать единый `executionFacts` envelope и
   классификацию `semantic / expected_physical / fallback_physical`;
4. рядом с первым workstream materialize-ится minimal proof-smoke, иначе
   proof-first semantics будет трудно удержать;
5. future native visual work допускается только после baseline benchmarks и при
   обязательном managed fallback.

## 5. Core success model

Для каждого action runtime должен уметь честно ответить на три вопроса:

```text
Что он видел?
Почему именно этот target/action были допустимы?
Что изменилось после действия?
```

Если хотя бы на один из этих вопросов нет ответа, публичный результат должен
быть:

```text
verify_needed
approval_required
blocked
failed
```

но не optimistic `done`.

## 6. Workstream A — execution facts and physical policy

### Goal

Собрать один coherent слой для:

- semantic execution;
- expected physical execution;
- fallback physical execution;
- confirmation semantics;
- foreground/integrity/user-interference facts.

### Files and zones

```text
src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinActionFinalizer.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinActionObservability.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinClickExecutionCoordinator.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinScrollExecutionCoordinator.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinDragExecutionCoordinator.cs
src/WinBridge.Server/ComputerUse/ComputerUseWinTargetPolicy.cs
```

### Deliverables

- explicit execution-fact envelope in public action results;
- consistent risky physical confirmation rules;
- policy-owned classification:
  - `semantic`
  - `expected_physical`
  - `fallback_physical`
- no hidden physical behavior.

### Acceptance

- low-risk UIA action stays concise and non-noisy;
- poor-UIA physical action exposes dispatch path and proof facts;
- medium/high-risk physical action requires confirmation;
- committed physical input without accepted proof remains `verify_needed`.

### Minimum `executionFacts` envelope

До начала реализации форма ниже должна считаться минимальным contract floor, а
не одной из возможных трактовок:

```json
{
  "executionFacts": {
    "dispatchPath": "semantic|expected_physical|fallback_physical",
    "executor": "uia.invoke|uia.value|physical.click|physical.type|physical.drag|...",
    "riskClass": "low|medium|high",
    "confirmationRequired": false,
    "confirmationSatisfied": false,
    "targetProof": {
      "stateTokenPresent": true,
      "captureReferencePresent": true,
      "windowContinuity": "accepted|failed|unknown",
      "targetLocalProof": "uia|focus|point|region|none"
    },
    "foregroundIntegrity": "accepted|failed|unknown",
    "fallbackUsed": false,
    "fallbackReason": null
  }
}
```

Implementation may later add more fields, but these fields form the smallest
useful envelope for product truth.

### Companion minimal proof-smoke

This is not the later full benchmark suite. It is the immediate harness required
to keep slices `13`, `15`, `17` and `23` honest.

Minimum scenarios:

```text
proof-smoke/core-action-receipt
proof-smoke/stale-state-block
proof-smoke/wrong-foreground-block
proof-smoke/physical-confirmation
proof-smoke/successor-observe-or-failure
```

Minimum gates:

- stale state blocks action;
- wrong foreground blocks action;
- risky physical path cannot silently dispatch;
- committed action returns successor observation or explicit successor failure;
- `executionFacts` and receipt materialize in result/trace.

## 7. Workstream B — target capability classification

### Goal

Научить runtime классифицировать target early, а не после случайного dispatch:

```text
strong_semantic
weak_semantic
poor_uia_expected_physical
dialog_like
browser_like
terminal_like
unknown
```

### Deliverables

- lightweight capability profiles by process/framework/observed UI quality;
- app playbooks consume these hints but do not replace policy;
- action layer can pick the right proof path earlier.

### Acceptance

- strong WPF/WinForms/WinUI targets do not drift into casual physical fallback;
- known poor-UIA targets are classified before text/click attempts;
- unknown apps stay conservative.

## 8. Workstream C — region-level visual proof

### Goal

Сделать `windows.region_capture` узким, дешёвым и useful proof primitive для:

- verify-after-action;
- successor visual evidence;
- poor-UIA fallback proof.

### Deliverables

- target-local ROI capture bound to current geometry basis;
- frame/region digest support;
- region proof reusable by semantic successor verification.

### Acceptance

- region capture does not become broad OCR/browser subsystem;
- stale capture references fail closed;
- region-level proof can support `verify_needed` -> proof escalation path.

## 9. Workstream D — semantic UIA action substrate

### Goal

Предпочитать semantic/UIA dispatch wherever the target honestly supports it.

### Required patterns

```text
InvokePattern
ValuePattern
TogglePattern
SelectionItemPattern
ExpandCollapsePattern
ScrollPattern
RangeValuePattern
```

### Deliverables

- internal `windows.uia_action` substrate;
- action router can choose semantic path before physical input;
- fresh element revalidation stays the gold standard.

### Acceptance

- buttons prefer Invoke;
- editable text prefers Value;
- toggles/selectors prefer pattern-owned semantic dispatch;
- missing pattern returns explicit downgrade decision, not silent coordinate click.

## 10. Workstream E — proof envelope and semantic successor proof

### Goal

Сделать `successorState` не просто convenience snapshot, а полноценным proof
source.

### Deliverables

- structured `actionReceipt`;
- successor-state-backed `semanticDiff`;
- `recommendedRecovery` for non-terminal outcomes;
- proof-weighted `done` semantics.

### Diff classes

```text
entity_value_changed
entity_text_changed
entity_enabled_changed
focus_changed
modal_appeared
modal_dismissed
window_changed
window_title_changed
scroll_position_changed
visual_region_changed
terminal_output_changed
browser_url_changed
```

### Contract separation

Before implementation starts, the following semantic split must be treated as
non-negotiable:

```text
actionReceipt = что runtime сделал и на основании какого target proof
semanticDiff = что реально изменилось после действия
acceptedProof = почему top-level result может стать `done`
recommendedRecovery = что делать дальше, если proof недостаточен
```

`actionReceipt` is not a diff log.  
`semanticDiff` is not an execution log.  
`recommendedRecovery` is not a substitute for proof.

### Acceptance

- committed action with accepted proof can end as `done`;
- committed action without accepted proof stays `verify_needed`;
- successor observation failure remains advisory but explicit.

## 11. Workstream F — stable target identity and lease substrate

### Goal

Усилить continuity across `observe -> act -> verify` loops without breaking
current public surface.

### Deliverables

- runtime-owned stable target identity;
- target proof digest;
- stale/ambiguous fail-close behavior;
- internal lease semantics above legacy `elementIndex`.

### Boundary

Public tools remain compatible while internals move from:

```text
stateToken + elementIndex
```

toward:

```text
stateToken + stable target identity + proof digest + lease semantics
```

### Acceptance

- unchanged UI can preserve target identity;
- drifted UI fails closed;
- ambiguous target cannot dispatch optimistically.

## 12. Workstream G — bounded OCR / visual proof provider

### Goal

Поддержать poor-UIA targets without turning the product into OCR-first runtime.

### Deliverables

- OCR/visual proof only where bounded by capture reference, policy and target-local
  verification;
- no broad global OCR discovery;
- no hidden raw-coordinate shortcuts.

### Acceptance

- OCR can verify text in a region;
- low-confidence visual proof requires confirmation;
- no capture reference means no casual physical proof.

## 13. Workstream H — optional browser/Electron substrate

### Goal

Добавить internal browser/Electron lane only when it improves the current
Windows control product path.

### Deliverables

- local-only browser/Electron evidence/executor substrate;
- no remote endpoint trust by default;
- browser lane uses same proof envelope and policy model as GUI actions.

### Acceptance

- local browser/Electron target can be handled without creating a separate
  browser tool family;
- proof, masking and policy stay consistent with the rest of the product.

## 14. Workstream I — controlled terminal surface

### Goal

Сделать terminal-like targets explicit and controlled rather than hidden shell
shortcuts inside GUI actions.

### Deliverables

- prompt/output observation;
- command risk assessment;
- successor output diff;
- policy-controlled command send.

### Acceptance

- destructive command classes require confirmation or are blocked;
- terminal output can participate in successor proof;
- GUI actions do not silently fall through to terminal execution.

## 15. Workstream J — benchmark and proof suite

### Goal

Сделать лидерство measurable.

This is the later full suite. It builds on top of the earlier minimal
proof-smoke and does not replace it.

### Suites

```text
core
uia
poor-uia
dialog
visual-proof
dpi-monitor
safety
browser
terminal
```

### Key gates

- wrong-click rate = 0 in covered suite;
- undetected stale-state action = 0;
- unconfirmed high-risk physical action = 0;
- committed action without receipt = 0;
- proof-bearing `done` only.

## 16. Recommended implementation order

```text
1. execution facts envelope
2. physical policy hardening
3. minimal proof-smoke
4. capability hints / playbooks
5. region capture + ROI proof
6. UIA semantic substrate
7. proof envelope / semantic successor proof
8. dialog / clipboard / surface lifecycle per roadmap
9. stable target identity / lease substrate
10. bounded OCR / visual proof
11. full benchmark/proof suite expansion
12. browser/Electron substrate
13. controlled terminal surface
14. later runtime breadth like daemon/overlay/virtual desktop
```

## 17. Definition of done

This plan is materially complete only when:

1. every action result carries enough execution facts to explain dispatch;
2. every committed action has reconstructable receipt-level evidence;
3. stale/ambiguous targets fail closed;
4. UIA-friendly targets prefer semantic execution;
5. poor-UIA fallback stays explicit, proof-bound and policy-controlled;
6. accepted proof is required for top-level `done`;
7. benchmark/proof traces demonstrate that the above remains true in real
   product scenarios.
