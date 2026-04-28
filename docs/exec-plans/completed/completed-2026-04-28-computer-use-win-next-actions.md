# ExecPlan: Computer Use for Windows action wave

## Completion status

- Status: completed on branch `codex/computer-use-win-next-actions-design`
- Drag shipment commit: `3a0db59`
- Closure path: archived final wave record; stage-by-stage instructions below
  are preserved as historical execution evidence rather than an active work
  queue

> **Для agentic workers:** этот файл является completed execution record для
> public action wave `computer-use-win`. Stage-by-stage sections below are kept
> as historical evidence of the delivery order, verification ladder and review
> gates that produced the shipped result.

## 1. Goal

Спроектировать и доставить следующую public action wave для `computer-use-win`
поверх уже shipped loop:

```text
list_apps -> get_app_state -> click -> get_app_state
```

Цель не в том, чтобы "добавить ещё несколько action tools". Цель - расширить
quiet operator-facing profile так, чтобы Codex получал небольшой, понятный и
доказуемый surface поверх внутреннего Windows-native engine Okno.

Инварианты цели:

- `computer-use-win` остаётся public Codex-facing plugin/profile.
- `Okno` / `WinBridge` остаётся внутренним Windows-native engine.
- `windowId` остаётся discovery-scoped selector.
- `stateToken` остаётся short-lived observed-state proof.
- approval, target policy, continuity proof и installed-surface guarantees не
  ослабляются ради новых actions.
- новый action становится implemented только после полного
  contract/runtime/test/docs/smoke proof.

## 2. Non-goals

- Не строить второй runtime рядом с `computer-use-win`.
- Не возвращать public UX обратно к low-level `windows.*` narrative.
- Не смешивать OpenAI-specific DTO/glue с `WinBridge.Runtime` /
  `WinBridge.Server`.
- Не вводить clipboard/paste как скрытый shortcut для text paths.
- Не делать broad redesign всего `windows.input`, если изменение нужно только
  для public `computer-use-win` action path.
- Не реализовывать overlay cursor как часть текущего action protocol.
- Не публиковать "optimistic success" для действий, где runtime не доказал
  dispatch или outcome хотя бы как `verify_needed`.
- Не копировать reference repos как source of truth: они дают паттерны и
  анти-паттерны, но не заменяют repo-local contract и official platform docs.

## 3. Current repo state

Дата среза: `2026-04-27`.

Ветка планирования: `codex/computer-use-win-next-actions-design`.

Operational snapshot note:

- Плановый docs-sync пакет был зафиксирован отдельным commit
  `70e2251 docs: expand computer-use-win action wave plan`.
- Этот operational context intentionally volatile. Stage 0 обязан заново
  выполнить `git status --short --branch`, сверить `main..HEAD` и обновить
  stage report по фактическому состоянию worktree на момент реализации.
- Не доверять historical dirty-worktree notes как текущей правде; если в
  worktree есть чужие правки, работать с ними по стандартному repo policy и не
  откатывать без явного запроса.

Фактический baseline:

- `windows.input` shipped как click-first engine slice, но broader structural
  action slots ещё не означают product-ready runtime dispatch для всей wave.
- Public Codex-facing путь идёт через profile/plugin `computer-use-win`, а не
  через low-level `windows.*` tools.
- Отдельный OpenAI-native adapter переведён в historical/superseded track.
- Server уже profile-aware и публикует `windows-engine` или `computer-use-win`
  через explicit surface profile.
- Current implemented `computer-use-win` subset:
  `list_apps`, `get_app_state`, `click`.
- Current next-wave target в architecture/roadmap:
  `press_key`, `set_value`, `type_text`, `scroll`,
  `perform_secondary_action`, `drag`.

Критичный gap:

- `src/WinBridge.Runtime.Tooling/ToolNames.cs` уже содержит
  `press_key`, `type_text`, `scroll`, `drag`.
- `set_value` и `perform_secondary_action` пока отсутствуют как declared tool
  names.
- `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs` уже содержит
  request DTO для `press_key`, `type_text`, `scroll`, `drag`, но не содержит DTO
  для `set_value` и `perform_secondary_action`.
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` и generated
  `computer-use-win` interfaces пока знают deferred wave только как
  `type_text`, `press_key`, `scroll`, `drag`.
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs` правильно содержит
  только shipped public entrypoints; latent actions не должны возвращаться в
  adapter до promotion stage.

Вывод: Stage 1 обязан явно синхронизировать target contract и declared
deferred map для всех шести actions. Нельзя молча начинать runtime work только
с уже существующих четырёх names.

## 4. Public contract direction

Итоговый target public set:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

Форма surface:

- Отдельные public tools, а не batch/multiplexing tool.
- Нельзя прятать эту wave внутрь `windows.input`.
- `windows.input` остаётся engine substrate и reusable dispatch capability.
- `ComputerUseWinTools` остаётся thin MCP adapter.
- Contract, validation, state resolution, result semantics и audit ownership
  живут в explicit owner classes под `src/WinBridge.Server/ComputerUse`.

Publication phases:

1. **Declared deferred:** tool name, description, manifest row, generated docs
   говорят, что action является target/deferred, но `computer-use-win` profile
   не публикует его как implemented callable.
2. **Runtime proof:** request DTO, validator, handler/coordinator,
   low-level/semantic dependency и tests готовы, но public promotion ещё
   заблокирована smoke/docs proof.
3. **Implemented public:** tool появляется в `tools/list` для profile
   `computer-use-win`, registration/schema/generated docs/smoke/install proof
   синхронизированы в том же commit-пакете.

## 5. Per-action design matrix

| Action | Primary intent | Contract shape | Primary path | Fallback policy | Result semantics |
| --- | --- | --- | --- | --- | --- |
| `press_key` | Отправить клавишу или короткое повторение в уже подготовленное окно. | `stateToken`, key literal, bounded repeat/count, optional confirmation for risky keys. | Foreground/action-ready proof + internal `windows.input` keypress dispatch. | Layout/VK/unicode decision фиксируется в Stage 1/3; dangerous combinations require confirmation; no blind global keypress when state is stale/blocked. | `done` только при factual dispatch proof; `verify_needed` если dispatch есть, но UI outcome не доказан; structured failure for unsupported key/layout/foreground. |
| `set_value` | Семантически установить значение конкретного элемента. | `stateToken`, `elementIndex`, value payload, optional value kind if needed. | Fresh UIA revalidation + `ValuePattern` / `RangeValuePattern` / approved settable semantic path. | Blind typing fallback запрещён как silent substitute; focus+typing можно добавить только как explicit low-trust branch with confirmation and docs. | `done` только после semantic set proof; `unsupported_action` for non-settable target; `stale_state` for revalidation mismatch. |
| `type_text` | Ввести текст в доказанную активную область. | `stateToken`, text payload, optional `elementIndex` if Stage 1 approves it. | Fresh editable proof for element target, or stored focused editable proof if no element target. | Clipboard/paste is not default; no implicit focus guessing; whitespace-only text remains valid if contract permits insertion. | Usually `verify_needed` after dispatch unless runtime can prove text value; failure distinguishes missing editable target vs stale state. |
| `scroll` | Прокрутить элемент, область или точку. | `stateToken`, direction/amount, one target mode: `elementIndex` or point/capture reference. | UIA `ScrollPattern` for scrollable element. | Wheel input at fresh coordinate only with geometry proof; coordinate fallback may require confirmation for risky targets. | `done` for semantic proof, `verify_needed` for wheel fallback, structured failure for unsupported/no-movement/stale geometry. |
| `perform_secondary_action` | Выполнить product-owned вторичное действие над элементом. | `stateToken`, `elementIndex`, optional action hint if Stage 1 proves need. | Fresh UIA tree + explicit semantic action mapping from element affordances. | Right-click/context menu path only as explicit fallback requiring confirmation/reobserve; not a mere alias for right-click. | `done` for semantic action proof; `verify_needed` after context menu fallback; `unsupported_action` when no strong secondary affordance exists. |
| `drag` | Выполнить drag between proven source and destination. | `stateToken`, exactly one source mode and one destination mode: element or point. | Fresh revalidation for both endpoints, then internal input drag dispatch. | Coordinate drag is high-risk and always explicit-confirm; if runtime proof is weak, keep deferred and create prerequisite ExecPlan. | Always recommend fresh `get_app_state`; likely `verify_needed` unless app-specific semantic proof exists. |

Shared action invariants:

- Every action requires `stateToken`.
- No action uses `windowId` to bypass observed-state proof.
- No action promotes `blocked`, `stale`, missing approval, missing capture proof
  or missing UIA proof into action-ready state.
- Action-specific prerequisite failures must not be masked as continuity
  failures: example, missing capture proof should reach action contract as
  `capture_reference_required`, not become generic `stale_state`.
- `get_app_state` after action remains canonical next step.

## 6. Integration points by file

### Public contract / profile system

| File | Ownership for this wave |
| --- | --- |
| `src/WinBridge.Runtime.Tooling/ToolNames.cs` | Add missing `ComputerUseWinSetValue` and `ComputerUseWinPerformSecondaryAction`; keep naming aligned with public literals. |
| `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs` | Add descriptions for all target actions; descriptions must mention state-first contract and avoid low-level `windows.*` narrative. |
| `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` | Keep implemented subset honest; add missing deferred rows first; promote one action per stage only after proof. |
| `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs` | Confirm generated profile/export preserves implemented vs deferred lifecycle and MCP annotations. |
| `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs` | Add request DTOs for missing actions; normalize existing DTOs; keep strict JSON boundary and action-specific validation. |

### Composition root / publication boundary

| File | Ownership for this wave |
| --- | --- |
| `src/WinBridge.Server/Program.cs` | Register new owner classes only when corresponding stage needs them; avoid service locator or late-bound closure regression. |
| `src/WinBridge.Server/ComputerUse/ComputerUseWinRegisteredTools.cs` | Add typed MCP wrapper methods only when action is ready for implemented publication. |
| `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs` | Add schemas/annotations per promoted action; keep `readOnly=false`, `destructive/openWorld` honest for OS side effects. |
| `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs` | Keep as thin adapter; no latent callable public methods for deferred actions. |

### Existing state/action lifecycle

| File | Ownership for this wave |
| --- | --- |
| `ComputerUseWinStateStore.cs` | Reuse bounded stored observation entries; do not introduce action-specific token stores unless root cause demands it. |
| `ComputerUseWinStoredStateResolver.cs` | Shared state token resolution; preserve mode-specific observed-state proof for coordinate vs semantic paths. |
| `ComputerUseWinRuntimeStateModel.cs` | Confirm no hidden transition from `blocked/stale` to action-ready for new actions. |
| `ComputerUseWinActionLifecyclePhase.cs` | Extend lifecycle phases only if new action paths need phase-specific refresh semantics. |
| `ComputerUseWinActionFinalizer.cs` | Reuse/extend result materialization; avoid per-handler result maps that drift. |
| `ComputerUseWinToolResultFactory.cs` | Keep structured `CallToolResult` construction central. |
| `ComputerUseWinFailureCodeMapper.cs` | Add public failure literals only as contract decisions, with tests and docs. |
| `ComputerUseWinAuditDataBuilder.cs` | Add safe audit fields for each action; no raw `stateToken`, no sensitive text leakage. |

### Identity / approval / continuity

| File | Ownership for this wave |
| --- | --- |
| `ComputerUseWinIdentityModel.cs` | No new action may mint reusable `windowId`; selectors remain discovery-scoped and phase-aware. |
| `ComputerUseWinApprovalStore.cs` | Reuse app-level approval; do not invent action approval as substitute for app approval. |
| `ComputerUseWinWindowContinuityProof.cs` | Keep path-specific continuity: discovery selector, attached refresh, observed state, coordinate capture proof. |
| `ComputerUseWinTargetPolicy.cs` | Extend confirmation/risk rules for dangerous keys, coordinate fallback, context-menu fallback and drag. |

### Discovery / observation / existing click path

| File | Ownership for this wave |
| --- | --- |
| `ComputerUseWinAppDiscoveryService.cs` | Should not change for action publication except generated contract docs if selector semantics are referenced. |
| `ComputerUseWinGetAppStateTargetResolver.cs` | No action may bypass this by resolving live window directly from stale selector. |
| `ComputerUseWinAppStateObserver.cs` | Source of stored observation envelope; add action affordance metadata only after projection contract decision. |
| `ComputerUseWinGetAppStateHandler.cs` | Keep commit-on-success and post-activation selector publication invariant. |
| `ComputerUseWinClickContract.cs` | Characterization baseline for action validation style. |
| `ComputerUseWinClickTargetResolver.cs` | Extract reusable element revalidation only if new actions would otherwise duplicate logic. |
| `ComputerUseWinClickExecutionCoordinator.cs` | Characterization baseline for activation/preflight/input dispatch/failure mapping. |
| `ComputerUseWinClickHandler.cs` | Do not turn click handler into a generic action router. |
| `ComputerUseWinAffordanceResolver.cs` | Extend projected affordances deliberately: settable, scrollable, secondary-action signals. |
| `ComputerUseWinAccessibilityProjector.cs` | Add only compact public metadata needed for action selection; avoid raw UIA dump expansion. |

### Low-level engine dependencies

| File | Ownership for this wave |
| --- | --- |
| `src/WinBridge.Runtime.Windows.Input/IInputService.cs` | Reuse for `press_key`, `type_text`, `scroll`, `drag` only after runtime dispatch proof. |
| `src/WinBridge.Runtime.Windows.Input/Win32InputService.cs` | Current structural slots are not enough; each new dispatch path needs factual tests. |
| `src/WinBridge.Runtime.Windows.Input/IInputPlatform.cs` | Existing dispatch touchpoints for text/key/scroll/drag must move from unsupported to implemented stage-by-stage. |
| `src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs` | Implement `SendInput`-backed paths with layout/foreground/UIPI limitations surfaced truthfully. |
| `src/WinBridge.Runtime.Windows.Input/InputCoordinateMapper.cs` | Reuse capture/screen mapping for scroll/drag point paths; no ad hoc geometry math. |
| `src/WinBridge.Runtime.Contracts/CaptureReferenceGeometryPolicy.cs` | Coordinate fallback must reuse existing capture-reference drift policy. |
| `src/WinBridge.Runtime.Windows.UIA/IUiAutomationService.cs` | Current service is snapshot-oriented; semantic set/scroll/secondary actions may require a narrow action seam instead of overloading snapshot. |
| `src/WinBridge.Runtime.Windows.UIA/AutomationSnapshotNode.cs` | Existing pattern availability flags (`value`, `range_value`, `scroll`, `invoke`, `toggle`, etc.) are the starting affordance source. |

### Existing tests that define the floor

| Test file | Role |
| --- | --- |
| `tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs` | Main action/projection contract floor; add per-action behavior tests here or adjacent. |
| `ComputerUseWinArchitectureTests.cs` | Guard public profile, thin adapter, no latent callable methods. |
| `ComputerUseWinFinalizationTests.cs` | Finalizer/refresh/failure taxonomy coverage. |
| `ComputerUseWinObservationTests.cs` | Observation and stored-state preconditions. |
| `ComputerUseWinInstallSurfaceTests.cs` | Installed-copy/publication proof when surface changes. |
| `McpProtocolSmokeTests.cs` | `tools/list`, schema, annotations and stdio MCP proof. |
| `tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs` | Manifest lifecycle and profile proof. |
| `ToolContractExporterTests.cs` | Generated/export contract proof. |
| `WindowInputToolTests.cs` | Low-level input runtime behavior proof; not a substitute for public `computer-use-win` tests. |

## 7. Official docs and constraints

### OpenAI / Codex / computer use

Checked sources:

- [OpenAI Computer Use Guide](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [OpenAI MCP and Connectors Guide](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)
- [Codex app features: Native Windows sandbox](https://developers.openai.com/codex/app/features#native-windows-sandbox)
- [OpenAI Docs MCP](https://developers.openai.com/learn/docs-mcp)
- [OpenAI Developer Docs MCP endpoint](https://developers.openai.com/mcp)
- [openai-cua-sample-app](https://github.com/openai/openai-cua-sample-app)
- [openai-testing-agent-demo](https://github.com/openai/openai-testing-agent-demo)

Constraints for this plan:

- Visual computer use is naturally a loop: screenshot/state, actions, updated
  screenshot/state, repeat.
- A custom harness/MCP integration is valid when a mature local automation
  harness already exists.
- Tool list size/noise matters; public profile should stay narrow and
  allowed-tool friendly.
- Official sample repos reinforce keeping one explicit action-loop owner and
  separating scenario/workflow orchestration from the public action vocabulary.
- Public action tools should not absorb workflow-control semantics like
  scenario completion signals; those belong outside the operator action surface.
- Risk/approval is product design, not transport decoration.
- Codex Windows runs natively with PowerShell and Windows sandbox support; this
  supports keeping `computer-use-win` Windows-native instead of adding another
  runtime.
- OpenAI Developer Docs MCP is a read-only documentation server for
  developers.openai.com / platform.openai.com; it should be used as the first
  OpenAI-doc lookup path for later implementation/review work, but it is not a
  product runtime dependency and does not call OpenAI APIs on behalf of Okno.

### MCP 2025-11-25

Checked sources:

- [MCP Overview 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic)
- [MCP Server Overview 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/server/index)
- [MCP Lifecycle 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)
- [MCP Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP Schema Reference 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/schema)
- [MCP Tasks 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks)
- [MCP Security Best Practices 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices)

Constraints for this plan:

- `tools/list` must describe the actual implemented profile; deferred actions
  cannot leak as callable implemented tools.
- Tool annotations are behavioral hints and must stay honest. Stateful or OS
  side-effecting actions are not read-only/idempotent.
- Public schemas and generated docs must match runtime registration.
- Tool-result failures should be structured tool results when the request
  reached the tool contract; protocol-level errors are reserved for
  protocol/transport failures.
- MCP tasks are optional; this wave should not add task support unless an
  action becomes genuinely long-running and the server/profile contract is
  updated deliberately.

### Microsoft / Win32 / UI Automation

Checked sources:

- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput)
- [GetKeyboardLayout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout)
- [VkKeyScanExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-vkkeyscanexw)
- [MapVirtualKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeya)
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [UI Automation Control Patterns Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-control-patterns-overview)
- [UI Automation Security Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-security-overview)
- [ValuePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.valuepattern?view=windowsdesktop-9.0)
- [RangeValuePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.rangevaluepattern?view=windowsdesktop-9.0)
- [InvokePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.invokepattern?view=windowsdesktop-10.0)
- [ScrollPattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.scrollpattern?view=windowsdesktop-10.0)
- [SelectionItemPattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.selectionitempattern?view=windowsdesktop-9.0)
- [ExpandCollapsePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.expandcollapsepattern?view=windowsdesktop-9.0)
- [TogglePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.togglepattern?view=windowsdesktop-9.0)
- [UI Automation TextPattern Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview)

Constraints for this plan:

- `SendInput` injects into the system input stream and can be blocked by UIPI;
  input failures must become truthful tool failures or `verify_needed`, not
  success.
- Foreground APIs have platform restrictions; failed foreground/preflight cannot
  be retried forever or hidden behind optimistic dispatch.
- Keyboard actions need explicit key normalization and layout handling.
- Semantic UIA patterns are preferred for `set_value`, scroll and secondary
  actions when available.
- UIA security boundaries and provider limitations mean external apps may have
  poor semantic trees; coordinate/input fallback must be explicit and risk
  tagged.

## 8. Reference repo takeaways

Policy source: `docs/architecture/reference-research-policy.md`.

Local reference repos checked:

- `references/Windows-MCP`
- `references/Windows-MCP.Net`
- `references/Peekaboo`
- `references/pywinauto-mcp`

Takeaways:

- `Windows-MCP` confirms broad action vocabulary (`Click`, `Type`, `Scroll`,
  `Shortcut`, etc.) but also shows why Okno should avoid a noisy tool zoo and
  hidden focus assumptions.
- `pywinauto-mcp` confirms real demand for keyboard, text, element set,
  right-click-like and mouse actions, but its portmanteau tools and fallback
  typing style are too broad for `computer-use-win`.
- `Peekaboo` is the most useful reference for snapshot-first actions:
  element-id targeting, exact one-target validation, scroll/drag through
  snapshot elements, and JSON outcome reporting.
- OpenAI sample apps are useful not as direct desktop-runtime templates, but as
  proof that scenario verification and explicit loop ownership matter more than
  broad action transcripts.
- None of the references override current Okno invariants: no weak retargeting,
  no clipboard-default typing, no optimistic success, no broad public surface.

What not to copy:

- broad convenience portmanteau tools;
- hidden attach/focus/activate side effects;
- silent fallback from semantic action to raw input;
- public success without verification;
- workflow-control tools inside the action vocabulary;
- weak identity or stale snapshot reuse.

## 9. Stage-by-stage delivery order

Global execution rule:

- [ ] Work strictly in order: Stage 0 -> Stage 1 -> Stage 2 -> Stage 3 ->
  Stage 4 -> Stage 5 -> Stage 6 -> Stage 7 -> Stage 8 -> Stage 9 -> Stage 10
  -> Stage 11.
- [ ] Do not start a later stage until current stage has checked boxes,
  stage report, green verification, review approval and commit SHA.
- [ ] Update this file after each completed subtask, not only at the end.
- [ ] Each stage that changes behavior, contract, validation, publication,
  protocol, failure path or state semantics must use TDD.

### Review gate before each commit

Before each commit, run at least two `gpt-5.5` review subagents:

- architecture/contract reviewer;
- tests/failure-path/docs/generated reviewer.

Prompt must include:

- stage id;
- scope;
- acceptance criteria;
- changed files;
- diff/base-head context;
- checks run;
- official docs checked;
- reference repos consulted when relevant.

Review findings are hypotheses. Confirm or reject each finding, find root
cause for confirmed findings, decide whether it is local or class-level, fix
root cause, verify neighboring paths, then send re-review to the same agents
until approval.

### Stage report template

```md
#### Отчёт этапа

- Статус этапа: `not_started` / `in_progress` / `blocked` / `ready_for_review` / `approved` / `committed`
- Branch:
- Commit SHA:
- TDD применялся:
- Проверки:
- Review agents:
- Official docs checked:
- Reference repos checked:
- Подтверждённые замечания:
- Отклонённые замечания:
- Исправленные root causes:
- Проверенные соседние paths:
- Остаточные риски:
- Разблокировка следующего этапа:
```

### Stage 0: Baseline and drift closure

**Назначение:** подтвердить фактический repo state и закрыть документационные
расхождения до contract work.

**Read set:**

- `AGENTS.md`
- `docs/product/okno-roadmap.md`
- `docs/product/okno-spec.md`
- `docs/product/okno-vision.md`
- `docs/architecture/computer-use-win-surface.md`
- `docs/architecture/computer-use-win-next-actions.md`
- `docs/architecture/openai-computer-use-interop.md`
- `docs/architecture/capability-design-policy.md`
- `docs/architecture/observability.md`
- `docs/architecture/reference-research-policy.md`
- completed plans for `windows.input`, `computer-use-win-deferred-work`,
  `computer-use-win-install-artifact`, and superseded OpenAI interop.
- generated docs under `docs/generated/`.

**Steps:**

- [x] Confirm branch and current worktree with `git status --short --branch`;
  record whether implementation starts clean or with user-owned changes.
- [x] Confirm current implemented `computer-use-win` profile is only
  `list_apps`, `get_app_state`, `click`.
- [x] Confirm target action gap:
  `set_value` and `perform_secondary_action` missing from `ToolNames`.
- [x] Confirm low-level `windows.input` has structural slots but not complete
  public-ready dispatch proof for this action wave.
- [x] Sync architecture docs if any doc still names the old four-action wave.
- [x] Fill stage report.

#### Отчёт этапа

- Статус этапа: `approved`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `3a0db59`
- TDD применялся: `нет`, stage только фиксирует baseline/drift и не меняет runtime behavior
- Проверки: baseline `git status --short --branch` до stage edits -> clean worktree на `codex/computer-use-win-next-actions-design`; current review diff -> только `M docs/exec-plans/active/computer-use-win-next-actions.md`; `git rev-parse HEAD/main/origin/main/merge-base` -> `HEAD=1c4d519`, `main=origin/main=merge-base=b6b18b9`; `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests"` -> `20/20`; `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests"` -> `50/50`
- Review agents: `McClintock -> approve`, `Pascal -> approve_with_notes`
- Official docs checked: OpenAI Computer Use Guide; MCP Tools spec `2025-11-25`; Microsoft `SendInput`
- Reference repos checked: `not_applicable` for this stage; использованы repo-local completed exec plans и current source files
- Подтверждённые замечания: один class-level docs/process finding - stage report и checkpoint должны различать исходный clean baseline и текущий review diff, а не называть worktree clean без оговорки
- Отклонённые замечания: `none`
- Исправленные root causes: stage report и `.tmp/.codex/task_state/latest.md` теперь явно разделяют baseline `git status` до Stage 0 edits и текущий tracked review diff
- Проверенные соседние paths: `ToolNames.cs`, `ToolDescriptions.cs`, `ToolContractManifest.cs`, `ComputerUseWinContracts.cs`, `ComputerUseWinRequestContractValidator.cs`, `ComputerUseWinTools.cs`, `ComputerUseWinToolRegistration.cs`, `Win32InputPlatform.cs`, `docs/generated/computer-use-win-interfaces.*`
- Остаточные риски: broad `windows.input` seams для `type_text`/`press_key`/`scroll`/`drag` пока только structural stubs; `set_value` и `perform_secondary_action` ещё не declared в manifest/DTO/public docs
- Разблокировка следующего этапа: закрыть review gate Stage 0, записать commit SHA, затем перейти к TDD RED для declared target set в Stage 1

### Stage 1: Target contract and deferred publication map

**Назначение:** declare full six-action target set without promoting runtime
implementation.

**Primary files:**

- `ToolNames.cs`
- `ToolDescriptions.cs`
- `ToolContractManifest.cs`
- `ToolContractExporter.cs`
- `ComputerUseWinContracts.cs`
- `ToolContractManifestTests.cs`
- `ToolContractExporterTests.cs`
- `ComputerUseWinArchitectureTests.cs`
- `McpProtocolSmokeTests.cs`
- generated docs via `scripts/refresh-generated-docs.ps1`
- `docs/CHANGELOG.md`

**Steps:**

- [x] TDD RED: tests show `set_value` and `perform_secondary_action` are
  missing from deferred target map.
- [x] Add missing tool names and descriptions.
- [x] Add deferred manifest rows for all not-yet-implemented target actions.
- [x] Add or normalize request DTO placeholders only if generated schema
  requires them; do not publish handlers yet.
- [x] Keep implemented public profile limited to shipped tools.
- [x] Refresh generated docs through project-native script.
- [x] Update changelog.
- [x] Run targeted tests and smoke assertions for `tools/list`.
- [ ] Review/re-review and commit.

**Acceptance criteria:**

- [x] `ToolNames` contains all six target action names.
- [x] `ToolContractManifest` distinguishes implemented vs deferred honestly.
- [x] Generated `computer-use-win-interfaces.*` shows target/deferred state.
- [x] No latent public callable method appears in `ComputerUseWinTools`.
- [x] `set_value` and `perform_secondary_action` do not appear as implemented.

#### Отчёт этапа

- Статус этапа: `approved`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `pending`
- TDD применялся: `да` (`RED -> GREEN` сначала на missing deferred declarations, затем на manifest/exporter/architecture/tools-list slice)
- Проверки: RED `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ComputerUseWinProfilePublishesVendorLikeImplementedSurface|FullyQualifiedName~ComputerUseWinExportDocumentDeclaresFullSixActionDeferredWave"` -> expected fail `2/2`; RED `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinDeferredWaveDeclaresAllSixTargetActionsWithoutPublishingThem"` -> expected fail `1/1`; GREEN rerun of both targeted tests -> `2/2` and `1/1`; nearby slice `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests"` -> `31/31`; `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> `52/52`; `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> green
- Review agents: `Linnaeus -> approve_with_notes -> approve`, `Kepler -> approve_with_notes -> approve`
- Official docs checked: OpenAI Computer Use Guide; OpenAI MCP and Connectors Guide; MCP Tools spec `2025-11-25`; MCP Schema Reference `2025-11-25`
- Reference repos checked: `not_used`; Stage 1 опирается на repo-local source of truth + official docs
- Подтверждённые замечания: один неблокирующий doc drift - `docs/product/okno-roadmap.md` ещё утверждал, что `set_value` и `perform_secondary_action` не добавлены в declared surface; синхронизировано перед commit
- Отклонённые замечания: `none`
- Исправленные root causes: declaration gap закрыт в `ToolNames`/`ToolDescriptions`/`ToolContractManifest`; deferred DTO surface дополнен `set_value` и `perform_secondary_action`, `type_text` normalized with optional `elementIndex`; generated profile/export now reflects six-action target wave while `ComputerUseWinTools` and registration keep only the shipped three-tool callable surface
- Проверенные соседние paths: `ToolContractManifestTests`, `ToolContractExporterTests`, `ComputerUseWinArchitectureTests`, `McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools`, `docs/generated/computer-use-win-interfaces.*`, `docs/generated/project-interfaces.*`
- Остаточные риски: validator logic for `set_value` / `perform_secondary_action` is placeholder-only until later promotion stages; no public schema/handler/runtime path exists yet for deferred actions by design
- Разблокировка следующего этапа: закрыть review gate Stage 1, сделать отдельный commit, затем перейти к Stage 2 characterization tests for click lifecycle and non-click observability owner

### Stage 2: Shared non-click action lifecycle

**Назначение:** prepare owner seams for new actions without changing `click`
behavior.

**Primary files:**

- `ComputerUseWinStoredStateResolver.cs`
- `ComputerUseWinRuntimeStateModel.cs`
- `ComputerUseWinActionLifecyclePhase.cs`
- `ComputerUseWinActionFinalizer.cs`
- `ComputerUseWinFailureCodeMapper.cs`
- `ComputerUseWinAuditDataBuilder.cs`
- `ComputerUseWinTargetPolicy.cs`
- `docs/architecture/observability.md`
- `ToolContractExporter.cs`
- diagnostics/runtime tests for action event/artifact redaction
- `ComputerUseWinClickContract.cs`
- `ComputerUseWinClickTargetResolver.cs`
- `ComputerUseWinClickExecutionCoordinator.cs`
- `ComputerUseWinActionAndProjectionTests.cs`
- `ComputerUseWinFinalizationTests.cs`

**Steps:**

- [x] Add characterization tests for current click payload/failure/refresh
  semantics.
- [x] Extract only reusable state/action pieces needed by at least two upcoming
  actions.
- [x] Keep click handler/coordinator behavior stable.
- [x] Define and implement common safe audit payload shape for non-click
  actions.
- [x] Define product-level action observability owner:
  `computer_use_win.action.completed` and
  `artifacts/diagnostics/<run_id>/computer-use-win/action-*.json`, unless a
  reviewed stage decision chooses a narrower alternative.
- [x] Add best-effort artifact/event failure tests proving diagnostics failures
  do not downcast factual action result.
- [x] Add redaction tests proving current action artifact/event family does not
  write raw state token or raw exception message; action-specific raw
  text/value/key redaction coverage is deferred to the corresponding promotion
  stages.
- [x] Add tests for no stale/blocked action-ready promotion.
- [ ] Review/re-review and commit.

**Acceptance criteria:**

- [x] `click` behavior and public payloads stay unchanged.
- [x] New lifecycle owner avoids per-action duplication of token/state/audit
  maps.
- [x] Action-specific validators remain action-specific; shared code does not
  swallow specific failure taxonomy.
- [x] Observability contract is explicit before the first action promotion.
- [x] `docs/architecture/observability.md` and generated artifact inventory are
  updated if implementation adds the action artifact/event family.
- [x] No new action is promoted as implemented in this stage.

#### Отчёт этапа

- Статус этапа: `approved`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `pending`
- TDD применялся: `да` (RED на missing action artifact family в exporter и missing `computer_use_win.action.completed` runtime trail; отдельный RED на отсутствие `exception_type` в unexpected action event)
- Проверки: RED `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ExportJsonIncludesArtifactPathsForShippedToolEvidence"` -> expected fail `1/1`; RED `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ActionFinalizerMaterializesComputerUseWinActionArtifactAndRuntimeEvent|FullyQualifiedName~ActionFinalizerKeepsPublicResultWhenActionArtifactWriteFails"` -> expected fail `1/2`; RED `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~UnexpectedActionFailureEventDoesNotLeakRawExceptionMessage"` -> expected fail `1/1`; GREEN reruns of these filters -> `1/1`, `2/2`, `1/1`; nearby runtime slice `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractExporterTests"` -> `11/11`; nearby server slice `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinFinalizationTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~ComputerUseWinArchitectureTests"` -> `120/120`; `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> green
- Review agents: `Sartre -> approve_with_notes -> approve`, `Kuhn -> approve_with_notes -> approve`
- Official docs checked: OpenAI Computer Use Guide; OpenAI MCP and Connectors Guide; MCP Tools spec `2025-11-25`; MCP Schema Reference `2025-11-25`; MCP Security Best Practices `2025-11-25`
- Reference repos checked: `not_used`; Stage 2 опирается на repo-local click lifecycle/tests + official docs
- Подтверждённые замечания: один non-blocking review note - Stage 2 redaction evidence narrowed to current action family (`stateToken` / raw exception message), while explicit text/value/key redaction tests stay deferred to later promotion stages
- Отклонённые замечания:
- Исправленные root causes: общий action lifecycle больше не зашит в `ComputerUseWinClickHandler`; reusable `ComputerUseWinActionRequestExecutor` централизует stored-state resolution, outcome mapping и unexpected-failure handling для будущих actions; `ComputerUseWinActionObservability` materialize-ит product-level safe action artifact/event family best-effort и не downcast-ит публичный result при `artifact_write` / event-sink failure; `AuditInvocationScope` и `AuditLog` расширены минимально только для этого product trail
- Проверенные соседние paths: `ComputerUseWinFinalizationTests`, `ComputerUseWinActionAndProjectionTests`, `ComputerUseWinArchitectureTests`, `ToolContractExporterTests`, `docs/generated/project-interfaces.*`, `docs/generated/computer-use-win-interfaces.*`, `docs/architecture/observability.md`
- Остаточные риски: new action event/artifact family пока использует click-first safe context; richer action-specific safe extensions для text/value/key paths будут добавляться при promotion соответствующих actions; explicit event tests для future raw text/value/key summaries ещё впереди
- Разблокировка следующего этапа: закрыть review gate Stage 2, сделать отдельный commit, затем перейти к Stage 3 official-doc sweep и `press_key` RED

### Stage 3: Implement `press_key`

**Назначение:** first non-click action proof with smallest geometry footprint.

**Primary files:**

- new/updated `ComputerUseWinPressKeyContract.cs`
- new `ComputerUseWinPressKeyHandler.cs`
- new `ComputerUseWinPressKeyExecutionCoordinator.cs`
- `ComputerUseWinRegisteredTools.cs`
- `ComputerUseWinToolRegistration.cs`
- `ComputerUseWinTools.cs`
- `IInputService.cs`
- `IInputPlatform.cs`
- `Win32InputService.cs`
- `Win32InputPlatform.cs`
- `InputActionContract.cs`
- `InputRequestValidator.cs`
- server integration tests and runtime input tests.

**Steps:**

- [x] TDD RED: request validation, missing state, stale state, blocked/approval
  inheritance, risky key confirmation, unsupported key/layout, success path.
- [x] Define key literal grammar and normalization.
- [x] Implement layout/VK/unicode decision with Microsoft doc constraints.
- [x] Implement `DispatchKeypress` path or prove a narrower runtime seam is
  needed.
- [x] Add public registration/schema only after runtime green.
- [x] Refresh generated docs and changelog.
- [x] Run targeted tests, smoke, review/re-review and commit.

**Acceptance criteria:**

- [x] `press_key` is published as implemented only after runtime proof.
- [x] Dangerous keys/combinations require confirmation.
- [x] Foreground/UIPI/factual dispatch failures do not return `done`.
- [x] Audit redacts raw sensitive state and records only safe action summary.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `pending`
- TDD применялся: `да` (первичный RED на dangerous combo policy, printable-key validator и missing keypress runtime path; затем follow-up RED на order-independent risk policy, bounded repeat, started-event redaction и public/runtime failure mapping после review findings)
- Проверки: RED `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~RiskPolicyRequiresConfirmationForDangerousPressKeyCombos|FullyQualifiedName~PressKeyValidatorRejectsPrintableKeyWithoutModifier"` -> expected fail `2/2`; RED `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ExecuteAsyncReturnsVerifyNeededForKeypressWhenPlatformDispatchSucceeds"` -> expected fail `1/1`; GREEN rerun этих filters -> `2/2` и `1/1`; review-fix RED `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~BeginInvocationRedactsComputerUseWinPressKeyRequestSummary|FullyQualifiedName~ExecuteAsyncRejectsKeypressRepeatBeyondBoundedMaximumBeforeDispatch|FullyQualifiedName~ExecuteAsyncReturnsFailureForKeypressWhenPlatformLosesForegroundProof|FullyQualifiedName~ExecuteAsyncReturnsFailureForCommittedKeypressDispatchFailure"` -> compile/contract fail before fix; review-fix RED `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~RiskPolicyRequiresConfirmationForDangerousPressKeyCombos|FullyQualifiedName~PressKeyValidatorRejectsRepeatBeyondBoundedMaximum|FullyQualifiedName~PressKeyHandlerReturnsStructuredFailureWhenRuntimeLosesForeground|FullyQualifiedName~PressKeyHandlerReturnsStructuredFailureWhenRuntimeDispatchFails|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> compile/schema fail before fix; review-fix GREEN rerun этих filters -> `4/4` и `5/5`; promotion/handler/tools-list subset `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~PressKeyHandlerRequiresConfirmationForDangerousCombo|FullyQualifiedName~PressKeyHandlerDispatchesNormalizedShortcutThroughInputService|FullyQualifiedName~ComputerUseWinProfilePublishesImplementedPressKeyAlongsideExistingOperatorTools|FullyQualifiedName~ComputerUseWinDeferredWaveKeepsFiveRemainingTargetActionsWithoutPublishingThem|FullyQualifiedName~ComputerUseWinToolsExposeOnlyCuratedOperatorEntryPoints|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> `6/6`; runtime/exporter/audit slice `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests|FullyQualifiedName~AuditLogTests|FullyQualifiedName~Win32InputServiceTests"` -> `96/96`; server slice `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~ComputerUseWinPressKeyMovesKeyboardFocusThroughApprovedAppState"` -> `103/103`; `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> green
- Review agents: `Hilbert` -> `approve`; `Tesla` -> `approve`
- Official docs checked: OpenAI Computer Use Guide; OpenAI MCP and Connectors Guide; MCP Tools spec `2025-11-25`; KEYBDINPUT; SendInput; Virtual-Key Codes; GetKeyboardLayout; VkKeyScanExW (last two used as rejected layout-sensitive shortcut path reference, not as shipped dispatch strategy)
- Reference repos checked: `not_used`; stage опирается на repo-local `windows.input` contract + official docs
- Подтверждённые замечания:
  - `press_key` confirmation policy нельзя строить на substring matching normalized key string: combo risk должен вычисляться по parsed modifier set + base key, иначе `shift+ctrl+w` обходил confirmation.
  - `press_key` started-audit path до follow-up фикса не имел explicit redaction class и мог писать raw `stateToken` / raw `key` в `tool.invocation.started`.
  - layout-sensitive `VkKeyScanExW(GetKeyboardLayout(...))` не подходит для shortcut semantics v1: public `press_key` должен dispatch-ить буквенно-цифровую combo-base как invariant virtual key, а не как text-layout translation.
  - public `repeat` без upper bound был слишком широким для prebuilt `INPUT[]` path.
  - failure evidence до follow-up фикса действительно покрывала success dispatch лучше, чем foreground/dispatch failure path.
- Отклонённые замечания:
- Исправленные root causes: `press_key` теперь имеет explicit named-key/modifier-combo contract вместо bare printable text; dangerous shortcuts подтверждаются parsed literal policy до activation и не зависят от порядка modifier tokens; `repeat` ограничен диапазоном `1..10` в public schema, request validator, input runtime validator и Win32 dispatch builder; `AuditToolContext` назначает `press_key` explicit target-metadata redaction class, поэтому started/completed audit не leak-ит raw `stateToken` и raw key literal; `Win32InputPlatform` dispatch-ит shortcut base `A-Z/0-9` как invariant virtual key вместо layout-sensitive text translation; `Win32InputService` / handler tests теперь отдельно доказывают structured foreground и dispatch failures; `ComputerUseWinPressKeyExecutionCoordinator` и `ComputerUseWinPressKeyHandler` используют existing shared action executor; public `tools/list` и generated exports повышены до implemented только после runtime/test/smoke proof
- Проверенные соседние paths: `ToolContractManifestTests`, `ToolContractExporterTests`, `Win32InputServiceTests`, `ComputerUseWinArchitectureTests`, `ComputerUseWinActionAndProjectionTests`, `McpProtocolSmokeTests`, `docs/generated/computer-use-win-interfaces.*`, `docs/architecture/computer-use-win-surface.md`, `docs/product/okno-roadmap.md`
- Остаточные риски: current `press_key` v1 intentionally limits grammar to named keys and modifier combos; no bare printable text path; non-Latin text/layout-dependent typing intentionally deferred to `type_text`; future `type_text` stage still needs separate semantic-vs-raw typing split and richer action-specific observability fields beyond keyboard-safe summary
- Разблокировка следующего этапа: закрыть review gate Stage 3, сделать отдельный commit, затем перейти к Stage 4 official-doc sweep и `set_value` RED

### Stage 4: Implement `set_value`

**Назначение:** semantic value setting before broad text typing.

**Primary files:**

- new/updated `ComputerUseWinSetValueContract.cs`
- new `ComputerUseWinSetValueHandler.cs`
- new `ComputerUseWinSetValueExecutionCoordinator.cs`
- new narrow UIA semantic action seam if required
- `ComputerUseWinAffordanceResolver.cs`
- `ComputerUseWinAccessibilityProjector.cs`
- UIA runtime/tests for `ValuePattern` / `RangeValuePattern`.

**Steps:**

- [x] TDD RED: settable element success, non-settable failure, stale element,
  missing state, blocked/approval inheritance, value validation.
- [x] Add settable affordance projection from UIA pattern availability.
- [x] Implement fresh element revalidation.
- [x] Implement semantic set path through a narrow UIA action owner.
- [x] Reject silent blind typing fallback.
- [x] Publish `set_value`, refresh generated docs/changelog.
- [x] Run targeted tests, smoke, review/re-review and commit.

**Acceptance criteria:**

- [x] Primary path uses semantic UIA settable proof.
- [x] Non-settable target returns `unsupported_action`, not fake success.
- [x] Revalidation mismatch returns `stale_state`.
- [x] `type_text` remains separate and lower-confidence.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `pending`
- TDD применялся: `да` (RED на manifest/exporter promotion, set_value validator/service-graph/action-handler paths и public text/number smoke; затем semantic UIA set path, helper mirror proof и generated sync)
- Проверки: RED `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests"` -> expected fail `2/31`; RED `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~SetValueValidatorRejectsMissingElementIndex|FullyQualifiedName~SetValueValidatorRejectsMismatchedValueKindPayload|FullyQualifiedName~ComputerUseWinProfilePublishesImplementedSetValueAlongsideShippedOperatorTools|FullyQualifiedName~SetValueHandlerReturnsUnsupportedActionForNonSettableStoredElement|FullyQualifiedName~SetValueHandlerReturnsStaleStateWhenFreshElementCannotBeMatched|FullyQualifiedName~SetValueHandlerAppliesTextValueViaSemanticService|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState"` -> compile/runtime fail before semantic service/public publication; GREEN `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests|FullyQualifiedName~AuditLogTests"` -> `51/51`; GREEN `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~SetValueValidatorRejectsMissingElementIndex|FullyQualifiedName~SetValueValidatorRejectsMismatchedValueKindPayload|FullyQualifiedName~ComputerUseWinProfilePublishesImplementedSetValueAlongsideShippedOperatorTools|FullyQualifiedName~SetValueHandlerReturnsUnsupportedActionForNonSettableStoredElement|FullyQualifiedName~SetValueHandlerReturnsStaleStateWhenFreshElementCannotBeMatched|FullyQualifiedName~SetValueHandlerAppliesTextValueViaSemanticService|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState|FullyQualifiedName~ComputerUseWinSetValueUpdatesRangeMirrorThroughApprovedAppState"` -> `9/9`; nearby server slice `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~ComputerUseWinPressKeyMovesKeyboardFocusThroughApprovedAppState|FullyQualifiedName~ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState|FullyQualifiedName~ComputerUseWinSetValueUpdatesRangeMirrorThroughApprovedAppState"` -> `110/110`; review-fix runtime slice `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~AuditLogTests"` -> `42/42`; review-fix contour `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~SetValueHandlerReturnsInvalidRequestWhenSemanticServiceRejectsValueFormat|FullyQualifiedName~ComputerUseWinSetValueUpdatesSemanticMirrorThroughApprovedAppState|FullyQualifiedName~ComputerUseWinSetValueUpdatesRangeMirrorThroughApprovedAppState"` -> `3/3`; `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> green (rerun after notes/redaction fixes)
- Review agents: `Chandrasekhar` -> `approve`; `Meitner` -> `approve`
- Official docs checked: OpenAI Computer Use Guide; OpenAI MCP docs; MCP tools spec `2025-11-25`; Microsoft `ValuePattern`; Microsoft `RangeValuePattern`; Microsoft UI Automation Value Control Pattern; Microsoft UI Automation RangeValue Control Pattern
- Reference repos checked: `not_used`; stage опирается на repo-local UIA/input seams + official docs/web inspection
- Подтверждённые замечания:
  - `ComputerUseWinContractNotes` после первой implementation wave всё ещё описывал `set_value` как deferred/non-published, хотя profile уже ship-ил action как implemented.
  - `set_value(number)` мог leak-ить raw `numberValue` в `tool.invocation.started`, потому что `TargetMetadata` redaction ранее redacted только string payload values.
  - `Win32UiAutomationSetValueService` не materialize-ил `ElementNotEnabledException` и `ArgumentException` как structured semantic failures, хотя official Microsoft docs рассматривают их как ожидаемые UIA outcome paths.
- Отклонённые замечания:
- Исправленные root causes: `set_value` больше не остаётся placeholder-only declared action: public profile и registration повышены до implemented только после semantic runtime proof; request contract стал discriminated по `valueKind=text|number`; semantic target revalidation теперь использует fresh UIA snapshot + shared element matcher вместо stored-bounds guess; actual set path идёт через narrow `IUiAutomationSetValueService` и `ValuePattern` / `RangeValuePattern` without blind typing fallback; numeric semantic path использует только тот official semantic pattern, который live control реально экспонирует и который честно round-trips requested numeric value; `ComputerUseWinContractNotes` синхронизирован с shipped subset; `AuditPayloadRedactor` теперь redacts `textValue` и `numberValue`, а `Win32UiAutomationSetValueService` materialize-ит `ElementNotEnabledException` / `ArgumentException` как structured semantic failure kinds вместо unexpected leak; public smoke fixture публикует text/range mirror state для factual post-action proof
- Проверенные соседние paths: `ToolContractManifestTests`, `ToolContractExporterTests`, `AuditLogTests`, `ComputerUseWinArchitectureTests`, `ComputerUseWinActionAndProjectionTests`, `McpProtocolSmokeTests`, `docs/generated/computer-use-win-interfaces.*`, `docs/architecture/computer-use-win-surface.md`, `docs/architecture/computer-use-win-next-actions.md`, `docs/product/okno-roadmap.md`
- Остаточные риски: numeric semantic path сейчас опирается на whichever semantic pattern реально экспонирует target control (`RangeValuePattern` preferred, `ValuePattern` accepted only when it faithfully round-trips requested numeric value); broad text entry, scroll and secondary actions всё ещё остаются отдельными unpublished stages
- Разблокировка следующего этапа: закрыть review gate Stage 4, сделать отдельный commit, затем перейти к Stage 5 official-doc sweep и `type_text` RED

### Stage 5: Implement `type_text`

**Назначение:** text input without clipboard-default or implicit focus guessing.

**Primary files:**

- new/updated `ComputerUseWinTypeTextContract.cs`
- new `ComputerUseWinTypeTextHandler.cs`
- new `ComputerUseWinTypeTextExecutionCoordinator.cs`
- `Win32InputService.cs`
- `Win32InputPlatform.cs`
- input runtime tests for text dispatch.

**Steps:**

- [x] TDD RED: whitespace text, missing editable target, element editable
  success, focused editable success if accepted, stale target, unsupported
  target, sensitive/risky confirmation if policy requires it.
- [x] Decide whether `elementIndex` is optional or required for v1.
- [x] Implement text dispatch without clipboard/paste default.
- [x] Keep `set_value` as preferred path for settable semantic controls.
- [x] Publish `type_text`, refresh generated docs/changelog.
- [x] Run targeted tests, smoke, review/re-review and commit.

**Acceptance criteria:**

- [x] No typing occurs without state-backed editable proof.
- [x] Clipboard is not used by default.
- [x] Result is `verify_needed` unless runtime can prove final text value.
- [x] Public docs explain `set_value` vs `type_text`.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `3c4a40a`
- TDD применялся: `да` (RED на manifest/exporter/audit promotion, runtime `type` dispatch path, validator/service-graph/handler paths, public tools-list/smoke/install surface; затем минимальная implementation и несколько review-driven fix/re-review loops до final GREEN)
- Проверки:
  - `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests|FullyQualifiedName~AuditLogTests|FullyQualifiedName~Win32InputServiceTests|FullyQualifiedName~InputResultMaterializerTests|FullyQualifiedName~InputBatchExecutionStateTests|FullyQualifiedName~UiaSnapshotTreeBuilderTests"` -> `124/124`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~McpProtocolSmokeTests"` -> `141/141`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints"` -> `1/1`
  - `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> green
- Review agents:
  - `Goodall` (`019dd263-38c1-7db0-8b74-5bf0e2f32138`) - `approve`
  - `Carver` (`019dd263-3a12-70d0-80c6-90171f6309d6`) - `approve`
- Official docs checked:
  - OpenAI Computer Use guide: `https://platform.openai.com/docs/guides/tools-computer-use`
  - Microsoft `SendInput`: `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput`
  - Microsoft `KEYBDINPUT`: `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput`
  - Microsoft surrogate/supplementary character guidance: `https://learn.microsoft.com/en-us/windows/win32/intl/surrogates-and-supplementary-characters`
- Reference repos checked: `none`
- Подтверждённые замечания:
  - `type_text` precheck и fresh path сначала были слишком слабыми: focused `edit`/`document` target проходил без требования опубликованного `type_text` affordance и без строгого writable proof.
  - keyboard/text committed side effects сначала терялись в `windows.input` evidence/cancellation model: partial text/keypress dispatch и cancellation после keyboard side effect выглядели как pointer-generic или даже как `no_committed_side_effect_observed`.
  - один doc drift: `docs/architecture/computer-use-win-next-actions.md` после promotion всё ещё не перечислял `type_text` в shipped subset.
- Отклонённые замечания:
- Исправленные root causes: focused-editable proof вынесен в explicit `type_text` contract/coordinator вместо optimistic raw typing; stored/fresh target теперь обязаны не просто быть focused `edit`, а уже нести опубликованный `type_text` affordance; `document` исключён из `type_text` v1 как insufficient writable proof; UIA snapshot/projection теперь протягивает `IsReadOnly` из `ValuePattern` / `RangeValuePattern`, а shared actionability layer fail-close-ит `set_value`/`type_text`, если writable proof не равен `false`; public affordance/public schema/registration/install surface больше не расходятся с shipped subset; Win32 text path больше не остаётся structural stub и использует `SendInput` unicode dispatch без clipboard-default; `windows.input` materializer и cancellation policy теперь различают committed text/keypress side effects через action-specific failure stages/evidence вместо pointer-only эвристик; started audit для `type_text` больше не leak-ит raw `stateToken` и raw text; helper smoke textbox делает deterministic select-all после focus/click, поэтому public smoke проверяет replacement outcome, а не случайное caret append behavior
- Проверенные соседние paths: `ToolContractManifestTests`, `ToolContractExporterTests`, `AuditLogTests`, `Win32InputServiceTests`, `ComputerUseWinArchitectureTests`, `ComputerUseWinActionAndProjectionTests`, `McpProtocolSmokeTests`, `ComputerUseWinInstallSurfaceTests`, `docs/generated/computer-use-win-interfaces.*`, `docs/architecture/computer-use-win-surface.md`, `docs/architecture/computer-use-win-next-actions.md`, `docs/product/okno-roadmap.md`
- Остаточные риски: `type_text` v1 сознательно ограничен focused writable `edit` targets с явным UIA writable proof и default `verify_needed`; raw text path не пытается скрыто переводить focus и не даёт semantic final-value proof, поэтому workflows без explicit focus proof по-прежнему должны идти через `click -> get_app_state -> type_text` или через `set_value`, если control поддерживает semantic set; partial keyboard/text dispatch теперь честно materialize-ится в evidence/cancellation trail, но richer non-pointer diagnostics vocabulary для будущих keyboard/scroll/drag paths всё ещё остаётся общим follow-up для следующих stages
- Разблокировка следующего этапа: закрыть Stage 5 commit/amend, затем перейти к Stage 6 official-doc sweep и `scroll` RED

### Stage 6: Implement `scroll`

**Назначение:** semantic-first scroll with explicit coordinate fallback.

**Primary files:**

- new/updated `ComputerUseWinScrollContract.cs`
- new `ComputerUseWinScrollTargetResolver.cs`
- new `ComputerUseWinScrollHandler.cs`
- new `ComputerUseWinScrollExecutionCoordinator.cs`
- UIA semantic scroll seam if required
- `InputCoordinateMapper.cs`
- `CaptureReferenceGeometryPolicy.cs`
- input scroll dispatch tests.

**Steps:**

- [x] TDD RED: element scroll success, unsupported element, point scroll path,
  missing capture proof, geometry drift, stale state, no movement/verify-needed.
- [x] Add scrollable affordance projection from UIA pattern availability.
- [x] Implement UIA `ScrollPattern` path first.
- [x] Implement wheel fallback only with fresh geometry proof.
- [x] Publish `scroll`, refresh generated docs/changelog.
- [x] Run targeted tests, smoke, review/re-review and commit.

**Acceptance criteria:**

- [x] Semantic scroll is preferred over wheel input.
- [x] Coordinate fallback is explicit and risk-aware.
- [x] `scroll` does not mutate selector/session ownership.
- [x] Result differentiates `done`, `verify_needed` and structured failure.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `03363a1`
- TDD применялся: `да` — сначала RED на promotion/runtime/schema/smoke для `scroll`, затем отдельный RED/re-fix цикл после review findings на point-shape validation, bounded `pages`, strict selector branches и documented `ArgumentException` handling.
- Проверки:
  - `RED` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ScrollValidatorRejectsPagesAboveMaximum|FullyQualifiedName~ToolRequestBinderRejectsNestedAdditionalPropertiesForComputerUseScrollPoint|FullyQualifiedName~ToolRequestBinderRejectsOutOfRangePagesForComputerUseScroll|FullyQualifiedName~ComputerUseWinScrollToolSchemaBoundsPagesAndRequiresNonNullSelectorBranches|FullyQualifiedName~ScrollHandlerRejectsMalformedPointBeforeActivation|FullyQualifiedName~ScrollHandlerReturnsStructuredFailureWhenSemanticScrollServiceThrowsArgumentException"` -> `6 failed`
  - `GREEN` — тот же targeted filter -> `6 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Runtime.Tests\WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests|FullyQualifiedName~Win32InputServiceTests"` -> `84 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests"` -> `128 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> `1 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinScrollUpdatesScrollMirrorThroughApprovedAppState"` -> `1 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints"` -> `1 passed` (после cleanup зависших repo-local процессов)
  - `GREEN` — `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`
- Review agents:
  - `Laplace` (`019dd2c7-a7e8-7f20-ab22-d249aaf03265`) — architecture/contract review, initial verdict `not approve`
  - `Hume` (`019dd2c7-a995-7b40-b5b9-46f15c107d55`) — tests/failure-path/docs/generated review, initial verdict `not approve`
  - `re-review`: `Laplace=approve`, `Hume=approve`
- Official docs checked:
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.scrollpattern?view=windowsdesktop-10.0`
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.scrollpattern.scroll?view=windowsdesktop-10.0`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput`
- Reference repos checked:
  - `references\Windows-MCP`
  - `references\Windows-MCP.Net`
  - `references\Peekaboo`
  - `references\pywinauto-mcp`
- Подтверждённые замечания:
  - point-target shape для `scroll` действительно не fail-close-ился до activation/runtime path
  - `scroll.pages` действительно не имел верхней границы и не был защищён от overflow/huge-loop inputs на product contract уровне
  - selector `oneOf` для public `scroll` schema действительно допускал nullable branch presence вместо non-null selector proof
  - documented `ScrollPattern.Scroll` `ArgumentException` действительно не materialize-ился как structured semantic failure
  - hand-written shipped-surface/docs/report anchors действительно отстали от promoted `scroll`
- Отклонённые замечания:
  - требование сохранить raw provider exception text в public `reason` — не принимается; public finalizer намеренно нормализует user-facing failure message, пока `failure_code` и lifecycle остаются truthful
- Исправленные root causes:
  - введён shared `ComputerUseWinPointContract` для point JSON shape validation/schema и переиспользования в `click`, `scroll`, `drag`
  - `scroll` contract теперь bounded по `pages` (`1..10`), валидирует point shape до activation и публикует strict non-null selector branches
  - semantic scroll seam materialize-ит documented `ArgumentException` как structured `dispatch_failed`/public `input_dispatch_failed`, а не как unexpected internal failure
  - shipped subset lists, roadmap status anchors и Stage 6 report синхронизированы с promoted `scroll`
- Проверенные соседние paths:
  - `click` point validation/schema после перевода на shared point contract
  - `drag` point validation через `ComputerUseWinRequestContractValidator`
  - `Win32InputService` scroll dispatch + verify-needed path
  - manifest/export/generated interfaces after promotion refresh
  - installed plugin publication/tools-list surface
- Остаточные риски:
  - install-surface acceptance может зависать при repo-local orphaned publish/test processes; runtime contract не страдает, но перед install proof нужен cleanup
  - `scroll` v1 сознательно ограничен page-like semantic `ScrollPattern` / wheel fallback и ещё не пытается доказывать richer per-app scroll semantics beyond fresh reobserve
- Разблокировка следующего этапа: дождаться re-review approval, зафиксировать отдельный Stage 6 commit, затем перейти к Stage 7 `perform_secondary_action`
- Разблокировка следующего этапа: `03363a1` зафиксировал shipped `scroll`; можно переходить к Stage 7 `perform_secondary_action`

### Stage 7: Implement `perform_secondary_action`

**Назначение:** product-owned secondary intent, not a right-click alias.

**Primary files:**

- new `ComputerUseWinPerformSecondaryActionContract.cs`
- new `ComputerUseWinPerformSecondaryActionHandler.cs`
- new `ComputerUseWinSecondaryActionResolver.cs`
- new `ComputerUseWinSecondaryActionExecutionCoordinator.cs`
- `ComputerUseWinAffordanceResolver.cs`
- `ComputerUseWinAccessibilityProjector.cs`
- UIA semantic action seam if required.

**Steps:**

- [x] TDD RED: semantic secondary action success, unsupported semantic target,
  stale element, right-click/context fallback decision, confirmation,
  reobserve-needed result.
- [x] Define secondary affordance mapping from UIA patterns and projected tree.
- [x] Implement semantic path.
- [x] If context-menu fallback is accepted, make it explicit, confirmed and
  `verify_needed`.
- [x] Publish `perform_secondary_action`, refresh generated docs/changelog.
- [x] Run targeted tests, smoke, review/re-review and commit.

**Acceptance criteria:**

- [x] Public docs do not describe tool as simple right-click.
- [x] No fallback runs without fresh element proof.
- [x] Context-menu path, if present, forces reobserve guidance.
- [x] Unsupported targets return `unsupported_action`.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `bcadcc0`
- TDD применялся: `да` — сначала RED на manifest/exporter promotion, затем compile/runtime RED на missing secondary-action seam, затем green implementation; context-menu fallback v1 сознательно отклонён и зафиксирован как semantic-only decision.
- Проверки:
  - `RED` — `dotnet test tests\WinBridge.Runtime.Tests\WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests"` -> `3 failed`
  - `RED` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~PerformSecondaryAction|FullyQualifiedName~ComputerUseWinProfilePublishesImplementedSecondaryAction|FullyQualifiedName~ComputerUseWinDeferredWaveKeepsOnlyDragWithoutPublishingIt|FullyQualifiedName~ComputerUseWinSecondaryActionToolSchemaRequiresStateTokenAndElementIndex|FullyQualifiedName~ComputerUseWinPerformSecondaryActionTogglesCheckboxStateThroughApprovedAppState"` -> сначала compile errors на отсутствующий secondary-action seam, затем `6 failed` после skeleton wiring
  - `RED` — review-driven regression filter на `fresh unsupported`, `expand_collapse` affordance gating и secondary observability markers -> compile error до добавления typed markers
  - `GREEN` — `dotnet test tests\WinBridge.Runtime.Tests\WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests"` -> `32 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~ComputerUseWinFinalizationTests"` -> `163 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> `1 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinPerformSecondaryActionTogglesCheckboxStateThroughApprovedAppState"` -> `1 passed`
  - `GREEN` — `dotnet test tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints"` -> `1 passed` (после cleanup зависших repo-local процессов)
  - `GREEN` — `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`
- Review agents:
  - `Kuhn` — architecture/contract review, initial verdict `findings`, final verdict `approve`
  - `Helmholtz` — tests/failure-path/docs/generated review, initial verdict `findings`, final verdict `approve`
  - `re-review`: closed
- Official docs checked:
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.togglepattern?view=windowsdesktop-10.0`
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.togglepattern.toggle?view=windowsdesktop-10.0`
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.expandcollapsepattern?view=windowsdesktop-9.0`
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.expandcollapsepattern.expand?view=windowsdesktop-10.0`
  - `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.expandcollapsestate?view=windowsdesktop-10.0`
- Reference repos checked:
  - none
- Подтверждённые замечания:
  - `fresh unsupported target` действительно не должен маскироваться под `stale_state`; same-identity target без live secondary affordance должен возвращать `unsupported_action`
  - `expand_collapse` не должен публиковаться как strong v1 affordance без projected state proof; pattern availability alone недостаточен для leaf/partial semantics
  - Stage 7 observability действительно требовала typed safe markers для secondary semantic path (`semantic_action_kind`, `fallback_used`, `context_menu_path_used`)
- Отклонённые замечания:
  - v1 context-menu/right-click fallback intentionally rejected; tool ships semantic-only because current evidence covers strong UIA secondary patterns, но не доказывает safe generic context-menu path
- Исправленные root causes:
  - secondary semantic path больше не остаётся declaration-only: введён dedicated UIA service seam и owner-layer coordinator/handler для `perform_secondary_action`
  - public affordance mapping теперь поднимает action только для strong secondary UIA proof `toggle`; `expand_collapse` не публикуется в v1 без projected state evidence
  - same-identity fresh target без secondary proof теперь честно materialize-ится как `unsupported_action`, а не `stale_state`
  - secondary action observability теперь пишет typed safe markers `semantic_action_kind`, `fallback_used=false`, `context_menu_path_used=false`
  - helper fixture теперь публикует checkbox semantic state через dynamic `AccessibleName`, что даёт factual post-action proof на public tree без внутренних pattern leaks
  - manifest/registration/generated/install surface синхронизированы с promotion `perform_secondary_action`; deferred wave сузилась до одного `drag`
- Проверенные соседние paths:
  - risky semantic confirmation policy на element targets
  - unsupported/stale semantic target outcomes
  - tools/list schema and installed plugin surface
  - existing click/set_value/type_text/scroll flows после helper checkbox update
- Остаточные риски:
  - `perform_secondary_action` v1 intentionally не поддерживает context-menu/right-click fallback и любые secondary semantics вне strong `toggle` proof; `expand_collapse` остаётся отдельным future/internal candidate до projected state evidence
  - install-surface acceptance по-прежнему может зависать при repo-local orphaned publish/test processes; нужен cleanup перед rerun
- Разблокировка следующего этапа: дождаться review/re-review approval и Stage 7 commit, затем перейти к Stage 8 `drag`
- Разблокировка следующего этапа: `bcadcc0` зафиксировал shipped `perform_secondary_action`; можно переходить к Stage 8 `drag`

### Stage 8: Drag decision and implementation or deferral

**Назначение:** make an evidence-based decision for the highest-risk action.

**Primary files if implemented:**

- new/updated `ComputerUseWinDragContract.cs`
- new `ComputerUseWinDragTargetResolver.cs`
- new `ComputerUseWinDragHandler.cs`
- new `ComputerUseWinDragExecutionCoordinator.cs`
- `Win32InputService.cs`
- `Win32InputPlatform.cs`
- `InputCoordinateMapper.cs`
- capture geometry policy tests.

**Decision gate:**

- If low-level drag dispatch, geometry proof and smoke harness are strong enough,
  implement `drag`.
- If not, keep `drag` deferred and create a smaller active ExecPlan for input
  runtime hardening prerequisites.

**Steps if implemented:**

- [ ] TDD RED: element-to-element drag, point-to-point drag, stale source,
  stale target, capture geometry drift, confirmation, verify-needed.
- [ ] Implement endpoint validation with exactly one source mode and one
  destination mode.
- [ ] Implement input drag dispatch with factual failure mapping.
- [ ] Publish `drag`, refresh generated docs/changelog.
- [ ] Run targeted tests, smoke, review/re-review and commit.

**Steps if deferred:**

- [x] Record evidence for deferral.
- [x] Keep manifest/generated docs honest: deferred, not implemented.
- [x] Create bounded prerequisite ExecPlan.
- [x] Review/re-review and commit decision.

**Acceptance criteria:**

- [x] No public `drag` implementation without full dispatch proof.
- [x] Coordinate drag always requires explicit confirmation.
- [x] Result always recommends fresh `get_app_state`.
- [x] Deferral, if chosen, is evidence-based and scoped.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `50bde3e`
- TDD применялся: `нет` — Stage 8 является decision-only deferral gate; runtime behavior не расширялся, вместо этого собран evidence pack и создан bounded prerequisite ExecPlan.
- Проверки:
  - evidence review: [Win32InputPlatform](../../../src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs) всё ещё materialize-ит `DispatchDrag(...)` как explicit unsupported stub
  - evidence review: [Win32InputService](../../../src/WinBridge.Runtime.Windows.Input/Win32InputService.cs) не содержит shipped drag execution branch с factual move/down/up lifecycle
  - evidence review: existing fake/runtime tests around drag остаются stub-only и не дают public dispatch proof
  - supporting green baseline from Stage 7: `ToolContractManifestTests` / `ToolContractExporterTests` / `ComputerUseWinArchitectureTests` / `McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools` / `ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints` продолжают доказывать, что public profile честно оставляет только `drag` в deferred set
- Review agents:
  - `Hypatia` — architecture/contract review, initial verdict `P2`, final verdict `approve`
  - `Bernoulli` — tests/failure-path/docs/generated review, initial verdict `P2`, final verdict `approve`
- Official docs checked:
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput`
  - `https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-support-for-drag-and-drop`
  - `https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingdrag`
- Reference repos checked:
  - none
- Подтверждённые замечания:
  - `drag` всё ещё не имеет shipped low-level dispatch proof в current Win32 input runtime
  - current helper/smoke harness не даёт deterministic public drag story и post-drag proof
  - official Microsoft drag/drop semantic model существенно богаче current shipped projection/runtime
  - bounded prerequisite plan initially не закреплял invariant `refreshStateRecommended=true` для любого future post-dispatch drag path; это добавлено в W2 + Acceptance
- Отклонённые замечания:
  - идея поднять `drag` только на основе existing structural DTO/input touchpoints отклонена как недостаточная; это был бы optimistic public publish без runtime/helper proof
- Исправленные root causes:
  - вместо слабой promotion создан bounded prerequisite ExecPlan `docs/exec-plans/completed/completed-2026-04-28-computer-use-win-drag-prerequisites.md`, который выделил drag runtime dispatch, endpoint proof и helper smoke как отдельные prerequisites
  - main wave docs/roadmap/changelog синхронизированы так, чтобы `drag` оставался единственным deferred public action без ложных implemented claims
- Проверенные соседние paths:
  - current public profile после Stage 7 (`perform_secondary_action` shipped, `drag` deferred only)
  - generated interface exports and roadmap wording around remaining wave
  - existing `ComputerUseWinRequestContractValidator` point validation reuse from Stage 6 остаётся available для будущего drag plan
- Остаточные риски:
  - новый prerequisite ExecPlan остаётся active until a future branch closes runtime/helper proof; до этого main wave нельзя считать full drag-capable
  - install/publication harness по-прежнему может подвисать при orphaned repo-local processes, поэтому even deferral-only review loop держит cleanup как operational note
- Разблокировка следующего этапа: `50bde3e` зафиксировал evidence-based drag deferral; Stage 9 final closure откладывается до закрытия нового Stage 10 `drag`

### Stage 9: Pre-closure checkpoint before `drag`

**Назначение:** зафиксировать partial closure baseline и сохранить финальный
closure заблокированным, пока `drag` не перейдёт из deferred в implemented.

**Steps:**

- [x] Re-read current active prerequisite plan
  `docs/exec-plans/completed/completed-2026-04-28-computer-use-win-drag-prerequisites.md` and sync this
  wave plan with it.
- [x] Preserve any Stage 8/Stage 9 partial verification fixes that remain
  needed for the future full contour (for example smoke/test expectation drift)
  without claiming final wave closure.
- [x] Keep `drag` absent from callable `computer-use-win` public profile until
  Stage 10 promotion proof is complete.
- [x] Leave final closure tasks blocked and point them to Stage 10 as the
  release gate.

**Acceptance criteria:**

- [x] Main wave plan no longer claims Stage 9 as final closure before `drag`.
- [x] Stage 10 exists as explicit implementation stage tied to the prerequisite
  plan.
- [x] Final wave closure remains blocked until `drag` has contract/runtime/test/
  docs/smoke/install proof.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `245d38e`
- TDD применялся: `нет` — checkpoint stage only; behavior changes move to Stage 10
- Проверки:
  - `git status --short --branch`
  - `git log --oneline -10`
  - `git diff main...HEAD --stat`
  - reread main wave plan + drag prerequisite plan + supporting architecture/product docs
  - `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~InputContractAndPolicyTests|FullyQualifiedName~WindowsInputSourceOfTruthTests"` -> `GREEN (77/77)`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinPerformSecondaryActionTogglesCheckboxStateThroughApprovedAppState|FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinGetAppStatePublishesSemanticSmokeSubtree"` -> `GREEN (2/2 matched tests)`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~McpProtocolSmokeTests.InitializeAndValidateCoreOknoToolsThroughStdio"` -> `GREEN (1/1)`
  - `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1` -> `GREEN`
- Review agents:
  - `Parfit` — architecture/contract review, verdict `approve`
  - `Newton` — tests/failure-path/docs/generated review, verdict `approve`
- Official docs checked:
  - `https://developers.openai.com/api/docs/guides/tools-computer-use`
  - `https://developers.openai.com/api/docs/guides/tools-connectors-mcp`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput`
  - `https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-support-for-drag-and-drop`
  - `https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingdrag`
- Reference repos checked:
  - none
- Подтверждённые замечания:
  - previous closure wording incorrectly implied final wave closure could proceed before `drag`; plan was updated so Stage 9 is checkpoint-only and Stage 10 is explicit promotion stage
  - smoke/test drift needed explicit alignment with the helper checkbox accessible name `Remember semantic selection: on`
  - runtime source-of-truth test needed to point at the completed superseded OpenAI interop plan instead of a no-longer-active path
  - input policy expectation needed to include already-shipped public failure literals `unsupported_key` and `unsupported_keyboard_layout`
- Отклонённые замечания:
  - none from review; both agents approved the checkpoint package
- Исправленные root causes:
  - main wave plan no longer presents Stage 9 as final closure before `drag`; final closure is now blocked behind explicit Stage 10 implementation
  - smoke harness expectation drift was corrected in both PowerShell smoke script and MCP integration tests
  - source-of-truth doc path drift in runtime tests was corrected to the completed OpenAI interop superseded plan
  - public input failure literal expectations were updated to match the current runtime contract
- Проверенные соседние paths:
  - deferred-only public profile for `drag`
  - `perform_secondary_action` helper subtree expectations in smoke and integration tests
  - stage ordering and final closure gating in the main ExecPlan
- Остаточные риски:
  - final wave closure remains blocked until Stage 10 promotion proof finishes
- Разблокировка следующего этапа: stage 9 checkpoint is approved; create separate checkpoint commit, then begin Stage 10 `drag`

### Stage 10: Implement `drag` from prerequisite plan

**Назначение:** реализовать `drag` как полноценное public action в
`computer-use-win` на основе historical prerequisite plan
`docs/exec-plans/completed/completed-2026-04-28-computer-use-win-drag-prerequisites.md`.

**Primary files:**

- `src/WinBridge.Server/ComputerUse/ComputerUseWinDragContract.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinDragTargetResolver.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinDragExecutionCoordinator.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinDragHandler.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinRegisteredTools.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `src/WinBridge.Runtime.Windows.Input/IInputService.cs`
- `src/WinBridge.Runtime.Windows.Input/IInputPlatform.cs`
- `src/WinBridge.Runtime.Windows.Input/Win32InputService.cs`
- `src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs`
- `src/WinBridge.Runtime.Windows.Input/InputCoordinateMapper.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`
- `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs`
- `tests/WinBridge.Runtime.Tests/*`
- `tests/WinBridge.Server.IntegrationTests/*`
- `tests/WinBridge.SmokeWindowHost/Program.cs`
- docs listed in the user request and prerequisite plan

**Contract rules:**

- `drag` requires `stateToken`, one source selector and one destination
  selector.
- Exactly one source mode and exactly one destination mode are allowed.
- Supported modes per endpoint:
  - `elementIndex`
  - `point` + capture reference / coordinate proof, following existing point
    conventions
- `windowId` must not bypass observed-state proof.
- Coordinate drag always requires explicit confirmation.
- Source and destination must be revalidated separately.
- Malformed point payloads must fail-close before activation/runtime dispatch.
- Every post-dispatch path must recommend fresh `get_app_state`.

**Implementation steps:**

- [x] RED: contract/schema tests for missing `stateToken`, missing source,
  missing destination, multiple source modes, multiple destination modes,
  malformed point, confirmation-required coordinate drag, pre-promotion
  `tools/list` absence and post-promotion presence.
- [x] Implement contract/DTO/validator support with strict source/destination
  shapes aligned to repo conventions.
- [x] RED: runtime tests for low-level drag dispatch, partial dispatch,
  foreground/preflight failure, platform failure, cancellation before side
  effect, cancellation after side effect, no optimistic `done`.
- [x] Implement factual drag dispatch in `Win32InputPlatform` /
  `Win32InputService` with move -> down -> move/path -> up lifecycle and honest
  failure taxonomy.
- [x] RED: public handler/state tests for missing token, stale state, blocked
  state, missing approval, stale source element, stale destination element,
  missing capture proof, capture geometry drift, unsupported endpoints,
  `refreshStateRecommended`.
- [x] Implement `ComputerUseWinDragContract`,
  `ComputerUseWinDragTargetResolver`,
  `ComputerUseWinDragExecutionCoordinator` and
  `ComputerUseWinDragHandler` by reusing shared lifecycle, continuity proof,
  capture geometry policy and point contract from earlier stages.
- [x] Extend action observability with safe `drag` fields:
  `source_mode`, `destination_mode`, `path_point_count_bucket`,
  `coordinate_fallback_used`, `refresh_state_recommended`; verify no raw token,
  raw points, raw exception message or capture bytes leak.
- [x] Extend `WinBridge.SmokeWindowHost` minimally if needed to provide one
  deterministic `get_app_state -> drag -> get_app_state` proof.
- [x] Promote `drag` in manifest/registration/tools list only after runtime,
  handler, smoke and install proof are green.
- [x] Refresh generated docs only through
  `scripts/refresh-generated-docs.ps1`.

**Acceptance criteria:**

- [x] `drag` stays deferred until contract/schema, runtime, public handler,
  smoke and install proof are all green.
- [x] Contract requires separate source/destination proof and exact selector
  modes.
- [x] Coordinate drag always requires explicit confirmation.
- [x] Default generic success is `verify_needed` unless strong post-action proof
  exists.
- [x] Public result always recommends fresh `get_app_state`.
- [x] `McpProtocolSmokeTests`, helper smoke and install-surface proof are green
  after promotion.
- [x] Docs/generated surface/changelog stay in sync with promotion.
- [x] Review/re-review approval from two `gpt-5.5` agents is recorded before
  commit.

#### Отчёт этапа

- Статус этапа: `approved`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `pending`
- TDD применялся: `да` — contract/publication RED -> GREEN, runtime RED -> GREEN, public handler/smoke/install RED -> GREEN; follow-up RED -> GREEN after review findings on drag failure taxonomy and low-level observability leak
- Проверки:
  - RED `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~Win32InputServiceTests.ExecuteAsyncFallsBackToUncompensatedDragEvidenceWhenCommittedFailureHintIsMissing|FullyQualifiedName~InputResultMaterializerTests.MaterializeRedactsDragCoordinatesAndSuppressesRuntimeArtifactLink"` -> expected fail `2/2`
  - GREEN rerun of the same RED filter -> `GREEN (2/2)`
  - `dotnet test tests/WinBridge.Runtime.Tests/WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~AuditLogTests|FullyQualifiedName~InputResultMaterializerTests|FullyQualifiedName~ToolContractManifestTests|FullyQualifiedName~ToolContractExporterTests|FullyQualifiedName~Win32InputServiceTests"` -> `GREEN (121/121)`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~ComputerUseWinFinalizationTests|FullyQualifiedName~McpProtocolSmokeTests"` -> `GREEN (200/200)`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints|FullyQualifiedName~ComputerUseWinInstallSurfaceTests.PublishComputerUseWinPluginKeepsCanonicalRuntimeRunnableWhenRepairHandoffFails|FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFailsFastWhenPluginLocalRuntimeBundleIsIncomplete"` -> `GREEN (3/3)`; first rerun hit command timeout only, second rerun with larger timeout passed unchanged
  - `powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1` -> `GREEN`
  - `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1` -> `GREEN`
- Review agents:
  - `Bohr` (`architecture/contract`) -> `approve_with_findings -> approve`
  - `Turing` (`tests/failure/docs/generated`) -> `approve_with_findings -> approve`
- Official docs checked:
  - `https://developers.openai.com/api/docs/guides/tools-computer-use`
  - `https://developers.openai.com/api/docs/guides/tools-connectors-mcp`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput`
  - `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput`
  - `https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-support-for-drag-and-drop`
  - `https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingdrag`
- Reference repos checked:
  - none
- Подтверждённые замечания:
  - element-to-element drag initially serialized `captureReference=null` into internal input request and therefore violated the frozen `windows.input` contract
  - drag helper initially depended on control-level move/up events that synthetic input did not reliably materialize; deterministic smoke required polling the real cursor/button state instead
  - install-surface rerun initially hit repo-local orphaned `Okno.Server` / launcher processes from older test-bundle runs; cleanup of repo-owned stale processes was required before the publish/install proof could complete
  - generic `invalid_request` public reason still said `click contract`; this was a class defect for all action tools and was generalized to `public action contract`
  - final low-level `button_up` drag failure initially fell through to a generic committed bucket; post-down release failure now materializes `drag_dispatch_partial_uncompensated`
  - if a platform-reported drag failure had committed side effects but omitted `FailureStageHint`, `Win32InputService` initially downgraded it to `drag_dispatch_committed_failure`; fallback now preserves uncompensated drag evidence
  - low-level `input.runtime.completed` still exposed `artifact_path` for sensitive drag artifacts and those artifacts still contained raw requested/resolved coordinates plus coordinate-bearing reasons; drag input artifacts/events now redact/suppress those fields
- Отклонённые замечания:
  - none
- Исправленные root causes:
  - added dedicated `ComputerUseWinDragContract`, endpoint resolver, coordinator, handler, registration and tool host path instead of trying to alias `drag` through existing click/scroll contracts
  - extended `InputCoordinateMapper` with drag-path mapping/validation and `Win32InputService` with a dedicated drag branch plus committed-side-effect taxonomy
  - replaced Win32 drag platform stub with factual `move -> button_down -> move/path -> button_up` dispatch and in-drag foreground-only boundary revalidation
  - extended product observability/artifacts with safe drag markers and explicit no-raw-points proof
  - added low-level drag observability policy so sensitive drag input artifacts redact coordinate-bearing result details and `input.runtime.completed` does not link those artifacts back into the audit graph
  - hardened drag failure-stage materialization so final release failure and missing-hint committed failures both preserve uncompensated drag evidence instead of falling back to a generic committed bucket
  - extended helper fixture with deterministic drag source/target/mirror surface and polling-based drag materialization for synthetic input
  - synchronized manifest/profile/install/generated/docs so `drag` is promoted only after runtime/helper/install proof
- Проверенные соседние paths:
  - current `computer-use-win` tools/list schema and public callable surface
  - runtime input committed-side-effect evidence for drag failure stages
  - `input.runtime.completed` drag audit path and low-level drag input artifact redaction
  - secondary-action and scroll smoke expectations after helper expansion
  - plugin-local publish/install path after drag promotion
- Остаточные риски:
  - generic drag v1 still defaults to `verify_needed`; no strong semantic postcondition proof is claimed outside dedicated helper story
  - install/publish proof can still be delayed by repo-local orphaned bundle processes; repo-owned stale-bundle cleanup now reduces this to an operational rerun concern rather than a contract/runtime gap
- Разблокировка следующего этапа: create separate Stage 10 commit, then proceed to Stage 11 full closure with the full sequential verification contour and full-branch review

### Stage 11: Full wave closure

**Назначение:** product-ready closure after `drag` promotion is complete.

**Steps:**

- [x] Move or copy completed plan to `docs/exec-plans/completed/` with date and
  final status.
- [x] Sync roadmap/spec/architecture docs.
- [x] Refresh generated docs through project-native script.
- [x] Run full sequential contour:
  `scripts/build.ps1` -> `scripts/test.ps1` -> `scripts/smoke.ps1` ->
  `scripts/refresh-generated-docs.ps1` -> `scripts/codex/verify.ps1`.
- [x] Run full-branch review against `main` with architecture/contract and
  tests/failure/docs/generated subagents.
- [x] Prepare closure report in the format at the end of this plan.

**Acceptance criteria:**

- [x] Shipped vs deferred actions are listed explicitly.
- [x] Final public profile includes `drag`:
  `list_apps`, `get_app_state`, `click`, `press_key`, `set_value`,
  `type_text`, `scroll`, `perform_secondary_action`, `drag`.
- [x] Public profile matches generated docs and smoke.
- [x] Install/publication proof covers cache-installed copy.
- [x] Residual risks and next work item are explicit.

#### Отчёт этапа

- Статус этапа: `completed`
- Branch: `codex/computer-use-win-next-actions-design`
- Commit SHA: `pending`
- TDD применялся: `да` для closure-only regression during full verification: failing `McpProtocolSmokeTests.ComputerUseWinGetAppStatePublishesObservedKeyboardFocus` reproduced in isolation, then helper focus activation was tightened and the same smoke returned GREEN before rerunning the full contour
- Проверки:
  - `scripts/build.ps1` -> `GREEN`
  - `scripts/test.ps1` -> first run failed in `ComputerUseWinInstallSurfaceTests.PublishComputerUseWinPluginPreservesBackupWhenRestoreFails`, isolated rerun -> `GREEN`; second full run failed in `McpProtocolSmokeTests.ComputerUseWinGetAppStatePublishesObservedKeyboardFocus`; isolated repro -> `RED`; helper focus fix -> isolated rerun `GREEN`; final `scripts/test.ps1` rerun -> `GREEN (Runtime 667/667, Integration 348/348)`
  - `scripts/smoke.ps1` -> `GREEN`
  - `scripts/refresh-generated-docs.ps1` -> `GREEN`
- `scripts/codex/verify.ps1` -> `GREEN`
- Review agents:
  - `Bohr` (`architecture/contract`) -> `approve_with_findings -> approve`
  - `Turing` (`tests/failure/docs/generated`) -> `changes_requested -> approve`
- Official docs checked:
  - no new external contract docs were needed beyond the Stage 10 baseline; closure rerun re-used the same official OpenAI Computer Use / MCP and Microsoft SendInput / MOUSEINPUT / UIA drag references because public/runtime semantics did not change after promotion
- Reference repos checked:
  - none
- Подтверждённые замечания:
  - full verification exposed one deterministic helper-fixture regression: initial `Shown` path set `ActiveControl` but did not force activation, so `get_app_state` could observe a live window with no `hasKeyboardFocus=true` element
  - completed-plan archival initially left the main wave record claiming `active execution plan` status, kept Stage 11 in `ready_for_review`, and preserved broken post-move `../../src/...` links; closure docs were normalized to a canonical completed record
- Отклонённые замечания:
  - parallel user work at the PC was ruled out as root cause for the final failing smoke; the reproducible failing path was isolated to helper focus activation semantics
- Исправленные root causes:
  - `WinBridge.SmokeWindowHost` now calls `PrepareCanonicalFocusTarget()` from the `Shown` path, so the canonical focus target is both activated and focused before public `get_app_state` focus proof
  - archived ExecPlan artifacts now live only in `docs/exec-plans/completed/`, carry explicit completion metadata, use completed-path references in changelog/architecture docs and resolve their internal code links from the deeper completed directory
- Проверенные соседние paths:
  - isolated rerun of `PublishComputerUseWinPluginPreservesBackupWhenRestoreFails`
  - isolated RED/GREEN rerun of `ComputerUseWinGetAppStatePublishesObservedKeyboardFocus`
  - final full `scripts/test.ps1`
  - final `scripts/smoke.ps1`
  - final `scripts/codex/verify.ps1`
- Остаточные риски:
  - generic `drag` v1 still defaults to `verify_needed`; no stronger semantic done-claim is made outside dedicated helper proof
  - install/publication reruns still depend on repo-owned stale-bundle cleanup when old local bundle processes are left alive
- Разблокировка следующего этапа: closure commit and final shipped-wave report

## 10. Observability contract

Эта wave не должна позволять каждому action самому изобретать observability.
До promotion первого action Stage 2 должен зафиксировать общий product-level
observability owner.

### Event family

Default decision:

- использовать одну общую product event family
  `computer_use_win.action.completed`;
- различать actions полем `action_name`, а не создавать шесть разных event
  names;
- продолжать использовать `tool.invocation.completed` как server/tool boundary
  audit trail;
- продолжать использовать `input.runtime.completed`, если action реально дошёл
  до low-level `windows.input` runtime path;
- не emit-ить `computer_use_win.action.completed` для pre-contract binder
  failures или validation-only rejects до action lifecycle owner.

Action-specific event family допустима только если stage докажет, что общий
event не может выразить factual result без unsafe payload. Такое решение должно
быть зафиксировано в stage report, `docs/architecture/observability.md` и
tests.

### Artifact family

Default decision:

- добавить одну product artifact family:
  `artifacts/diagnostics/<run_id>/computer-use-win/action-*.json`;
- artifact пишется для promoted public action после входа в action lifecycle
  owner;
- artifact может ссылаться на child artifacts вроде
  `artifacts/diagnostics/<run_id>/input/input-*.json`, но не заменяет их и не
  дублирует raw payload;
- pre-gate validation-only failures остаются на `tool.invocation.completed`
  trail и не обязаны materialize-ить action artifact.

### Common safe fields

Common event/artifact fields:

- `action_name`
- `status`
- `public_result`
- `failure_code`
- `runtime_state`
- `lifecycle_phase`
- `app_id`
- `window_id_present`
- `execution_target_id_present`
- `state_token_present`
- `target_mode`
- `element_index_present`
- `coordinate_space`
- `capture_reference_present`
- `confirmation_required`
- `confirmed`
- `risk_class`
- `dispatch_path`
- `refresh_state_recommended`
- `verify_status`
- `artifact_path`
- `child_artifact_paths`
- `failure_stage`
- `exception_type`

Never write:

- raw `stateToken`;
- raw typed text;
- raw value set by `set_value`;
- raw key or key combination;
- raw UIA text/value from target controls unless already intentionally public
  in tool response;
- raw exception message;
- raw full screenshot/capture bytes inside JSON artifact.

Per-action safe extensions:

- `press_key`: `key_category`, `repeat_count`, `dangerous_combo`,
  `layout_resolution_status`; no raw key literal.
- `set_value`: `value_kind`, `value_length`, `value_bucket`,
  `semantic_pattern`; no raw value.
- `type_text`: `text_length`, `text_bucket`, `contains_newline`,
  `whitespace_only`; no raw text/hash by default.
- `scroll`: `scroll_direction`, `scroll_amount_bucket`, `scroll_unit`,
  `semantic_scroll_supported`, `fallback_used`.
- `perform_secondary_action`: `semantic_action_kind`, `fallback_used`,
  `context_menu_path_used`.
- `drag`: `source_mode`, `destination_mode`, `path_point_count_bucket`,
  `coordinate_fallback_used`.

### Failure behavior

- Artifact write and event write are best-effort diagnostics. They must not
  downcast factual action result.
- If artifact write fails after action result is known, public result remains
  factual and diagnostics carry `failure_stage=artifact_write` where possible.
- If event sink fails, source of truth is public payload plus action artifact
  and child runtime artifact, if present.
- Observability failures must be tested at least once in Stage 2 before the
  first action promotion.

### Docs/tests impact

When this contract is implemented, update:

- `docs/architecture/observability.md`;
- `ToolContractExporter.cs` / generated project interfaces if artifact
  inventory changes;
- runtime diagnostics tests for event/artifact redaction;
- per-action tests for safe fields and absence of raw text/value/key/token.

## 11. Test ladder

Per action:

1. Contract unit/integration tests for request validation and schema.
2. State tests: missing token, expired/stale token, blocked target, approval.
3. Revalidation tests: same element, stale element, wrong target, missing
   capture/UIA proof.
4. Risk tests: confirmation required for dangerous keys, coordinate fallback,
   context fallback, drag.
5. Runtime dispatch tests in `WinBridge.Runtime.Tests` for the low-level slice.
6. Public profile publication tests in `McpProtocolSmokeTests`.
7. Install-surface tests if generated/public plugin surface changes.

Required RED examples:

- Stage 1: missing declared names for `set_value` and
  `perform_secondary_action`.
- Stage 3: `press_key` not published and/or dispatch unsupported.
- Stage 4: non-settable element must not silently type.
- Stage 5: clipboard/paste default must not exist.
- Stage 6: wheel fallback without fresh geometry proof must fail.
- Stage 7: `perform_secondary_action` must not be a right-click alias.
- Stage 8: drag must not publish without source and target revalidation.

## 12. Smoke strategy

Smoke coverage must stay profile-aware:

- `--tool-surface-profile computer-use-win` must materialize exactly the
  implemented public subset for the current stage.
- `tools/list` must show schemas/annotations for promoted actions only.
- Deferred actions must either be absent from callable public profile or have
  an explicitly tested deferred/unsupported path if the project chooses to
  expose them. Default for this wave: absent until promoted.
- Existing `list_apps -> get_app_state -> click -> get_app_state` smoke must
  remain green after every promotion.
- Reuse `tests/WinBridge.SmokeWindowHost/Program.cs` as the canonical fixture
  by default. Do not introduce a new helper app unless Stage 0/1 proves the
  existing helper cannot expose the required deterministic UIA/input scenario.
- If the helper is insufficient, extend `WinBridge.SmokeWindowHost` in the same
  stage as the first action that needs it, using deterministic controls and
  explicit helper commands; do not test against arbitrary third-party apps.
- Beyond raw dispatch proof, each promoted action should have one compact
  scenario-level proof in a live runtime, so the wave is validated by factual
  post-state rather than transcript-only success.
- Full install proof is required when plugin/manifest/generated public surface
  changes: cache-installed copy, fresh thread/materialization path and at least
  one state-first/read-only call.

Canonical fixture/scenario map:

| Action | Fixture owner | Canonical scenario proof |
| --- | --- | --- |
| `press_key` | Existing `SmokeWindowHost` focusable controls; extend only if tab/order proof is unstable. | `list_apps -> get_app_state -> press_key(Tab or Arrow*) -> get_app_state/wait` proves focus/selection moved to the expected helper control without typing arbitrary text. |
| `set_value` | Existing `Smoke query input` textbox if UIA `ValuePattern` is exposed; otherwise extend helper with a dedicated settable control. | `get_app_state` selects textbox element, `set_value` writes deterministic value, fresh `get_app_state` or UIA wait proves public value changed without clipboard/default typing. |
| `type_text` | Existing `Smoke query input` textbox after explicit focus proof. | Focus textbox through existing click/state loop, call `type_text` with deterministic non-sensitive text, then fresh state/wait proves text changed. |
| `scroll` | Existing tree/list only if it exposes stable scroll proof; otherwise extend helper with a deterministic scrollable panel/list. | `scroll(elementIndex)` uses semantic `ScrollPattern` when available, or confirmed coordinate fallback, then fresh state/capture proves position/content changed. |
| `perform_secondary_action` | Existing checkbox/tree/button affordances only if they expose stable UIA semantic pattern; otherwise extend helper with a dedicated secondary-action target. | Semantic secondary action changes an observable helper state; context-menu fallback, if accepted, returns `verify_needed` and requires fresh reobserve. |
| `drag` | Dedicated helper surface if implemented; existing helper is not assumed sufficient. | Source and destination are revalidated separately, drag executes only after confirmation, fresh state/capture proves deterministic visual or UIA state changed. |

Smoke non-goals:

- no arbitrary external GUI apps;
- no user desktop state as expected output;
- no proof based only on dispatch transcript;
- no hidden helper-only shortcut that bypasses public
  `list_apps -> get_app_state -> action -> get_app_state` loop.

## 13. Docs/generated sync

Docs to update as stages progress:

- `docs/architecture/computer-use-win-surface.md`
- `docs/architecture/computer-use-win-next-actions.md`
- `docs/product/okno-roadmap.md`
- `docs/product/okno-spec.md` if user-visible capability changes.
- `docs/architecture/observability.md` if audit/event schema changes.
- `docs/generated/computer-use-win-interfaces.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/commands.md`
- `docs/generated/test-matrix.md`
- `docs/CHANGELOG.md`
- this ExecPlan, then archive it to completed on closure.

Generated docs must be changed only through:

```powershell
scripts/refresh-generated-docs.ps1
```

If generated docs and runtime contract disagree, runtime/tests are source of
truth and generated docs must be refreshed, not manually patched.

## 14. Risks / rollback / unresolved forks

### Main risks

- UIA coverage for real third-party GUI apps can be poor. Semantic actions must
  fail truthfully and allow explicit low-level fallback only where safe.
- Global input through `SendInput` can conflict with the user session and can be
  blocked by foreground/UIPI constraints.
- Text input is sensitive: clipboard shortcuts and silent focus changes can
  create surprising side effects.
- Drag is highest-risk and may remain deferred if low-level proof and smoke are
  not strong.
- Public surface expansion can create tool noise if actions are promoted before
  they have crisp semantics.

### Rollback

- If a stage promotion causes publication or install-surface drift, revert only
  that stage commit and keep earlier deferred contract stages if still valid.
- If runtime proof fails for an action, leave it deferred and record a bounded
  prerequisite ExecPlan rather than weakening state/selector invariants.
- Never rollback by reintroducing broad `windows.*` public profile as the Codex
  path.

### Unresolved forks to close during Stage 1

- Exact `press_key` grammar: named keys only vs text chars plus named keys.
- Whether `type_text` v1 requires `elementIndex` or allows focused editable
  proof without element target.
- Whether `set_value` supports only string value first or also numeric/range
  typed payload.
- How much secondary-action vocabulary is public: generic
  `perform_secondary_action` only, or a small enum/hint.
- Whether drag is implemented in this wave or intentionally split into a
  prerequisite input-runtime ExecPlan.

## 15. Overlay cursor follow-up

Agent cursor/overlay is deliberately out of this action protocol.

Future sidecar may include:

- software cursor overlay;
- optional preview / picture-in-picture;
- local host UX progress display;
- no MCP payload shape changes;
- no dependency on a second real OS cursor;
- no dispatch ownership inside overlay.

Do not mix overlay work with public action promotion.

## 16. Closure report format after full implementation

After full product-ready implementation, return a closure report with:

1. What shipped: implemented actions, deferred actions, final profile, and how
   the loop improved.
2. Public contract: links to `ToolNames.cs`, `ToolDescriptions.cs`,
   `ToolContractManifest.cs`, generated interfaces, registration/schema files
   and public status/failure literals.
3. Runtime semantics: per-action behavior, semantic vs low-level fallback,
   stateToken/windowId invariants, confirmation/risk model, `done` vs
   `verify_needed`, Win32/UIA patterns used and intentional omissions.
4. Verification: unit tests, integration tests, smoke story and full sequential
   contour result.
5. Observability/artifacts: new events, artifact families, audit payloads and
   redaction.
6. Docs sync: roadmap, spec, architecture docs, active/completed ExecPlan,
   generated interfaces, commands, test matrix and changelog.
7. Install/publication proof if public/plugin surface changed.
8. Residual risks and next step.
9. Minimal closure packet: branch commit hash, completed ExecPlan path, roadmap
   row, generated interface row, smoke result, verify result and short e2e
   story.
