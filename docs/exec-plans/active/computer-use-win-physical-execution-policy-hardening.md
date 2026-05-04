# ExecPlan: Computer Use for Windows physical execution policy hardening

Status: `active`  
Date: `2026-05-04`

## 1. Goal

Усилить `computer-use-win` после shipped screenshot-first hardening не через
новые tools, а через общий policy/observability слой для physical execution в
poor-UIA / weak-semantic GUI.

Этот workstream должен:

- сделать explicit execution facts частью product truth model;
- отличать semantic execution, expected physical execution и fallback physical execution;
- централизовать risky physical confirmation и shared physical-input policy;
- подготовить почву для app capability hints/playbooks и later `region_capture`,
  не смешивая это всё в одну broad wave.

## 2. Non-goals

- не делать OCR subsystem;
- не проектировать “второй курсор” или driver-level HID path;
- не расширять public tool count;
- не возвращать hidden clipboard/paste defaults;
- не делать browser/app-native executor частью этого workstream;
- не подменять shipped `observeAfter=true` или `verify_needed` optimistic success semantics.

## 3. Why this is next

Current repo state already proves:

- screenshot-first observation is shipped;
- poor-UIA `type_text` fallback is shipped;
- `observeAfter=true` successor-state is shipped;
- strict `windowId` continuity reuse is shipped.

What is still missing is a single coherent answer to:

- when an action is semantic vs physical;
- when physical input is expected vs fallback;
- what shared Windows resources were touched;
- how risky physical paths are guarded;
- how user/operator interference should be handled.

## 4. Primary packages

### Package A: execution fact envelope

- extend current `computer_use_win.action.completed` story from partial fields
  (`dispatch_path`, `risk_class`, `fallback_used`) to a more explicit
  execution-fact model;
- decide which fields become public result fields vs audit-only fields;
- keep redaction strict.

Target questions:

- `executionMode`
- `executionReason`
- `physicalInputUsed`
- `systemCursorMoved`
- `foregroundChanged`
- `keyboardFocusChanged`
- `clipboardTouched`
- `successorStateAvailable`

### Package B: physical input lease / policy

- centralize risky physical behavior instead of leaving it fragmented across
  individual coordinators;
- define policy classes for short click, keyboard text, wheel, drag and
  multi-step physical batches;
- define interruption / user-activity / foreground-not-acquired behavior.

### Package C: approvals and risky confirmation integration

- widen the current approvals follow-up into a real physical-execution policy
  layer;
- make confirmation semantics consistent across coordinate click, coordinate
  typing, wheel, drag and any future poor-UIA physical paths;
- preserve fail-closed integrity / foreground / stale-state semantics.

### Package D: app capability hints and playbooks handoff

- document what should remain a later playbook/capability-hint slice;
- define how app-specific hints can consume execution facts without replacing
  them;
- avoid shipping full capability memory until Packages A-C are stable.

## 5. Strategic guardrails

- No `second cursor` roadmap item.
- No `UIA-first` assumption for weak-semantic targets.
- No public `hwnd + processId` selector.
- No hidden physical behavior.
- No fake semantic `done` from physical dispatch.
- `observeAfter` remains the normal post-action truth path for low-confidence
  physical actions.

## 6. Expected outcome

After this workstream the project should be able to say, in code and in docs:

- physical input is common but not casual;
- screenshot-first + physical input is an expected mode for some app classes;
- every risky physical action is explicitly guarded and observable;
- successor observation remains the confidence mechanism for low-semantic UI;
- app playbooks and `region_capture` build on top of this layer rather than
  compensating for its absence.

## 7. Next after this plan

If this plan lands well, the likely next order remains:

1. app playbooks / capability hints expansion
2. `windows.region_capture`
3. `windows.clipboard_get` / `windows.clipboard_set`
4. `windows.uia_action`
