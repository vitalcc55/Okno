# ExecPlan: Computer Use for Windows screenshot-first hardening

Status: `active`  
Date: `2026-04-29`  
Primary scope: `focused type_text fallback`, `successor-state / action+observe`, `public instance continuity UX`

## 1. Goal

Усилить `computer-use-win` как **screenshot-first, state-first Windows operator surface** для poor-UIA / custom UI / React / Electron / Qt и других слабосемантических GUI, не ломая уже shipped safety model.

Конкретно:

- снизить стоимость цикла `get_app_state -> action -> get_app_state` там, где runtime уже может честно вернуть больше;
- закрыть узкий poor-UIA text-entry gap после screenshot-first navigation;
- улучшить instance continuity UX без rollback к наивному public selector;
- сохранить разделение между runtime truth model, operator/client UX и future adjacent work.

### Boundary frame

Этот workstream обязан держать три слоя раздельно:

- **Runtime truth model:** что `computer-use-win` реально доказал про target, input dispatch и successor state.
- **Operator/client UX:** как Codex или другой client отображает screenshot, window continuity hints и post-action loop.
- **Future adjacent work:** `region_capture`, OCR-lite, approvals/playbooks, clipboard и broad UIA/browser expansion.

## 2. Non-goals

- не делать broad OCR subsystem;
- не тащить `windows.region_capture` в эту работу, кроме одного узкого adjacent note;
- не ослаблять `windowId` до public `hwnd + processId`;
- не вводить clipboard/paste как hidden default для `type_text`;
- не превращать `verify_needed` в optimistic `done`;
- не плодить tool zoo вроде `click_and_observe`, `drag_and_observe`, `preview_app_state`, если это можно решить внутри текущего surface;
- не переписывать продукт обратно в low-level `windows.*` narrative;
- не чинить screenshot preview UX ценой отказа от first-class image content в runtime;
- не смешивать этот workstream с approvals/playbooks, clipboard, `uia_action`, browser-only UX или broad dialog/menu slices.

## 3. Current repo state

### Repo fit snapshot

Source of truth: только текущий checkout `C:\Users\v.vlasov\Desktop\Okno`.

Discovery snapshot от `2026-04-29`:

- worktree clean, branch `main`;
- roadmap уже переставлен под три follow-up slices в [docs/product/okno-roadmap.md](../../product/okno-roadmap.md);
- public profile `computer-use-win` уже shipped как девять tools в [src/WinBridge.Runtime.Tooling/ToolContractManifest.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractManifest.cs);
- generated/export/docs уже отражают shipped action wave в [docs/generated/computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md), [docs/generated/project-interfaces.md](../../generated/project-interfaces.md), [plugins/computer-use-win/README.md](../../../plugins/computer-use-win/README.md), [README.md](../../../README.md).

### Current runtime facts that matter for this plan

1. `get_app_state` уже image-bearing:
   [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs) добавляет `ImageContentBlock` и commit-ит `stateToken` только после успешной observation materialization.
2. `type_text` сейчас intentionally narrow:
   [ComputerUseWinTypeTextExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs) требует focused editable target с writable UIA proof и dispatch-ит только Win32 `SendInput` text path.
3. Clipboard default уже отсутствует:
   [Win32InputPlatform.cs](../../../src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs) dispatch-ит text через `SendInput`; в public docs и tests hidden paste path не заявлен.
4. Successor-state в action results сейчас отсутствует:
   [ComputerUseWinActionFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionFinalizer.cs) материализует только `status`, `refreshStateRecommended`, `failureCode`, `reason`, `targetHwnd`, `elementIndex`.
5. `windowId` сейчас discovery-scoped и generation-based:
   [ComputerUseWinIdentityModel.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinIdentityModel.cs) каждый `Materialize(...)` создаёт новый discovery generation; previous snapshot invalidates old selectors.
6. `stateToken` остаётся short-lived proof:
   [ComputerUseWinStateStore.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStateStore.cs) держит TTL `30s` и bounded retention `16`.
7. Observed-state continuity уже path-specific:
   [ComputerUseWinWindowContinuityProof.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinWindowContinuityProof.cs) различает semantic actions, coordinate screen и `capture_pixels`.

### Current gaps confirmed by repo state

- Нет request/result fields для successor-state opt-in (`observeAfter`, nested successor payload, post-action image content).
- Нет shared owner path для “action succeeded, then reobserve best-effort”.
- Нет poor-UIA fallback branch inside `type_text`; current path rejects anything weaker than focused editable proof.
- `windowId` churn смягчается только attached/hwnd reuse path, но repeated `list_apps` still reissues selectors.
- Screenshot preview gap локально трактуется как client/operator UX issue, не как runtime contract absence.

## 4. Shipped invariants to preserve

- `computer-use-win` остаётся public Codex-facing plugin/profile; low-level `windows.*` остаётся внутренним substrate.
- `windowId` остаётся runtime-owned selector, discovery-scoped и fail-closed.
- `stateToken` не становится reusable selector и не переживает stale/replacement silently.
- `get_app_state` продолжает возвращать screenshot как first-class image content, а не только `artifactPath`.
- `type_text` не получает hidden clipboard/paste path.
- `verify_needed` остаётся честным default result для low-confidence actions.
- post-action observation не имеет права downcast-ить committed action в ordinary pre-dispatch failure.
- malformed request shapes продолжают materialize-иться как `invalid_request`, а не как transport/binder noise.
- install/publication surface `plugins/computer-use-win` не должен drift-ить без fresh-host proof.

## 5. Exact source pack

### Repo-local sources

Policy/product boundary:

- [AGENTS.md](../../../AGENTS.md)
- [docs/product/okno-roadmap.md](../../product/okno-roadmap.md)
- [docs/product/okno-spec.md](../../product/okno-spec.md)
- [docs/product/okno-vision.md](../../product/okno-vision.md)

Surface/screenshot-first framing:

- [docs/architecture/computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [docs/architecture/observe-capture.md](../../architecture/observe-capture.md)
- [docs/architecture/openai-computer-use-interop.md](../../architecture/openai-computer-use-interop.md)
- [docs/architecture/reference-research-policy.md](../../architecture/reference-research-policy.md)

Shipped wave / generated truth:

- [docs/exec-plans/completed/completed-2026-04-28-computer-use-win-next-actions.md](../completed/completed-2026-04-28-computer-use-win-next-actions.md)
- [docs/generated/computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
- [docs/generated/project-interfaces.md](../../generated/project-interfaces.md)
- [docs/generated/commands.md](../../generated/commands.md)
- [docs/generated/test-matrix.md](../../generated/test-matrix.md)
- [docs/generated/stack-research.md](../../generated/stack-research.md)
- [README.md](../../../README.md)
- [plugins/computer-use-win/README.md](../../../plugins/computer-use-win/README.md)
- [docs/CHANGELOG.md](../../CHANGELOG.md)

### Official OpenAI sources

- [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [Images and vision](https://developers.openai.com/api/docs/guides/images-vision)
- [MCP and Connectors](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Tools overview](https://developers.openai.com/learn/tools)
- [Guide to Using the Responses API's MCP Tool](https://developers.openai.com/cookbook/examples/mcp/mcp_tool_guide)
- [Docs MCP](https://developers.openai.com/learn/docs-mcp)
- [Codex MCP](https://developers.openai.com/codex/mcp)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)

### Official MCP sources

- [MCP basic](https://modelcontextprotocol.io/specification/2025-11-25/basic)
- [MCP lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)
- [MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
- [MCP server tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP schema](https://modelcontextprotocol.io/specification/2025-11-25/schema)
- [MCP security best practices](https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices)

### Microsoft / Win32 / UIA sources

- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput)
- [MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [UI Automation overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview)
- [UI Automation control patterns overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-control-patterns-overview)
- [ValuePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.valuepattern?view=windowsdesktop-9.0)
- [RangeValuePattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.rangevaluepattern?view=windowsdesktop-9.0)
- [ScrollPattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.scrollpattern?view=windowsdesktop-10.0)
- [TextPattern overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview)
- [Screen capture guidance for Windows](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)

### Reference repos

Local cache present:

- `references/repos/Windows-MCP`
- `references/repos/Windows-MCP.Net`
- `references/repos/pywinauto-mcp`
- `references/repos/Peekaboo`

OpenAI sample repos were **not** present in local `references/repos` during this planning pass, so use them as secondary GitHub references rather than local source of truth:

- [openai/openai-cua-sample-app](https://github.com/openai/openai-cua-sample-app)
- [openai/openai-testing-agent-demo](https://github.com/openai/openai-testing-agent-demo)

## 6. Official docs and constraints

### OpenAI / Codex constraints

- `computer use` guidance normalizes a screenshot-first loop: screenshot before the first action batch is normal, and post-action loops should return updated screenshots as image input, not only local file paths.
- `images-vision` reinforces that spatially sensitive screenshots should keep original/full-fidelity detail, or coordinate remap must be explicit after downscale.
- OpenAI docs support custom MCP harnesses when the product already has a mature local operator surface; this workstream should harden `computer-use-win`, not rebuild around built-in `computer`.
- `allowed_tools`, approvals and Codex-side `enabled_tools` / `disabled_tools` belong to client/ops narrowing, not to `WinBridge.Runtime` contract design.
- Codex Windows guidance supports keeping the product Windows-native and PowerShell-native.

### MCP constraints

- Tool results may legitimately carry both `structuredContent` and rich `content`; image-bearing tool results are part of the protocol model, not a workaround.
- `isError=true` should be used for tool-level failures when the request reached the tool boundary; transport/protocol errors stay reserved for MCP/runtime framing failures.
- Server-side result honesty matters more than convenience: a committed action followed by failed reobserve cannot be rewritten as an ordinary pre-dispatch failure.
- Stateful selector issuance and app approvals are server responsibilities; client-side narrowing does not replace truthful server semantics.

### Microsoft / Win32 / UIA constraints

- `SendInput` is factual dispatch, not proof of semantic UI outcome.
- foreground/focus on Windows is best-effort and policy-limited; `SetForegroundWindow` and `GetForegroundWindow` enforce a real boundary that the runtime must keep explicit.
- UIA `ValuePattern` / `RangeValuePattern` justify `set_value` as the strong semantic path.
- `ScrollPattern` is the strong semantic path for `scroll`; `TextPattern` is not a generic writable proof substitute for arbitrary desktop apps.
- Poor-UIA apps often expose weaker semantic proof, so a degraded typing path can exist only with bounded focus proof and explicit confirmation.
- Screen capture APIs solve observation, not identity continuity; screenshot metadata must stay separate from live selector semantics.

## 7. Current product feedback synthesis

### What feedback already says

- screenshot-first navigation in poor-UIA apps is already usable;
- semantic and coordinate actions already cover many post-navigation steps;
- the biggest remaining text-entry gap is not “typing in general”, but typing after the operator has already navigated visually to a target that UIA still cannot prove as writable;
- the second gap is friction: honest `verify_needed` currently forces a full explicit `get_app_state` roundtrip even after actions where the runtime could reobserve immediately;
- the third gap is continuity churn: strict `windowId` semantics are correct, but repeated discovery cycles issue new selectors more often than the operator needs.

### Reference-repo takeaways

- `Windows-MCP` explicitly splits fast screenshot-first capture from heavier full-state capture and documents coordinate remap after downscale. This supports `get_app_state` / successor-state as separate observe semantics, not raw coordinate optimism.
- `pywinauto-mcp` shows the opposite extreme: portmanteau tools, clipboard, OCR and broad surface breadth. Useful as an anti-pattern for this workstream because it mixes too many adjacent concerns.
- `Peekaboo` keeps verification and region-focused capture in an enhancement layer rather than collapsing them into the base type/click tools. This supports keeping preview/verification UX adjacent to core runtime truth.

## 8. Design forks to close

### A. Focused `type_text` fallback

**Recommendation**

- Keep this inside existing `type_text`, not as a new tool.
- Add one explicit request opt-in, recommended name: `allowFocusedFallback: boolean`.
- Require `confirm=true` whenever `allowFocusedFallback=true`.
- Keep current default path unchanged when the flag is absent.
- Do **not** freeze the fallback to `elementIndex == null` only at plan level. Whether `v1` starts there as the minimal slice or also accepts a coarse/focusable `elementIndex` remains an explicit implementation fork to close against the proof model.

**Why this shape**

- It preserves the quiet public surface.
- It keeps semantic `set_value` and current focused-editable `type_text` as the preferred paths.
- It avoids silently widening existing `type_text` semantics.

**Minimum proof before dispatch**

- live `stateToken` resolution still passes through current observed-state proof;
- target window survives activation/foreground proof;
- runtime can prove a target-local focus boundary on the same window after activation, using either current UIA focused element evidence or one narrow additional focus probe;
- if a coarse/focusable `elementIndex` is present but writable proof is absent, that element may be treated as a stronger localization hint than window-level fallback, but only if the same focus proof can be kept honest and fail-closed;
- fallback is unavailable if this proof cannot be produced truthfully.

**Success semantics**

- Default result remains `verify_needed`.
- Do not return optimistic `done` from the fallback branch on dispatch alone.
- Only a later successor-state proof may justify stronger post-action evidence, and even then that proof belongs to Package C, not to raw fallback dispatch.

**Hard guardrails**

- No clipboard/paste default.
- No hidden focus guessing.
- No widening of projected `actions[]` just because fallback exists; public affordances should still prefer semantic `set_value` / current focused-editable `type_text`.

**Implementation spike allowed**

- If current UIA snapshot cannot provide honest target-local focus proof, run one narrow spike first to evaluate a Win32-focused control probe. If that probe cannot be kept narrow and fail-closed, keep fallback unavailable instead of widening semantics.

### B. Successor-state / action+observe

**Recommendation**

- Do not add `click_and_observe`, `drag_and_observe`, or similar extra tools.
- Add one explicit action-level opt-in, recommended name: `observeAfter: boolean`.
- Implement this through a shared post-action observe path used by existing actions.

**Payload direction**

- Keep top-level action result (`status`, `failureCode`, `refreshStateRecommended`) as the factual action outcome.
- Add optional nested successor envelope, recommended shape: `successorState`, with the same product payload family as `get_app_state` (`session`, `stateToken`, `capture`, `accessibilityTree`, `instructions`, `warnings`).
- When `successorState` is present, append a new `ImageContentBlock` for the updated screenshot.
- When `successorState` is present and materialized successfully, treat it as the fresh state for the next step; `refreshStateRecommended` should therefore become `false` for that result because another immediate `get_app_state` is no longer required.
- `verify_needed` may still remain the top-level action status even when `refreshStateRecommended=false`; that combination means “fresh state is already available, but the action outcome still should be interpreted conservatively”.

**Failure semantics**

- If the action itself failed pre-dispatch, do not run successor observe.
- If the action committed and successor observe fails, keep the action’s truthful top-level outcome and surface successor failure as best-effort advisory, not as a fake pre-dispatch failure.
- `verify_needed` remains ordinary for low-confidence actions even when successor observe succeeds; successor state lowers loop cost, not semantic honesty.

**Scope direction**

- First-class beneficiaries: `click`, focused fallback `type_text`, `press_key`, coordinate `scroll`, `drag`.
- `set_value` and semantic `perform_secondary_action` may reuse the shared machinery later, but they are not required to be the first adopters.

### C. Public instance continuity UX

**Recommendation**

- Improve publication behavior first.
- Keep `windowId` strict and discovery-scoped.
- Do not introduce public selector based on `hwnd + processId`.

**Primary continuity change**

- Reuse previously published `windowId` across repeated `list_apps` snapshots **only when** the live window still passes the same strict discovery proof.
- Preserve current invalidation behavior when the snapshot materially changes or continuity cannot be proven.

**Public hint decision**

- Do **not** add a new public actionable selector.
- Defer any extra non-actionable identity hint unless selector reuse alone proves insufficient in tests and operator flows.
- Existing `session` fields (`appId`, `title`, `processName`, `hwnd`) are enough for the first iteration if publication churn is reduced.

**Paths that should reuse the current selector**

- repeated `list_apps` over materially unchanged visible instances;
- `get_app_state(hwnd=...)` or attached fallback only when they strictly match the current published selector snapshot.

**Paths that must not reuse silently**

- replacement windows with reused `HWND`;
- post-activation windows that no longer match discovery proof;
- attached/hwnd paths when no current published selector strictly matches.

### D. Screenshot preview UX

**Decision**

- Runtime already returns first-class image content today; do not treat preview complaints as proof of runtime contract absence.
- Keep preview hint as client/operator UX note unless Package C needs one additive field to help certain clients render successor screenshots.
- Never replace image-bearing results with path-only metadata.

### E. Non-goal boundary freeze

- `windows.region_capture` stays out of this workstream.
- OCR-lite and text-box detection stay out of this workstream.
- `list_apps` enrichment like `commandLine` / `cwd` is adjacent and not core here.
- approvals/playbooks are the next workstream, not part of this plan.

## 9. Integration points by file

### Public contract and publication layer

| File | Why it matters for this workstream |
| --- | --- |
| [ToolNames.cs](../../../src/WinBridge.Runtime.Tooling/ToolNames.cs) | No new tools should be added for action+observe; this file is a guardrail against tool-zoo drift. |
| [ToolDescriptions.cs](../../../src/WinBridge.Runtime.Tooling/ToolDescriptions.cs) | Needs wording updates if `type_text` gains explicit focused fallback or if action results can optionally embed successor state. |
| [ToolContractManifest.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractManifest.cs) | Source of truth for shipped profile notes; must reflect new request/result semantics without pretending stronger proof. |
| [ToolContractExporter.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractExporter.cs) | Generated docs must expose any new action request/result fields consistently. |
| [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs) | Request/result DTO owner for `allowFocusedFallback`, `observeAfter`, and optional successor payload fields. |
| [ComputerUseWinToolRegistration.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs) | MCP schemas for new opt-in flags and successor-state result shape. |
| [ComputerUseWinTools.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs) | Must stay a thin adapter; no cross-action orchestration logic should leak here. |

### Observation owner path

| File | Why it matters |
| --- | --- |
| [ComputerUseWinGetAppStateHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs) | Baseline source of truth for current observation/approval/activation order. |
| [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs) | Current owner of state-token commit and image-bearing result; Package C should reuse, not re-invent, this shaping. |
| [ComputerUseWinAppStateObserver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs) | Current capture + UIA + instructions assembly path; likely needs extraction/reuse for post-action observe. |
| [ComputerUseWinAccessibilityProjector.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAccessibilityProjector.cs) | Public tree/public affordance shaping must stay compact if successor state reuses it. |
| [ComputerUseWinAffordanceResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAffordanceResolver.cs) | Must not widen `type_text` affordance indiscriminately. |

### State, token, continuity, selector lifetime

| File | Why it matters |
| --- | --- |
| [ComputerUseWinStateStore.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStateStore.cs) | Successor-state path will need a new committed token only after successful post-action observe. |
| [ComputerUseWinStoredStateResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStoredStateResolver.cs) | Current action-ready gate stays authoritative for fallback and action+observe. |
| [ComputerUseWinWindowContinuityProof.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinWindowContinuityProof.cs) | Central proof matrix for selector reuse and post-action continuity. |
| [ComputerUseWinIdentityModel.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinIdentityModel.cs) | Primary owner for continuity UX package; selector reuse belongs here. |
| [ComputerUseWinRuntimeStateModel.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinRuntimeStateModel.cs) | Guards that approval/attached/stale/blocked never become observed or action-ready silently. |
| [ComputerUseWinTargetPolicy.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTargetPolicy.cs) | Confirmation policy owner; focused fallback risk model must land here or in a narrow sibling, not ad hoc in the handler. |

### Shared action lifecycle

| File | Why it matters |
| --- | --- |
| [ComputerUseWinActionRequestExecutor.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs) | Best place to insert common post-dispatch successor observe orchestration without per-tool duplication. |
| [ComputerUseWinActionFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionFinalizer.cs) | Current public action payload owner; Package C must extend this carefully. |
| [ComputerUseWinActionObservability.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionObservability.cs) | Needs additive safe fields for fallback and successor-state evidence. |
| [ComputerUseWinToolResultFactory.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs) | Central place to keep tool-result shaping consistent. |
| [ComputerUseWinFailureCodeMapper.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinFailureCodeMapper.cs) | Any new public failure wording must be deliberate and minimal. |
| [ComputerUseWinAuditDataBuilder.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAuditDataBuilder.cs) | Redaction-safe audit summary for new request/result fields. |

### Action-specific owner paths

| File | Why it matters |
| --- | --- |
| [ComputerUseWinTypeTextContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextContract.cs) | Additive contract entry for focused fallback opt-in. |
| [ComputerUseWinTypeTextExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs) | Primary owner of current failure gate and future fallback branch. |
| [ComputerUseWinTypeTextHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextHandler.cs) | Observability summary needs to distinguish semantic vs focused-fallback path. |
| [ComputerUseWinClickExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinClickExecutionCoordinator.cs) | Characterization baseline for low-confidence action + possible `observeAfter`. |
| [ComputerUseWinScrollExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollExecutionCoordinator.cs) | Low-confidence wheel path is a prime Package C adopter. |
| [ComputerUseWinDragExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinDragExecutionCoordinator.cs) | Drag already defaults to `verify_needed`; ideal action+observe candidate. |
| [ComputerUseWinPressKeyExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinPressKeyExecutionCoordinator.cs) | Useful for post-hotkey/post-submit successor state. |
| [ComputerUseWinSetValueExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinSetValueExecutionCoordinator.cs) | Semantic path comparison baseline; should not regress into raw typing. |

### Low-level runtime dependencies

| File | Why it matters |
| --- | --- |
| [CaptureMetadata.cs](../../../src/WinBridge.Runtime.Contracts/CaptureMetadata.cs) | Successor state must reuse the same screenshot/capture metadata family as `get_app_state`. |
| [InputCaptureReference.cs](../../../src/WinBridge.Runtime.Contracts/InputCaptureReference.cs) | Coordinate successor state must preserve capture-reference discipline. |
| [CaptureReferenceGeometryPolicy.cs](../../../src/WinBridge.Runtime.Contracts/CaptureReferenceGeometryPolicy.cs) | No special-case geometry path for successor screenshots. |
| [CaptureReferencePublisher.cs](../../../src/WinBridge.Runtime.Windows.Capture/CaptureReferencePublisher.cs) | Relevant only if successor screenshots need the same capture bridge semantics. |
| [Win32InputService.cs](../../../src/WinBridge.Runtime.Windows.Input/Win32InputService.cs) | Current dispatch owner reused by fallback typing. |
| [Win32InputPlatform.cs](../../../src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs) | Keeps text dispatch on `SendInput`; must stay clipboard-free. |
| [Win32UiAutomationSetValueService.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationSetValueService.cs) | Strong semantic path baseline. |
| [Win32UiAutomationScrollService.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationScrollService.cs) | Strong semantic scroll baseline. |

### Tests and helper surfaces

| File | Why it matters |
| --- | --- |
| [ComputerUseWinObservationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs) | Observation floor; confirms structured failure and successful payload shaping. |
| [ComputerUseWinActionAndProjectionTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs) | Best integration floor for selector reuse, `type_text`, action promotion and continuity. |
| [ComputerUseWinFinalizationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinFinalizationTests.cs) | Best unit/integration floor for action result shaping and redaction. |
| [ComputerUseWinArchitectureTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs) | Schema guards for request/result changes. |
| [ComputerUseWinInstallSurfaceTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs) | Required if public wording/profile/install surface changes. |
| [McpProtocolSmokeTests.cs](../../../tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs) | MCP `tools/list` / schema / fresh-host proof for new fields and image blocks. |
| [ToolContractManifestTests.cs](../../../tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs) | Profile and manifest truth. |
| [ToolContractExporterTests.cs](../../../tests/WinBridge.Runtime.Tests/ToolContractExporterTests.cs) | Generated export truth. |
| [AuditLogTests.cs](../../../tests/WinBridge.Runtime.Tests/AuditLogTests.cs) | Redaction guard for new request/result fields. |
| [WindowInputToolTests.cs](../../../tests/WinBridge.Server.IntegrationTests/WindowInputToolTests.cs) | Low-level coordinate/capture discipline floor. |
| [tests/WinBridge.SmokeWindowHost/Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs) | Smoke helper must grow one poor-UIA typing target for Package B and possibly explicit mirror targets for Package C. |

## 10. Delivery packages

### Stage execution protocol

- [ ] Work strictly in order: Stage 0 (`Package A`) -> Stage 1 (`Package B`) -> Stage 2 (`Package C`) -> Stage 3 (`Package D`) -> Stage 4 (`Package E`) -> Stage 5 (full closure).
- [ ] Do not start a later stage until the current stage has: completed its mapped package checklist, updated its stage report, finished its stage-scoped verification, passed review approval and recorded a commit SHA.
- [ ] Update this file after each completed subtask, not only at the end.
- [ ] Each stage that changes behavior, contract, validation, publication, failure semantics, state semantics or docs/generated surface must use TDD where the mapped package calls for RED/GREEN work.

### Review gate before each commit

Before each stage commit, run at least two `gpt-5.5` review subagents:

- architecture/contract reviewer;
- tests/failure-path/docs/generated reviewer.

Subagent invocation rules:

- spawn fresh review subagents for the current stage;
- do **not** fork the main implementer chat history into them: `fork_context=false`;
- the implementer must set context explicitly in the prompt instead of relying on inherited thread history;
- every review subagent prompt must include the sandbox-mode addendum below verbatim;
- provide the exact files, diff/base-head context, checks run and source pack context needed for review in that prompt;
- after findings, confirm or reject each finding, fix root cause for confirmed findings, verify neighboring paths, then send re-review to the same agents when practical until approval.

Mandatory review prompt sandbox addendum:

```md
Это review в sandbox-режиме: не запускай bootstrap, verify, dotnet restore, dotnet test, сборку, smoke, линтеры и любые тяжёлые проверки, если я отдельно не попрошу; считай, что такие прогоны здесь нерелевантны или ненадёжны, и делай выводы только по статическому анализу diff, кода, тестов, docs и официальной документации. Если для подтверждения замечания очень нужен runtime-сигнал, сначала коротко укажи, зачем именно он нужен, но ничего не запускай без отдельного разрешения, не запрашивай разрешения на повышение прав.
```

Review prompt must include:

- stage id;
- scope;
- acceptance criteria;
- changed files;
- diff/base-head context;
- checks run;
- official docs checked;
- reference repos consulted when relevant;
- explicit questions for that reviewer.

Suggested prompt skeleton:

```md
Stage: `Stage N / Package X`
Role: `architecture/contract` | `tests/failure/docs/generated`
Scope:
Acceptance criteria:
Changed files:
Base/head context:
Checks run:
Official docs checked:
Reference repos checked:
Questions for review:
```

### Stage report template

```md
#### Отчёт этапа

- Статус этапа: `not_started` / `in_progress` / `blocked` / `ready_for_review` / `approved` / `committed`
- Branch:
- Commit SHA:
- TDD применялся:
- Проверки:
- Review agents:
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`
- Official docs checked:
- Reference repos checked:
- Подтверждённые замечания:
- Отклонённые замечания:
- Исправленные root causes:
- Проверенные соседние paths:
- Остаточные риски:
- Разблокировка следующего этапа:
```

### Stage mapping

- Stage 0 = `Package A`
- Stage 1 = `Package B`
- Stage 2 = `Package C`
- Stage 3 = `Package D`
- Stage 4 = `Package E`
- Stage 5 = final closure after `Package E`

### Package A: Baseline and decision freeze

**Stage mapping:** `Stage 0`

**Purpose**

- lock current repo fit;
- freeze source pack and boundaries;
- decide what is in scope and what is explicitly deferred.

**Primary outputs**

- this exec-plan;
- confirmed design decisions for `allowFocusedFallback`, `observeAfter`, and selector reuse strategy;
- explicit note that preview gap is client-side unless additive hint proves necessary.

**TDD / DDD fit**

- No implementation yet.
- Strong DDD fit at the vocabulary level: observed state, action outcome, selector continuity.

**Exit criteria**

- another agent can start Package B without rediscovering repo structure or source pack;
- design forks above are either frozen or reduced to one narrow spike.

**Stage gate:** before leaving Stage 0, fill the stage report, run the two required `gpt-5.5` review subagents with explicit-prompt/no-fork context, then create a dedicated commit.

#### Stage 0 checklist

- [x] Подтверждён baseline ветки и worktree через `git status --short --branch`: `codex/computer-use-win-screenshot-first-hardening`, clean до Stage 0 правок.
- [x] Выполнен Codex harness handshake: `scripts/codex/bootstrap.ps1`, затем `scripts/codex/verify.ps1`.
- [x] Подтверждено, что design decisions остаются frozen для `allowFocusedFallback`, `observeAfter`, selector reuse и screenshot preview boundary.
- [x] Подтверждено, что Stage 0 не меняет runtime behavior, public schemas, generated contracts или plugin install surface.
- [x] Отчёт Stage 0 заполнен до review gate.
- [x] По прямому user request добавлен mandatory sandbox-mode addendum для всех будущих review subagent prompts.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `0e59a3d`
- TDD применялся: `нет`, Stage 0 только фиксирует baseline/design freeze и не меняет runtime behavior
- Проверки: `scripts/codex/bootstrap.ps1` -> success; `scripts/codex/verify.ps1` -> success, build `0 warnings / 0 errors`, runtime tests `669/669`, integration tests `357/357`, smoke run `20260430T105948366`, generated-docs refresh completed; после verify у generated docs не было content diff, а EOL-only status очищен через `git add --renormalize`
- Review agents: `James -> approve`, `Locke -> approve`; после review по прямому user request добавлен mandatory sandbox-mode addendum для будущих review subagents
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`
- Official docs checked: OpenAI Docs MCP `tools-computer-use#4-capture-and-return-the-updated-screenshot`, OpenAI Apps SDK MCP server concept, OpenAI Codex MCP server guide; MCP server tools spec `2025-11-25`; Microsoft `GetForegroundWindow`
- Reference repos checked: `not_applicable` для Stage 0; implementation pattern comparison не нужен до Package B
- Подтверждённые замечания: `none`
- Отклонённые замечания: `none`
- Исправленные root causes: `none`
- Проверенные соседние paths: `docs/generated/computer-use-win-interfaces.*`, `docs/generated/project-interfaces.*`, `docs/generated/test-matrix.md`, `docs/CHANGELOG.md`, `.tmp/.codex/task_state/latest.md`; review agents дополнительно подтвердили, что `docs/generated/*` не требует Stage 0 commit, потому что content diff отсутствует
- Остаточные риски: focused fallback proof всё ещё требует Package B spike/test decision; successor-state result shaping и selector reuse остаются нереализованными до Packages C/D
- Разблокировка следующего этапа: после review approval и dedicated Stage 0 commit начать Package B с TDD RED tests для explicit `allowFocusedFallback` opt-in и `confirm=true`

### Package B: Focused `type_text` fallback

**Stage mapping:** `Stage 1`

**Intent**

- close the narrow poor-UIA text-entry gap without weakening current `type_text` defaults.

**Recommended contract**

- add `allowFocusedFallback: boolean` to `type_text`;
- require `confirm=true` when that flag is true;
- disallow hidden fallback when the flag is absent;
- keep `elementIndex` optional exactly as today; do not freeze the fallback to `elementIndex == null` only.
- if `v1` needs a narrower first delivery, `elementIndex == null` may be the minimal starting slice, but a coarse/focusable `elementIndex` remains an explicitly allowed stronger-localization candidate rather than an out-of-scope case.

**Primary files**

- [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)
- [ComputerUseWinToolRegistration.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs)
- [ComputerUseWinTypeTextContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextContract.cs)
- [ComputerUseWinTypeTextExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs)
- [ComputerUseWinTypeTextHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextHandler.cs)
- [ComputerUseWinTargetPolicy.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTargetPolicy.cs)
- one narrow new proof helper only if current UIA evidence is insufficient.

**Tests first**

- RED tests for request validation and explicit opt-in;
- RED tests that fallback refuses to run without `confirm=true`;
- RED tests that clipboard/paste does not appear in runtime path or audit;
- RED tests for stale/foreground/focus-proof failure taxonomy.
- if `v1` accepts coarse/focusable `elementIndex`, add RED tests proving that path uses stronger target localization without pretending writable proof.

**Expected result model**

- fallback success returns `verify_needed`;
- stale/foreground/focus-proof failures remain structured failures;
- no implicit semantic upgrade to `done`.

**Stage gate:** before leaving Stage 1, fill the stage report, run the two required `gpt-5.5` review subagents with explicit-prompt/no-fork context, then create a dedicated commit.

#### Stage 1 checklist

- [x] Добавлен `allowFocusedFallback: boolean` в public `type_text` request DTO и MCP schema без новых tools.
- [x] Validator требует `confirm=true`, если `allowFocusedFallback=true`; absent flag сохраняет старое поведение.
- [x] Focused fallback доступен только для target-local focused element с bounds и click affordance; writable UIA proof не подделывается.
- [x] Fresh UIA revalidation требует ровно один focused element и совпадение с resolved stored target; stale/missing focus proof fail-closed как structured failure.
- [x] Fallback dispatch остаётся `SendInput` text path; clipboard/paste path не добавлен и audit не пишет raw text.
- [x] Dispatch-only success на fallback нормализуется в `verify_needed`, даже если lower input result вернул optimistic `done`.
- [x] Добавлен helper-backed MCP integration story для poor-UIA custom control: `get_app_state -> click/focus -> get_app_state -> type_text(allowFocusedFallback=true, confirm=true) -> get_app_state`.
- [x] Tooling notes/descriptions отражают opt-in fallback, mandatory confirmation, fresh focus proof, `verify_needed` и отсутствие clipboard default.
- [x] Generated docs, product/architecture docs and plugin README synced for the Stage 1 public contract change after review found stale public docs.
- [x] Stage-scoped verification завершена targeted GREEN; broader full-branch verification still remains Stage 4/5.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `8ab34f2`
- TDD применялся: `да`; RED зафиксирован для request validation/schema, focused fallback behavior, stale focus-proof failure, audit clipboard/paste guard, no optimistic `done`, manifest wording и helper-backed MCP flow.
- Проверки: RED `TypeTextHandlerFocusedFallbackDoesNotPromoteDispatchOnlyDone` -> failed `done` vs `verify_needed`; RED `ToolContractManifestTests.ComputerUseWinContractNotesReflectShippedSecondaryAction` -> missing `allowFocusedFallback`; GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinActionAndProjectionTests.TypeTextHandler"` -> `12/12`; GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinArchitectureTests.TypeTextValidatorRequiresConfirmForFocusedFallbackOptIn|FullyQualifiedName~ComputerUseWinArchitectureTests.ComputerUseWinTypeTextToolSchemaExposesFocusedFallbackOptIn"` -> `2/2`; GREEN `dotnet test .\tests\WinBridge.Runtime.Tests\WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests.ComputerUseWinContractNotesReflectShippedSecondaryAction|FullyQualifiedName~AuditLogTests.BeginInvocationRedactsComputerUseWinTypeTextRequestSummary"` -> `2/2`; GREEN `dotnet test .\tests\WinBridge.Runtime.Tests\WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractExporterTests"` -> `11/11`; GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinTypeText"` -> `2/2`; GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools"` -> `1/1`; GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinPluginReadmeDocumentsCurrentShippedToolSurface"` -> `1/1`; `scripts/refresh-generated-docs.ps1` -> success, build `0 warnings / 0 errors`; `git diff --check` -> success with generated-doc line-ending normalization warnings only.
- Review agents: `Meitner -> approve/no P0-P3`; `Herschel -> approve/no P0-P3 after P2 fix`
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`; review prompts must include mandatory sandbox-mode addendum verbatim.
- Official docs checked: Stage 0 source pack reused; Package B implementation stayed within already frozen Microsoft `SendInput` / UIA proof constraints and did not require fresh online lookup.
- Reference repos checked: `not_applicable` для Stage 1 implementation; reference repos were not needed to choose the narrow proof model.
- Подтверждённые замечания: P2 stale public/generated/plugin docs after public `type_text` contract change; residual non-blocking gap for direct `elementIndex + allowFocusedFallback` test coverage.
- Отклонённые замечания: `none`
- Исправленные root causes: generated `computer-use-win-interfaces.*` refreshed; plugin README updated; product/architecture docs updated to mark focused fallback shipped; install-surface README guard added; direct `elementIndex + allowFocusedFallback` regression test added.
- Проверенные соседние paths: normal focused-editable `type_text` route, element-scoped fallback route, schema binder invalid-request path, manifest/exporter tests, audit redaction, MCP `tools/list` schema exposure, plugin README install-surface guard, helper-backed standard type_text MCP story, helper-backed poor-UIA fallback MCP story.
- Остаточные риски: full sequential contour and fresh cache-installed publication proof remain Stage 4/5; broad OCR, clipboard, region capture and successor-state remain out of Package B.
- Разблокировка следующего этапа: Stage 2 / Package C may start after this metadata update is committed.

### Package C: Successor-state / action+observe

**Stage mapping:** `Stage 2`

**Intent**

- reduce explicit post-action reobserve cost while preserving truthful action semantics.

**Recommended contract**

- add `observeAfter: boolean` to selected action requests;
- extend action result with optional `successorState`;
- append post-action screenshot as image content when successor state exists;
- reuse current observation envelope budget unless future evidence demands a separate nested tuning object.

**Primary files**

- [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)
- [ComputerUseWinActionRequestExecutor.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs)
- [ComputerUseWinActionFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionFinalizer.cs)
- [ComputerUseWinToolResultFactory.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs)
- [ComputerUseWinAppStateObserver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs)
- [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs)
- action-specific coordinators for early adopters: click, type_text, press_key, scroll, drag.

**Preferred internal shape**

- extract or formalize one shared observed-state payload/materializer so `get_app_state` and successor-state do not duplicate parallel DTO logic.

**Tests first**

- [x] RED tests for new schema fields and result shape;
- [x] RED tests that action success plus failed successor observe still returns factual action outcome;
- [x] RED tests for image-bearing action result when `observeAfter=true`;
- [x] RED tests for new token commit only on successful successor observe.

**Expected result model**

- top-level action status stays factual;
- `successorState` is additive;
- successful `successorState` satisfies the “need fresh state for the next step” concern, so `refreshStateRecommended=false` for that result shape.
- low-confidence top-level status may still remain `verify_needed` even when `successorState` is present; that status now means semantic conservatism, not “you must immediately call `get_app_state` again”.
- if successor observe fails after a committed action, keep the ordinary action result semantics and leave `refreshStateRecommended=true`.

#### Отчёт этапа

- Статус этапа: `approved`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `pending`
- TDD применялся: да; RED сначала падал на отсутствующих `ObserveAfter` request fields / executor constructor, затем GREEN закрыл schema, result shape, image block, successor token commit и failed successor observe advisory path.
- Проверки:
  - `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinActionAndProjectionTests.ClickHandlerReportsCaptureReferenceRequiredWhenLiveStateLacksCaptureProof|FullyQualifiedName~ComputerUseWinSelectedActionSchemasExposeObserveAfterOptIn|FullyQualifiedName~ClickHandlerEmbedsSuccessorStateAndImageWhenObserveAfterSucceeds|FullyQualifiedName~ClickHandlerKeepsCommittedActionOutcomeWhenObserveAfterFails|FullyQualifiedName~McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinClickUsesStateTokenAndElementIndexAfterApprovedAppState"` passed `6/6`.
  - `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinActionAndProjectionTests.ClickHandlerReportsCaptureReferenceRequiredWhenLiveStateLacksCaptureProof|FullyQualifiedName~ComputerUseWinSelectedActionSchemasExposeObserveAfterOptIn|FullyQualifiedName~ClickHandlerEmbedsSuccessorStateAndImageWhenObserveAfterSucceeds|FullyQualifiedName~ClickHandlerKeepsCommittedActionOutcomeWhenObserveAfterFails|FullyQualifiedName~ClickHandlerKeepsCommittedActionOutcomeWhenSuccessorMaterializationThrows|FullyQualifiedName~McpProtocolSmokeTests.ToolsListPublishesComputerUseWinProfileWithOnlyCuratedOperatorTools|FullyQualifiedName~McpProtocolSmokeTests.ComputerUseWinClickUsesStateTokenAndElementIndexAfterApprovedAppState|FullyQualifiedName~ComputerUseWinHandlersResolveFromServiceCollection"` passed `8/8` after self-review hardening.
  - `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinFinalizationTests"` passed `28/28`.
  - `dotnet test .\tests\WinBridge.Runtime.Tests\WinBridge.Runtime.Tests.csproj --filter "FullyQualifiedName~ToolContractManifestTests.ComputerUseWinContractNotesReflectShippedSecondaryAction|FullyQualifiedName~ToolContractExporterTests"` passed `12/12` after an earlier invalid parallel test invocation caused a file-lock and was rerun sequentially.
  - `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinPluginReadmeDocumentsCurrentShippedToolSurface"` passed `1/1` after bundled skill guidance fix.
  - `scripts\refresh-generated-docs.ps1` passed with build `0 warnings / 0 errors`.
- Review agents: `Copernicus -> approve/no P0-P3`; `Aristotle -> approve with non-blocking P3 docs-skill note`, re-review `approve/no remaining findings`
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`; review and re-review prompts included mandatory sandbox-mode addendum verbatim.
- Official docs checked: active source-pack constraints from Stage 0; no new external runtime facts introduced in this stage.
- Reference repos checked: none for Stage 2 implementation; repo-local owner paths and tests were sufficient.
- Подтверждённые замечания: P3 bundled plugin skill still described only old post-action `get_app_state` loop and omitted `observeAfter=true`.
- Отклонённые замечания: `none`
- Исправленные root causes: plugin-bundled `plugins/computer-use-win/skills/computer-use-win/SKILL.md` now documents `observeAfter=true` as a supported post-action loop alongside `get_app_state` and explicit verify-step; post-self-review hardening also made thrown successor materialization advisory instead of rewriting committed action outcome.
- Проверенные соседние paths: action finalizer/audit materialization, MCP tools/list schema, real helper click successor-state smoke, generated profile export, DI handler resolution, bundled plugin README + skill guidance.
- Остаточные риски: full install/publication contour remains Stage 4/5 scope.
- Разблокировка следующего этапа: blocked until Stage 2 commit SHA is recorded.

**Stage gate:** before leaving Stage 2, fill the stage report, run the two required `gpt-5.5` review subagents with explicit-prompt/no-fork context, then create a dedicated commit.

### Package D: Public instance continuity UX

**Stage mapping:** `Stage 3`

**Intent**

- reduce selector churn without weakening strict discovery proof.

**Recommended change**

- keep current strict selector acceptance;
- change publication behavior so repeated unchanged discovery snapshots reuse existing `windowId` instead of minting a new one every time;
- keep null `windowId` on explicit `hwnd` / attached paths when there is no current strict published match.

**Primary files**

- [ComputerUseWinIdentityModel.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinIdentityModel.cs)
- [ComputerUseWinGetAppStateTargetResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateTargetResolver.cs)
- [ComputerUseWinGetAppStateHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs)
- [ComputerUseWinWindowContinuityProof.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinWindowContinuityProof.cs)
- [ComputerUseWinListAppsHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinListAppsHandler.cs)
- relevant tests in [ComputerUseWinActionAndProjectionTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs).

**Tests first**

- repeated `list_apps` on unchanged windows reuses `windowId`;
- drifted discovery snapshot issues a new selector or nulls the old path;
- replacement/reused `HWND` never reuses the previous selector silently;
- explicit `hwnd` and attached fallback still do not mint a fake reusable selector.

**Expected public outcome**

- same public schema unless later evidence forces one additive advisory hint;
- lower `list_apps` churn for steady windows;
- unchanged fail-closed behavior on ambiguity or replacement.

**Stage gate:** before leaving Stage 3, fill the stage report, run the two required `gpt-5.5` review subagents with explicit-prompt/no-fork context, then create a dedicated commit.

### Package E: Verification, docs and install-surface sync

**Stage mapping:** `Stage 4`

**Intent**

- prove the three slices through the standard sequential contour and synchronize public docs.

**Primary files and artifacts**

- [docs/generated/computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
- [docs/generated/project-interfaces.md](../../generated/project-interfaces.md)
- [docs/generated/commands.md](../../generated/commands.md)
- [docs/generated/test-matrix.md](../../generated/test-matrix.md)
- [docs/architecture/computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [docs/architecture/observe-capture.md](../../architecture/observe-capture.md)
- [docs/product/okno-roadmap.md](../../product/okno-roadmap.md)
- [plugins/computer-use-win/README.md](../../../plugins/computer-use-win/README.md)
- [docs/CHANGELOG.md](../../CHANGELOG.md)
- completed exec-plan moved from `active` to `completed` after closure.

**Acceptance**

- full sequential contour passes;
- fresh-host/publication proof stays green if schemas or profile wording changed;
- docs explain successor-state and continuity semantics without pretending broader OCR/clipboard work.

**Stage gate:** before leaving Stage 4, fill the stage report, run the two required `gpt-5.5` review subagents with explicit-prompt/no-fork context, then create a dedicated commit.

### Stage 5: Full closure

**Mapped scope**

- final closure after Stages 0-4 are green and committed.

**Steps**

- [ ] Move or copy the active plan to `docs/exec-plans/completed/` with final status.
- [ ] Sync roadmap/spec/architecture/plugin/generated docs in the same cycle.
- [ ] Run full sequential contour:
  `scripts/build.ps1` -> `scripts/test.ps1` -> `scripts/smoke.ps1` -> `scripts/refresh-generated-docs.ps1` -> `scripts/codex/verify.ps1`.
- [ ] Run full-branch review against `main` with architecture/contract and tests/failure/docs/generated subagents, again with explicit-prompt/no-fork context.
- [ ] Prepare the closure report in the product-ready format requested by this plan.

**Acceptance criteria**

- [ ] Shipped vs out-of-scope slices are listed explicitly.
- [ ] Final docs/generated/plugin surface matches the real public profile and verification results.
- [ ] Install/publication proof covers cache-installed copy when public surface wording or behavior changed.
- [ ] Residual risks and next work item are explicit.
- [ ] Review/re-review approval from two `gpt-5.5` agents is recorded before the closure commit.

## 11. Test ladder

### L1: unit / contract / owner tests

- `ToolContractManifestTests`
- `ToolContractExporterTests`
- `AuditLogTests`
- new focused-fallback contract tests
- new selector reuse tests against `ComputerUseWinExecutionTargetCatalog`
- new action finalizer tests for successor-state shaping and post-dispatch observe failure

### L2: server / integration tests

- `ComputerUseWinActionAndProjectionTests`
- `ComputerUseWinFinalizationTests`
- `ComputerUseWinObservationTests`
- `ComputerUseWinArchitectureTests`
- `ComputerUseWinInstallSurfaceTests`
- `McpProtocolSmokeTests`

### L3: real smoke

- extend helper app with one poor-UIA typing target that can receive focus and visible text changes but does not expose current writable UIA proof;
- keep existing semantic textbox for current `set_value` / `type_text` path so new fallback does not erase the old proof-backed route;
- add one explicit smoke story for `observeAfter=true` with post-action updated screenshot;
- keep current drag/scroll/selectors smoke stories intact.

## 12. Smoke strategy

### New smoke stories to add

1. `get_app_state -> click/focus poor-UIA target -> get_app_state -> type_text(allowFocusedFallback=true, confirm=true) -> get_app_state`
   at minimum this covers the no-`elementIndex` focused fallback path; if `v1` also accepts coarse/focusable `elementIndex`, add a paired story for that stronger-localization branch.
2. `get_app_state -> click(..., observeAfter=true)` and assert:
   updated screenshot image block exists,
   successor state payload exists,
   new `stateToken` is usable on the next action,
   `refreshStateRecommended=false` because fresh state is already embedded in the action result.
3. repeated `list_apps` on an unchanged helper window should reuse the same `windowId`.
4. helper window drift that changes discovery proof should force a new selector or null publication rather than silent reuse.

### Helper requirements

- add one focusable custom control or owner-drawn target that visually mirrors typed text but does not publish the same strong UIA writable proof as the standard textbox;
- keep deterministic mirror labels so smoke can verify visible outcome without OCR.

## 13. Docs / generated sync

When implementation starts, the agent must sync all impacted docs in the same cycle:

- roadmap order/status text in [docs/product/okno-roadmap.md](../../product/okno-roadmap.md)
- screenshot-first semantics and client/runtime boundary in [docs/architecture/computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- capture/observation note if successor-state returns updated screenshot in [docs/architecture/observe-capture.md](../../architecture/observe-capture.md)
- plugin/operator wording in [plugins/computer-use-win/README.md](../../../plugins/computer-use-win/README.md)
- generated interfaces/commands/test matrix via `scripts/refresh-generated-docs.ps1`
- closure entry in [docs/CHANGELOG.md](../../CHANGELOG.md)
- completed exec-plan under `docs/exec-plans/completed/`

## 14. Risks / rollback / out-of-scope

### Main risks

- **Focused fallback proof risk:** a poor-UIA fallback is only worth shipping if the focus proof stays narrow and truthful.
- **Result-shaping risk:** successor-state can accidentally blur action truth and observation truth if top-level status and nested state are not clearly separated.
- **Continuity regression risk:** selector reuse logic can accidentally reintroduce silent retarget if it drifts from strict discovery proof.
- **Client confusion risk:** preview complaints may push implementation toward runtime contract regressions; this must be resisted.

### Rollback posture

- Package B and C should be additive opt-in semantics; absence of the new flags must preserve current behavior.
- Package D changes behavior, not schema; keep rollback simple by isolating selector reuse logic inside the catalog/publication layer.
- If successor-state shaping proves too invasive, ship B and D first and keep C behind unfinished contract fields rather than forcing an all-or-nothing merge.

### Explicitly out of scope after this plan

- OCR-lite and text detection
- `windows.region_capture`
- clipboard and paste workflow
- approvals hardening
- playbooks expansion
- `windows.uia_action`
- browser-only or client-only rendering redesign

## 15. Recommended implementation sequence

1. Execute strictly in Stage order: `Stage 0 -> Stage 1 -> Stage 2 -> Stage 3 -> Stage 4 -> Stage 5`.
2. Keep package scope narrow inside each stage; if a new prerequisite is discovered, record it in the stage report instead of silently pulling later-stage work forward.
3. Implement `Package B` with strict TDD on contract, failure taxonomy and helper-backed integration proof before widening any successor-state work.
4. Only after `Package B` is green, extract shared observed-state materializer for `Package C`.
5. Inside `Package C`, land `observeAfter` first on one action pair (`click`, `type_text`) before widening to `press_key` / `scroll` / `drag`.
6. Extend smoke helper only after contract/unit tests for the current stage are already red and specified.
7. Run the full sequential contour only in `Stage 4` / `Stage 5`, not as a substitute for stage-scoped verification earlier.
8. Close with docs/generated sync, full-branch review and completed-plan archival in `Stage 5`.

## 16. Decision summary

- `type_text` fallback: same tool, explicit `allowFocusedFallback`, mandatory `confirm`, default `verify_needed`; `elementIndex == null` may be the narrowest `v1`, but the plan does not freeze that as the only valid fallback shape.
- action+observe: same tools, explicit `observeAfter`, shared post-action observer, nested `successorState`, additive image content; successful successor observe satisfies immediate refresh need even if top-level status remains `verify_needed`.
- continuity UX: reduce selector churn by reusing `windowId` only across repeated strict discovery matches; no public `hwnd + processId` selector.
- screenshot preview: runtime already correct; treat preview as client/operator UX unless an additive hint is strictly needed.
