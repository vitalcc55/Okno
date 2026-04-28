# ExecPlan: computer-use-win drag prerequisites

## Completion status

- Status: prerequisites satisfied on branch `codex/computer-use-win-next-actions-design`
- Closure path: implemented by `Stage 10` in
  [completed-2026-04-28-computer-use-win-next-actions](./completed-2026-04-28-computer-use-win-next-actions.md)
- Stage 10 commit: `3a0db59`
- Follow-up: main wave closure and this prerequisite plan are now both archived
  in `docs/exec-plans/completed/`

## Goal

Подготовить минимально достаточный engineering base, после которого `drag`
можно будет честно поднять в public `computer-use-win` surface без optimistic
success и без слабого geometry proof.

## Historical prerequisite gap

Historical note: bullets below capture the original prerequisite gap that this
plan was created to close. They are preserved as closure context and no longer
describe the latest repo state after Stage 10 promotion.

Текущий `Stage 8` main wave зафиксировал evidence-based deferral:

- [Win32InputPlatform](../../../src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs)
  всё ещё возвращает explicit `unsupported_action_type` для `DispatchDrag(...)`.
- [Win32InputService](../../../src/WinBridge.Runtime.Windows.Input/Win32InputService.cs)
  не содержит shipped drag execution branch с factual move/down/up lifecycle.
- current helper/smoke harness не даёт deterministic public proof для
  source/target drag story.
- official Microsoft drag/drop docs описывают отдельный drag/drop semantic
  model и state/events, которого текущий shipped projection/runtime пока не
  materialize-ит.

Пока эти prerequisites не закрыты, public promotion `drag` будет слабее уже
shipped quality bar для `click` / `scroll` / `perform_secondary_action`.

## Official docs baseline

- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [UI Automation Support for Drag-and-Drop](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-support-for-drag-and-drop)
- [Drag Control Pattern](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingdrag)

## Scope

1. **Input runtime proof**
   - реализовать real Win32 drag dispatch path поверх одного runtime;
   - materialize pointer-down / move / up side-effect boundaries без false
     success;
   - покрыть cancellation/partial-dispatch semantics.

2. **Endpoint proof**
   - определить exactly-one source mode и exactly-one destination mode;
   - разделить stale source vs stale destination vs geometry drift;
   - закрепить confirm-required contract для coordinate drag.
   - закрепить public result invariant: любой post-dispatch path возвращает
     explicit `refreshStateRecommended=true` / fresh `get_app_state`
     guidance, включая `verify_needed` и partial-dispatch outcomes.

3. **Deterministic helper and smoke**
   - добавить dedicated helper surface для drag source/target;
   - получить factual post-drag proof через public `get_app_state` или другой
     already-shipped observe path;
   - закрыть fresh-thread install/publication smoke.

4. **Promotion contour**
   - только после пунктов 1-3 поднимать `drag` в manifest/registration/docs.

## Non-goals

- не строить второй runtime;
- не публиковать low-level `windows.input` как user-facing replacement;
- не использовать hidden clipboard/shell automation как shortcut;
- не расширять `perform_secondary_action`/`scroll` заново в рамках этого плана.

## Work items

### W1. Drag runtime dispatch

- RED: focused runtime tests на down/move/up sequence, partial dispatch,
  cancellation after committed side effect.
- Implement: factual drag path в `Win32InputPlatform` / `Win32InputService`.
- GREEN: runtime tests + no regressions for `click` / `scroll`.

### W2. Computer Use endpoint proof

- RED: source stale, destination stale, geometry drift, mixed selector modes,
  confirm-required coordinate drag.
- Implement: `ComputerUseWinDragContract`, `ComputerUseWinDragTargetResolver`,
  `ComputerUseWinDragExecutionCoordinator`.
- GREEN: integration tests distinguish stale/unsupported/verify-needed honestly.

### W3. Helper + smoke

- Add dedicated helper drag surface with deterministic observable result.
- Add live smoke:
  `get_app_state -> drag -> get_app_state`
- Add install-surface proof for promoted tool list only after GREEN smoke.

### W4. Promotion

- Promote `drag` in manifest/registration/generated docs.
- Run review/re-review and separate stage commit in the future branch.

## Acceptance

- no public `drag` without full dispatch proof;
- coordinate drag always requires explicit confirmation;
- source and destination failures are classified separately;
- public `drag` result always recommends fresh `get_app_state` после любого
  post-dispatch path;
- smoke proves factual post-drag state change through public surface;
- install/publication surface stays in sync with manifest/generated docs.
