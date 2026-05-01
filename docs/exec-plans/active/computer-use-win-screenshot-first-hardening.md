# ExecPlan: Computer Use for Windows screenshot-first hardening

Status: `active`
Date: `2026-04-30`
Primary scope: `focused type_text fallback`, `successor-state / action+observe`, `public instance continuity UX`

## Completion status

- Status: completed on branch `codex/computer-use-win-screenshot-first-hardening`
- Stage 1 package commit: `8ab34f2`
- Stage 2 package commit: `cd57192`
- Stage 3 package commit: `a76effd`
- Stage 4 package commit: `4840007d25981a13d78a44a688d901600cebbb28`
- Stage 5 closure commit: `b92ae22b7eb17202d3f51d6c85b8223ba402594f`
- Closure path: archived completed execution record; stage-by-stage sections
  below are preserved as historical delivery evidence rather than an active
  work queue.
- Reopen path: этот active файл возвращён из completed record после product
  feedback на реальном Telegram/Qt сценарии; historical stages `0-5` остаются
  evidence, а живая работа продолжается новыми stages `6-8`.

> **Для agentic workers:** этот active файл является reopened continuation
> historical completed record для screenshot-first hardening workstream
> `computer-use-win`. Historical stages `0-5` остаются evidence of delivery
> order, verification ladder and review gates that produced the shipped slice;
> active execution now continues through stages `6-8`.

## Reopen status

Причина reopen:

- shipped ветка закрыла helper/product-neutral first slice;
- реальный product run против Telegram через cache-installed
  `computer-use-win` подтвердил, что screenshot-first navigation до
  `Польза -> Рабочий👷‍♂️` работает, но text entry по-прежнему fail-close-ится;
- значит workstream нельзя считать продуктово закрытым, пока не закрыт именно
  Telegram/Qt poor-UIA text-entry acceptance signal.

Reopen boundary:

- historical stages `0-5` не переписываются и не откатываются;
- новые stages `6-8` должны либо довести Telegram acceptance до реального
  product pass, либо честно зафиксировать, что для этого нужен отдельный
  follow-up beyond current safety model;
- no OCR, no clipboard default, no blind active-window typing и no silent
  rollback к weaker selector semantics остаются неизменными guardrails.

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

- [ ] Historical stages `0-5` already remain committed evidence in this file; reopened execution works strictly in order: Stage 6 (Telegram diagnosis + proof-fork freeze) -> Stage 7 (implementation) -> Stage 8 (real product acceptance + re-closure).
- [ ] Do not reopen historical stages `0-5` unless Stage 6 proves a class-level root cause that invalidates shipped behavior rather than extending it.
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
- Stage 5 = historical closure after `Package E`
- Stage 6 = `Telegram / Qt diagnosis and proof-fork closure`
- Stage 7 = `Telegram text-entry gap implementation`
- Stage 8 = `Real Telegram product acceptance and re-closure`

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

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `cd57192`
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
- Разблокировка следующего этапа: Stage 3 / Package D may start after this metadata update is committed.

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

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `a76effd`
- TDD применялся: да; RED сначала падал на repeated unchanged discovery snapshot (`list_apps` и direct catalog) из-за mint нового `windowId`, затем GREEN подтвердил strict selector reuse без public schema changes.
- Проверки:
  - RED `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ListAppsReusesWindowIdsForUnchangedDiscoverySnapshot|FullyQualifiedName~ExecutionTargetCatalogReusesWindowIdAcrossStrictDiscoveryMatch|FullyQualifiedName~ExecutionTargetCatalogIssuesNewWindowIdWhenDiscoverySnapshotDrifts|FullyQualifiedName~ExecutionTargetCatalogDoesNotReuseWindowIdForReusedHwndReplacement"` failed `2/4` on unchanged selector churn.
  - GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ListAppsReusesWindowIdsForUnchangedDiscoverySnapshot|FullyQualifiedName~ExecutionTargetCatalogReusesWindowIdAcrossStrictDiscoveryMatch|FullyQualifiedName~ExecutionTargetCatalogIssuesNewWindowIdWhenDiscoverySnapshotDrifts|FullyQualifiedName~ExecutionTargetCatalogDoesNotReuseWindowIdForReusedHwndReplacement"` passed `4/4`.
  - GREEN `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ListAppsReusesWindowIdsForUnchangedDiscoverySnapshot|FullyQualifiedName~ExecutionTargetCatalogReusesWindowIdAcrossStrictDiscoveryMatch|FullyQualifiedName~ExecutionTargetCatalogIssuesNewWindowIdWhenDiscoverySnapshotDrifts|FullyQualifiedName~ExecutionTargetCatalogDoesNotReuseWindowIdForReusedHwndReplacement|FullyQualifiedName~ExecutionTargetCatalog|FullyQualifiedName~GetAppStateTargetResolver"` passed `15/15`.
  - GREEN after P3 fix `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ExecutionTargetCatalogDoesNotReuseWindowIdWhenCurrentDiscoveryBatchHasDuplicateStrictMatches|FullyQualifiedName~ListAppsReusesWindowIdsForUnchangedDiscoverySnapshot|FullyQualifiedName~ExecutionTargetCatalogReusesWindowIdAcrossStrictDiscoveryMatch|FullyQualifiedName~ExecutionTargetCatalogIssuesNewWindowIdWhenDiscoverySnapshotDrifts|FullyQualifiedName~ExecutionTargetCatalogDoesNotReuseWindowIdForReusedHwndReplacement|FullyQualifiedName~ExecutionTargetCatalog|FullyQualifiedName~GetAppStateTargetResolver"` passed `16/16`.
  - `git diff --check` passed.
- Review agents: `Pasteur -> approve with non-blocking P3 duplicate-current-batch note`, re-review `approve/no remaining findings`; `Einstein -> approve/no P0-P3`
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`; review prompts must include mandatory sandbox-mode addendum verbatim.
- Official docs checked: Stage 0 source-pack constraints reused; Stage 3 changes repo-local selector publication behavior only and did not require fresh online lookup.
- Reference repos checked: none for Stage 3 implementation; strict discovery proof owner paths were local.
- Подтверждённые замечания: P3 current discovery batch with duplicate strict matches could reuse one old `windowId` twice.
- Отклонённые замечания: `none`
- Исправленные root causes: added duplicate-current-batch regression and disabled previous-entry reuse when more than one pending target would reuse the same latest-published `windowId`.
- Проверенные соседние paths: `list_apps` public payload, direct execution target catalog reuse, drifted discovery snapshot, reused `HWND` replacement, duplicate-current-batch ambiguity, existing catalog overflow/invalidation tests, explicit `hwnd` and attached fallback target resolver behavior.
- Остаточные риски: full docs/generated/install contour remains Stage 4/5 scope.
- Разблокировка следующего этапа: Stage 4 / Package E may start after this metadata update is committed.

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

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `4840007d25981a13d78a44a688d901600cebbb28`
- TDD применялся: `not_applicable` для docs/verification sync; regression fix for the Stage 2 test marker was driven by the failing full test contour and verified by a targeted test before the full contour was rerun.
- Проверки:
  - `.\scripts\build.ps1` passed with `0 warnings / 0 errors`.
  - Initial `.\scripts\test.ps1` exposed one Stage 2 accidental test-marker drift in `ClickExecutionCoordinatorReappliesConfirmationAfterRetryReresolution`; targeted diagnosis confirmed the second fresh UIA snapshot had to stay risky (`Name = "Delete item"`, `AutomationId = "DeleteButton"`), and the test was restored.
  - `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ClickExecutionCoordinatorReappliesConfirmationAfterRetryReresolution"` passed `1/1` after the marker fix.
  - Re-run `.\scripts\build.ps1` passed with `0 warnings / 0 errors`.
  - Re-run `.\scripts\test.ps1` passed `669/669` runtime tests and `375/375` server integration tests.
  - `.\scripts\smoke.ps1` passed with smoke run `20260430T131856610`, declared tools `17`, and report `artifacts\smoke\20260430T131856610\report.json`.
  - Fresh-host/publication evidence is covered by the same smoke/publication contour: `fresh_host_windows_input_tools_list=verified`, `fresh_host_windows_input_contract=verified`, `fresh_host_windows_input_missing_target=failed/missing_target`, and declared tool count stayed `17`.
  - `.\scripts\refresh-generated-docs.ps1` passed with build `0 warnings / 0 errors`; generated docs stayed unchanged after the current code/doc sync.
  - `.\scripts\codex\verify.ps1` passed end-to-end: build `0 warnings / 0 errors`, tests `669/669` and `375/375`, smoke run `20260430T132553203`, and final generated-doc refresh.
- Review agents: `Socrates` (architecture/contract) approved initial review with non-blocking P3 docs fixes and approved re-review; `Fermat` (tests/failure/docs/generated) requested one P2 report-evidence fix and one P3 drag-docs fix, then approved re-review with no remaining P0-P3 findings.
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`
- Official docs checked: Stage 0 source-pack constraints reused; Stage 4 changed repo-local docs/verification/install-surface wording only and did not require fresh online lookup.
- Reference repos checked: none for Stage 4; no new runtime behavior was introduced.
- Подтверждённые замечания: P3 stale roadmap date, P3 missing Stage 4 changelog entry, P2 Stage 4 report did not spell out fresh-host/publication evidence, P3 drag docs still implied fresh `get_app_state` was always required even when `observeAfter=true` succeeds.
- Отклонённые замечания: none.
- Исправленные root causes: restored the risky re-resolution test marker accidentally weakened during Stage 2 from `Continue` / `ContinueButton` back to `Delete item` / `DeleteButton`; updated roadmap date, changelog Stage 4 entry, fresh-host/publication evidence in this report and drag no-`observeAfter` wording.
- Проверенные соседние paths: full build/test/smoke/generated-doc/verify contour, plugin README wording, root README wording, roadmap and architecture docs for successor-state / selector-continuity language.
- Остаточные риски: final closure still needs completed-plan archival, Stage 5 full sequential contour and full-branch review before closure commit.
- Разблокировка следующего этапа: Stage 5 may start after Stage 4 review approval and commit.

**Stage gate:** before leaving Stage 4, fill the stage report, run the two required `gpt-5.5` review subagents with explicit-prompt/no-fork context, then create a dedicated commit.

### Stage 5: Full closure

**Mapped scope**

- final closure after Stages 0-4 are green and committed.

**Steps**

- [x] Move or copy the active plan to `docs/exec-plans/completed/` with final status.
- [x] Sync roadmap/spec/architecture/plugin/generated docs in the same cycle.
- [x] Run full sequential contour:
  `scripts/build.ps1` -> `scripts/test.ps1` -> `scripts/smoke.ps1` -> `scripts/refresh-generated-docs.ps1` -> `scripts/codex/verify.ps1`.
- [x] Run full-branch review against `main` with architecture/contract and tests/failure/docs/generated subagents, again with explicit-prompt/no-fork context.
- [x] Prepare the closure report in the product-ready format requested by this plan.

**Acceptance criteria**

- [x] Shipped vs out-of-scope slices are listed explicitly.
- [x] Final docs/generated/plugin surface matches the real public profile and verification results.
- [x] Install/publication proof covers cache-installed copy when public surface wording or behavior changed.
- [x] Residual risks and next work item are explicit.
- [x] Review/re-review approval from two `gpt-5.5` agents is recorded before the closure commit.

#### Отчёт этапа

- Статус этапа: `committed`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `b92ae22b7eb17202d3f51d6c85b8223ba402594f`
- Shipped slices:
  - Package B: `type_text` focused fallback through explicit `allowFocusedFallback=true` + `confirm=true`, fresh target-local focus proof, SendInput text dispatch, no clipboard/paste default and public `verify_needed` semantics.
  - Package C: `observeAfter=true` on `click`, `press_key`, `type_text`, `scroll` and `drag`, with nested `successorState`, updated screenshot image content, short-lived successor `stateToken`, factual top-level action status and advisory `successorStateFailure`.
  - Package D: strict selector reuse for repeated unchanged `list_apps` discovery snapshots, with no public `hwnd + processId` selector and fail-closed drift/replacement/duplicate-current-batch behavior.
- Explicitly out of scope:
  - OCR-lite and text detection.
  - `windows.region_capture`.
  - Clipboard/paste workflows.
  - Approvals hardening and playbooks expansion.
  - `windows.uia_action`, browser-only UX and client rendering redesign.
- Verification:
  - Final-review P1 regression was reproduced RED first: `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ExecutionTargetCatalogFailsClosedWhenResolvingWindowIdAgainstDuplicateStrictLiveMatches"` failed with `InvalidOperationException: Sequence contains more than one matching element` before the fix.
  - After the fix, the same regression test passed `1/1`; neighboring selector/resolver filter passed `17/17`.
  - `.\scripts\codex\publish-computer-use-win-plugin.ps1` refreshed the repo plugin-local runtime bundle in `plugins\computer-use-win\runtime\win-x64`.
  - `dotnet test .\tests\WinBridge.Server.IntegrationTests\WinBridge.Server.IntegrationTests.csproj --filter "FullyQualifiedName~ComputerUseWinInstallSurfaceTests.ComputerUseWinLauncherFromTempPluginCopyPublishesPublicSurfaceWithoutRepoHints"` passed `1/1` after the install-copy tools/list schema assertion was extended to cover `allowFocusedFallback`, selected-action `observeAfter` and absence of `observeAfter` on `set_value` / `perform_secondary_action`.
  - Cache-installed proof: stale cache MCP processes under `C:\Users\v.vlasov\.codex\plugins\cache\computer-use-win-local\computer-use-win\0.1.0` were stopped, the old cache copy was backed up to `.tmp\.codex\plugin-cache-backup-20260430-142857\computer-use-win-local\computer-use-win\0.1.0`, the cache copy was synchronized from `plugins\computer-use-win`, and a fresh cache-launched MCP host proved `tools/list` + `list_apps` in `.tmp\.codex\computer-use-win-cache-proof\proof-20260430-142946.json`.
  - The cache proof reported protocol `2025-11-25`, server `Okno.Server`, tools `click`, `drag`, `get_app_state`, `list_apps`, `perform_secondary_action`, `press_key`, `scroll`, `set_value`, `type_text`, `typeTextHasAllowFocusedFallback=true`, `selectedActionsHaveObserveAfter=true`, `semanticOnlyActionsLackObserveAfter=true`, `listAppsStatus=ok`, and `appCount=10`.
  - Post-review `.\scripts\build.ps1` passed with `0 warnings / 0 errors`.
  - First post-review `.\scripts\test.ps1` exposed a transient UIA worker timeout in `McpProtocolSmokeTests.ComputerUseWinPressKeyMovesKeyboardFocusThroughApprovedAppState`; diagnostic stdout already contained a successful snapshot with focus on `Transient wait target`, targeted rerun passed `1/1`, and the full retry passed `669/669` runtime tests plus `376/376` server integration tests.
  - Post-review `.\scripts\smoke.ps1` passed with run `20260430T145037825`, declared tools `17`, fresh-host checks `fresh_host_windows_input_tools_list=verified`, `fresh_host_windows_input_contract=verified`, `fresh_host_windows_input_missing_target=failed/missing_target`, and report `artifacts\smoke\20260430T145037825\report.json`.
  - Post-review `.\scripts\refresh-generated-docs.ps1` passed with build `0 warnings / 0 errors`.
  - First post-review `.\scripts\codex\verify.ps1` exposed a transient UIA worker timeout in `McpProtocolSmokeTests.ComputerUseWinTypeTextUpdatesQueryMirrorAfterExplicitFocusProof` before dispatch; diagnostic stdout already contained a successful focused `Smoke query input` snapshot, targeted rerun passed `1/1`, and the full retry passed end-to-end: build `0 warnings / 0 errors`, tests `669/669` and `376/376`, smoke run `20260430T151003984`, final generated-doc refresh, and total `00:04:49.8436759`.
- Review agents: `Ampere` found one blocking P1 selector ambiguity, then approved architecture/contract re-review with no remaining P0-P3 findings; `Kant` found one P2 cache-installed proof overclaim and one P3 stale changelog wording, then approved tests/failure/docs/generated re-review with no remaining P0-P3 findings.
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`
- Official docs checked: Stage 0 source-pack constraints reused; no new external-doc-dependent runtime semantics were introduced in Stage 5.
- Reference repos checked: none in Stage 5; closure verified shipped local behavior and docs.
- Подтверждённые замечания: P1 duplicate strict live matches could throw during `windowId` resolution; P2 closure overclaimed cache-installed proof using only generic `windows.input` fresh-host evidence; P3 changelog still said `того же active exec-plan` after archival.
- Отклонённые замечания: `none`
- Исправленные root causes: active exec-plan archived under `docs/exec-plans/completed/`, changelog links retargeted from active to completed path, Stage 4 verification/fresh-host evidence preserved in the completed record, `TryResolveWindowId` now counts strict live matches without throwing and fails closed on ambiguity, install-copy schema proof now covers the new fields, and cache-installed `computer-use-win` proof is backed by a real cache-launched MCP host.
- Проверенные соседние paths: roadmap, root README, plugin README, architecture docs, generated docs refresh and full verification contour.
- Остаточные риски: screenshot preview remains client/operator UX work; approvals/playbooks, clipboard, region capture and OCR-lite remain explicit future work.
- Next work item: approvals hardening + risky action confirmation, then app playbooks expansion.

#### Post-closure adversarial review addendum

- Статус: `closed`
- Review agents: `Goodall`, `Hume`, `Nietzsche`
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`
- Review mode: sandbox/static-analysis; prompts explicitly forbade bootstrap, verify, dotnet restore/build/test, smoke, linters, generated-doc refresh, GUI/server processes and any heavy checks without a separate request.
- Подтверждённые замечания:
  - P1 focused `type_text` fallback admitted arbitrary focused clickable non-window controls instead of a text-entry-like target class.
  - P2 successor observe reused pre-action `WindowDescriptor` / `windowId` instead of re-resolving the post-action live window before nested state materialization.
  - P2 `successorStateFailure.reason` could transit raw UIA/capture failure text because `observation_failed` lacked product-owned public wording.
  - P2 docs/generated/proof drift: observability docs missed successor/fallback safe fields, generated test matrix omitted focused fallback / `observeAfter` / continuity / cache proof coverage, and cache-installed proof was not reproducible as a committed script.
  - P2 closure proof gap: direct `type_text(allowFocusedFallback=true, confirm=true, observeAfter=true)` e2e was not covered by a single runtime/smoke story.
  - Re-review P1: token classification still used substring hints, so `ContextMenu`, `ResearchPanel`, `FieldsetHost` and `EntryPointCanvas` could pass as text-entry-like `document`/`custom` controls.
  - Re-review P2: `observe_after_requested` was added only on committed success path, so failed/approval pre-dispatch branches lost the request-level opt-in evidence.
  - Re-review P2: structured action failures still published raw UIA/capture `Reason` through `CreateActionFailure`, even though successor failures were sanitized.
  - Re-review P3: generated test matrix attributed cache-installed proof to integration tests, and proof metadata recorded `repoHead` without dirty/clean source provenance.
  - Re-review round 2 P2: focused fallback semantic-hint tokenization still used unbounded stack allocation over app-controlled UIA `Name` / `AutomationId`.
  - Re-review round 2 P3: `observe_after_requested` was still missing on pre-resolution state-token failure branches.
  - Re-review round 2 P2: stored-state and get_app_state live-window resolution still used `SingleOrDefault`, so duplicate strict matches could throw instead of fail-closing.
  - Re-review round 3 P2: cache-installed proof could prove cache/repo plugin-copy equality while the published runtime bundle was stale relative to latest runtime source fixes.
  - Re-review round 3 P3: this post-closure addendum still carried `in_progress` wording after the latest fix batch.
  - Re-review round 4 P2: cache-installed proof freshness still used a `src`-only input boundary and missed repo-root runtime publication inputs such as `Directory.Build.props`, `Directory.Packages.props`, `global.json`, solution/package config and analyzer config.
  - Re-review round 5 P2: cache-installed proof anchored freshness to `Okno.Server.exe` mtime instead of the runtime bundle manifest that publish/launcher already use as completion proof.
- Исправленные root causes:
  - `ComputerUseWinActionability` now limits focused fallback to `edit` or text-entry-like `document` / `custom` targets through bounded tokenized text/input/edit/query/search-box hints, preserving target-local focus proof and blocking arbitrary focused clickable controls, substring false positives and oversized UIA semantic-hint metadata.
  - `ComputerUseWinStoredStateResolver` now provides a shared post-action live-window resolver for successor observe; `ComputerUseWinActionRequestExecutor` uses it before capture/UIA and does not carry stale pre-action `windowId` into nested `successorState`.
  - `ComputerUseWinFailureCodeMapper` now maps `observation_failed` to safe product-owned fresh-observation guidance, and `ComputerUseWinToolResultFactory.CreateActionFailure` applies public failure mapping to unsafe structured action failure payloads/audit completion messages.
  - `ComputerUseWinToolResultFactory.CreateActionFailure` now sanitizes only unsafe runtime/provider classes (`observation_failed`, `unexpected_internal_failure`, `input_dispatch_failed`) while preserving product-owned validation and stale-target retry reasons such as malformed drag points and source/destination mismatch.
  - `ComputerUseWinActionRequestExecutor` now records `ObserveAfterRequested` before state resolution and before approval/failure/success branching, carrying it through state-token failures, failed pre-dispatch paths and unexpected failure fallback observability.
  - `ComputerUseWinLiveWindowSelector` centralizes 0/1/many live-window selection; stored-state resolution, successor observe and get_app_state explicit/attached target resolution now fail-close on duplicates without `SingleOrDefault` exceptions.
  - `scripts/codex/prove-computer-use-win-cache-install.ps1` records a stale-resistant cache-installed MCP proof by comparing repo/cache plugin file hashes, proving exact nine-tool tools/list schema, `allowFocusedFallback`, selected-action `observeAfter`, semantic-only absence of `observeAfter`, and `list_apps.status=ok`.
  - `scripts/codex/prove-computer-use-win-cache-install.ps1` now records `repoWorkingTreeClean`, `repoStatusShort`, runtime-affecting dirty paths, runtime bundle freshness against publication inputs, runtime bundle manifest path/write time, `publicationAcceptanceEligible`, `repoPluginDigest` and `cachePluginDigest`; generated test matrix separates integration tests from install/publication proof.
  - The proof script now uses one runtime publication input model for dirty-path classification and freshness (`src` project files plus repo-root build/analyzer/package config), validates `okno-runtime-bundle-manifest.json`, and treats manifest write time as the publication completion timestamp instead of trusting a single executable sentinel.
  - Observability/generated/product/plugin docs now describe `observe_after_requested`, `successor_state_available`, `successor_state_failure_code`, tokenized text-entry-like fallback scope, post-action live-window re-resolution and the cache-installed proof command.
- TDD применялся: `да`; RED targeted tests first failed for raw successor failure leakage, stale pre-action successor window use, arbitrary non-text focused fallback dispatch and the composed focused-fallback+observeAfter proof gap, then passed after root-cause fixes.
- Проверенные соседние paths: normal `type_text` vs fallback, `allowFocusedFallback` with/without `elementIndex`, non-text focused controls, adversarial text-marker substrings, oversized UIA names/automation ids, successor observe success/failure, pre-resolution and pre-dispatch observeAfter failure telemetry, structured action failure sanitizer plus drag validation/stale retry reasons, post-action target missing/ambiguous failure mapping, stored/get_app_state duplicate target resolution, public docs/generated/plugin skill wording and cache proof reproducibility/provenance.

### Stage 6: Telegram / Qt diagnosis and proof-fork closure

**Назначение:** зафиксировать, почему current shipped fallback не закрывает
реальный Telegram acceptance, и закрыть design fork между дополнительным
Win32 focus proof и explicit coordinate-confirmed typing model.

**Primary files / evidence owners:**

- `docs/exec-plans/active/computer-use-win-screenshot-first-hardening.md`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinActionability.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinContracts.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinStateStore.cs`
- `plugins/computer-use-win/README.md`
- cache-installed product path `C:\Users\v.vlasov\.codex\plugins\cache\computer-use-win-local\computer-use-win\0.1.0`

**Official docs to check in this stage:**

- Microsoft `GetGUIThreadInfo`:
  `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo`
- Microsoft `GUITHREADINFO`:
  `https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-guithreadinfo`
- Microsoft `GetFocus`:
  `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getfocus`
- Microsoft `GetCaretPos`:
  `https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcaretpos`
- UIA `AutomationElement.FocusedElement`:
  `https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.focusedelement?view=windowsdesktop-9.0`
- Qt accessibility for widgets / custom widgets:
  `https://doc.qt.io/qt-6/accessible-qwidget.html`
- Electron accessibility:
  `https://www.electronjs.org/docs/latest/tutorial/accessibility`
- Chrome accessibility guidance for custom controls / focusability:
  `https://developer.chrome.com/docs/lighthouse/accessibility/custom-controls-labels`
- Chrome accessibility guidance for custom controls / roles:
  `https://developer.chrome.com/docs/lighthouse/accessibility/custom-control-roles`
- WAI-ARIA `textbox` role model:
  `https://www.w3.org/TR/wai-aria-1.3/`

**Existing product evidence from this reopen pass (`2026-04-30`):**

- `list_apps` product path sees `telegram` as ordinary approved app with
  `windowId=cw_fae6a7486cb14e47bf786fb0aabe1ee0`.
- `get_app_state(windowId, maxNodes=160)` and retry with `maxNodes=512`
  publish only top-level window, title bar, system menu and generic groups; no
  usable editable child appears.
- Product navigation still succeeds:
  `click(point=33,139, observeAfter=true)` opens `Польза`,
  `click(point=185,125, observeAfter=true)` opens `Рабочий👷‍♂️`.
- Product input still fails:
  `click(point=386,805, observeAfter=true)` visibly targets the input area, but
  `type_text(... allowFocusedFallback=true, confirm=true, observeAfter=true)`
  returns `unsupported_action` with
  `allowFocusedFallback без elementIndex требует ровно один focused target-local element`.
- `set_value(elementIndex=17, valueKind=text)` on the right pane reproduces
  `input_dispatch_failed`; this remains a semantic/UIA path and is not itself
  the acceptance target for this follow-up.
- Low-level Win32 diagnostic after the input click shows:
  foreground window = Telegram top-level window,
  `hwndFocus == hwndActive == top-level Qt51518QWindowIcon`,
  `hwndCaret == 0`.

**Cross-app class taxonomy to close by root cause, not by app name:**

- **Class A: semantic editable surface**
  UIA or platform semantics already expose a real editable control (`ValuePattern`,
  `TextPattern`, `edit`, proper textbox role). This class is already covered by
  existing `set_value` / normal `type_text` paths and must not regress.
- **Class B: weak child-focus surface**
  No writable semantic proof, but there is still a unique target-local child
  focus or caret signal after the click. Candidate solution: an additional
  honest focus-proof branch inside `allowFocusedFallback`.
- **Class C: top-level-only focus, no caret, fresh coordinate evidence**
  After the click the app keeps focus only on the top-level window and exposes
  no child/caret boundary, while the operator still has fresh screenshot/click
  geometry inside the same observed window. Telegram/Qt currently reproduces
  this class. Candidate solution: explicit coordinate-confirmed typing, not a
  fake child-focus proof.
- **Class D: accessibility tree disabled, deferred or only partially exposed**
  Common in Electron/Chromium/custom web content: the semantic tree may become
  richer only after accessibility support is enabled, focus enters the web
  area, or the app uses proper roles/labels. Product policy here is to
  diagnose and classify honestly, not to inject hidden app-specific toggles by
  default.
- **Class E: canvas / owner-drawn / virtualized text surface with no honest locality proof**
  No usable UIA target, no child focus/caret boundary, and no safe coordinate
  proof contract yet. This class must remain unsupported until a new explicit
  contract is designed.
- **Class F: delayed focus-settle / field-editor indirection**
  A first click lands on a container, and the real text target appears only
  after a bounded focus settle or follow-up reobserve. The fix here is bounded
  settle/reobserve, not blind typing or hidden retries without proof.

**Root-cause design goal for the reopen:**

- solve the family `screenshot-first navigation reaches a plausible text-entry
  target, but current proof model is too narrow to type safely`;
- do not solve this by app-specific Telegram hacks;
- do not broaden the contract so far that it degenerates into “type into
  whatever is focused”.

**Design decision gate:**

- If Stage 6 proves an honest target-local focus boundary beyond current UIA
  subtree, Stage 7 may add a second proof branch inside existing
  `allowFocusedFallback`.
- If Stage 6 confirms that Telegram still exposes only top-level focus and no
  caret/child focus boundary, Stage 7 must not pretend that a stronger focus
  proof exists; it must either:
  - add an explicit coordinate-confirmed typing model inside `type_text`, or
  - record that Telegram requires a separate follow-up beyond the current
    contract.
- Whatever branch is chosen must be justified against the class taxonomy above,
  so that the resulting contract covers a reusable root-cause family
  (`B`, `C`, possibly `F`) rather than only one Telegram symptom.

**Steps:**

- [x] Re-run the Telegram product diagnosis through cache-installed
  `computer-use-win`, not repo-internal seams, and save the proof artifact
  paths in the stage report.
- [x] Compare three signals after the input click:
  UIA focused element, Win32 GUI-thread focus/caret, and current
  `type_text` eligibility.
- [x] Map the observed behavior onto classes `A-F` above and record which
  classes are already covered, which are newly targeted, and which must remain
  unsupported.
- [x] Close the fork:
  `additional honest focus branch` vs `explicit coordinate-confirmed typing`.
- [x] Decide whether bounded focus-settle / reobserve is required as a
  cross-cutting mitigation for class `F`, and if yes keep it narrow and
  explicit.
- [x] Reject any design that relies on hidden last-click reuse, blind active
  window typing, clipboard/paste, OCR or screenshot text detection.
- [x] Fill stage report, run review/re-review and commit the decision before
  Stage 7 implementation starts.

**Acceptance criteria:**

- [x] Root cause is explicit: current Telegram failure is classified as
  `missing target-local proof`, not as generic navigation or selector failure.
- [x] The follow-up contract fork is frozen before code changes.
- [x] If top-level-only focus/no-caret remains true, Stage 7 does not claim a
  fake child-focus proof.
- [x] The resulting Stage 6 decision explicitly states which classes from
  `A-F` Stage 7 is expected to close and which remain intentionally out of
  scope.
- [x] The exact Telegram acceptance target is explicit in the report:
  reach `Польза -> Рабочий👷‍♂️`, make `Тест MPC` visible in the input field,
  then execute a separate explicit send step through the plugin MCP surface.

#### Отчёт этапа

- Статус этапа: `approved`
- Branch: `codex/computer-use-win-screenshot-first-hardening`
- Commit SHA: `e13ff61`
- TDD применялся: нет, Stage 6 является proof-fork/diagnosis gate без runtime behavior changes.
- Проверки:
  - `scripts/codex/bootstrap.ps1`
  - cache-installed `computer-use-win` `list_apps`
  - cache-installed `computer-use-win` `get_app_state(windowId, maxNodes=512)`
  - cache-installed `computer-use-win` coordinate click probe against Telegram input area
  - Win32 foreground GUI-thread probe via `GetForegroundWindow`, `GetWindowThreadProcessId`, `GetGUIThreadInfo`
- Proof artifacts:
  - `.tmp/.codex/telegram-stage6-proof/proof-20260501-telegram-class-c.json`
- Review agents:
  - `Boole` / `019de3a6-02ee-71a1-bca1-42588033cf63`: Stage 6 static review found process/provenance wording issues.
  - `Nietzsche` / `019de3a6-04cb-7c72-8930-df5f1fd8184a`: Stage 6 static review found premature gate closure and acceptance-boundary wording issues.
  - `Planck` / `019de3a6-0648-7ac0-b710-cada9e44ccaa`: Stage 6 static review found diagnosis-vs-fix wording issue.
  - Final re-review: all three agents returned `approve` after stale branch/status/smoke/acceptance/proof wording was corrected.
- Subagent context mode: `explicit_prompt_only` / `fork_context=false`
- Official docs checked:
  - Microsoft `GetGUIThreadInfo`
  - Microsoft `GUITHREADINFO`
  - Microsoft `GetFocus`
  - Microsoft `GetCaretPos`
  - Qt accessibility for custom widgets
- Reference repos checked:
  - не требовались для Stage 6 decision gate; existing source-pack conclusions remain unchanged.
- Подтверждённые замечания:
  - Telegram/Qt remains Class `C`: UIA publishes only top-level window/menu/generic groups; no usable editable child is available.
  - Win32 GUI-thread proof shows top-level-only focus: `hwndFocus == hwndActive == Telegram top-level Qt51518QWindowIcon`, `hwndCaret == 0`.
  - Current `allowFocusedFallback` correctly fail-closes because it requires a target-local focused child and no such child exists.
- Отклонённые замечания:
  - This is not a navigation, selector or `observeAfter` regression; the remaining blocker is text-entry locality proof.
  - This must not be solved by pretending Telegram has child-focus proof.
- Замороженное решение / выбранная ветка:
  - Stage 6 freezes the root-cause branch before code: implement explicit coordinate-confirmed typing inside `type_text`.
- Проверенные соседние paths:
  - Class `A` semantic editable path remains owned by `set_value` / normal focused editable `type_text`.
  - Class `B` weak focused child path remains owned by existing `allowFocusedFallback`.
  - Class `F` bounded focus-settle/reobserve is not selected by Stage 6 and remains deferred; Stage 7 must not add hidden settle/retry behavior.
  - Class `D` accessibility enablement/toggles and Class `E` no-locality-proof surfaces remain out of scope.
- Остаточные риски:
  - Coordinate-confirmed typing still proves dispatch and locality, not semantic text outcome; public result must remain `verify_needed`.
  - Real Telegram product acceptance can still fail because of runtime/input environment and must be proven through cache-installed plugin in Stage 8.
- Разблокировка следующего этапа:
  - Stage 7 may implement only Branch `B` from this report: explicit coordinate-confirmed typing, with explicit request point/geometry proof and no hidden last-click reuse.
  - Stage 6 is approved for decision commit; after commit, Stage 7 may start.

### Stage 7: Cross-app poor-UIA text-entry implementation

**Назначение:** реализовать самый узкий честный proof branch, который закрывает
не только Telegram/Qt, а корневой family poor-UIA text-entry gaps без
разрушения shipped safety model.

**Primary files:**

- `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs`
- `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextContract.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextHandler.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinActionability.cs`
- one narrow new owner if Stage 6 requires it:
  Win32 focus probe owner or coordinate-confirmed typing owner
- `tests/WinBridge.Runtime.Tests/*`
- `tests/WinBridge.Server.IntegrationTests/*`
- `tests/WinBridge.SmokeWindowHost/Program.cs`

**Contract guardrails:**

- no new `click_and_observe`-style tools;
- no clipboard/paste default;
- no OCR-lite or screenshot text detection;
- no blind typing into whichever window is currently foreground;
- no hidden reuse of the previous click; the weaker path must carry explicit
  request point/geometry proof that runtime revalidates against the current
  observed window;
- `confirm=true` remains mandatory for the weaker path;
- top-level success stays `verify_needed`;
- `observeAfter=true` remains additive and does not upgrade semantic certainty.

**Implementation branch selected by Stage 6:**

- Selected branch: explicit coordinate-confirmed typing model inside `type_text`,
  only if the request carries explicit point/geometry proof and the runtime
  revalidates it against the current observed window; this branch must not rely
  on opaque hidden memory of the previous click alone.
- Rejected branch for this reopen: additional honest focus proof inside current
  `allowFocusedFallback`, because Stage 6 evidence shows top-level-only Qt focus
  with no child/caret boundary.
- Deferred branch: bounded focus-settle/reobserve for Class `F`; do not add it
  in Stage 7 without a new explicit stage decision.

**Class coverage target:**

- preserve Class `A`;
- close reusable root-cause family `C`;
- preserve existing Class `B` coverage without broadening it;
- defer Class `F` settle/reobserve;
- do not pretend to close `D` or `E` unless Stage 6 evidence actually proves
  that the same branch covers them honestly.

**Steps:**

- [ ] TDD RED for the Telegram-like Class `C` case where UIA shows no usable
  editable child after a successful input click.
- [ ] TDD RED for at least one non-Telegram representative of the same
  root-cause family, using helper/integration fixtures rather than only naming
  Telegram in tests.
- [ ] TDD RED for stale geometry / wrong target / missing `confirm` /
  missing explicit proof on the chosen weaker branch.
- [ ] If Stage 6 selected a Class `F` settle/reobserve mitigation, add RED
  tests for bounded focus-settle timing and fail-close behavior.
- [ ] Implement the chosen Stage 6 branch without widening normal
  `type_text` semantics.
- [ ] Add safe observability markers that distinguish the new branch from the
  existing focused fallback, without leaking raw points or raw text.
- [ ] Extend helper/integration coverage so the exact proof class is tested,
  not only easier surrogate controls, and the branch is named/documented by
  proof class rather than by Telegram.
- [ ] Refresh generated docs and product docs only after GREEN runtime and
  public handler proof.
- [ ] Run review/re-review and commit.

**Acceptance criteria:**

- [ ] The chosen branch is documented and tested as a root-cause-family
  solution, not as a Telegram-only exception.
- [ ] Telegram-like Class `C` no-child-focus case no longer fails only because
  the current `focused target-local element` gate is too narrow.
- [ ] At least one additional representative of the same class family is
  covered by tests or helper proof.
- [ ] No hidden typing into arbitrary active windows is introduced.
- [ ] No clipboard/OCR/browser-preview work is added.
- [ ] Public tool count remains unchanged.
- [ ] Action result remains conservative (`verify_needed`) on the weaker path.

### Stage 8: Real Telegram product acceptance and re-closure

**Назначение:** доказать, что reopened workstream closes the real product case
through the plugin MCP surface, not only through helper fixtures.

**Steps:**

- [ ] Refresh/publish the plugin-local runtime bundle and synchronize the
  cache-installed copy used by ordinary agents.
- [ ] Run the real product scenario through cache-installed
  `computer-use-win`:
  `list_apps -> get_app_state -> click(Pольза) -> click(Рабочий👷‍♂️) -> type_text(...)`
  and prove that `Тест MPC` is visible in the input field before any send step.
- [ ] After visible text proof passes, run a separate explicit send step
  (`press_key(Enter)` or equivalent) and record its outcome separately from the
  coordinate-confirmed typing proof.
- [ ] Save a proof artifact that includes tool-level outcomes, image-bearing
  successor state evidence and cache-installed provenance.
- [ ] Record a class-coverage matrix in the stage report:
  which of `A-F` are now covered, which remain intentionally unsupported, and
  which require future separate work.
- [ ] Rerun full sequential contour:
  `scripts/build.ps1 -> scripts/test.ps1 -> scripts/smoke.ps1 -> scripts/refresh-generated-docs.ps1 -> scripts/codex/verify.ps1`.
- [ ] Run full-branch review against `main` with the same two `gpt-5.5`
  review roles and explicit-prompt/no-fork context.
- [ ] Archive this active reopened plan back to `completed` only after the real
  Telegram acceptance target passes or after an explicit user-approved closure
  decision narrows the target.

**Acceptance criteria:**

- [ ] Real Telegram scenario passes as a product acceptance signal, not only
  helper smoke.
- [ ] Closure report names the solved root-cause family, not only the Telegram
  app name.
- [ ] Docs clearly distinguish:
  semantic editable path,
  focused fallback path,
  any new coordinate-confirmed or settle/reobserve path,
  and still-unsupported classes.
- [ ] The closure report records two ordered outcomes:
  first, visible text proof (`Тест MPC` is present in the Telegram input field);
  second, the separate explicit send-step result. Coordinate-confirmed typing is
  accepted only by the visible-text proof, while the send step is reported as an
  additional product outcome.
- [ ] Cache-installed proof and docs reflect the true acceptance boundary.
- [ ] Residual risks are explicit and no new broad workstream is smuggled into
  the branch.

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

- extend helper app with one Class `C` poor-UIA typing target that visually accepts text after explicit point/geometry targeting but does not expose focused child/caret or current writable UIA proof;
- keep existing semantic textbox for current `set_value` / `type_text` path so new fallback does not erase the old proof-backed route;
- keep the existing Class `B` focused weak-child target so the current `allowFocusedFallback` path does not regress;
- add one explicit smoke story for `observeAfter=true` with post-action updated screenshot;
- keep current drag/scroll/selectors smoke stories intact.

## 12. Smoke strategy

### New smoke stories to add

1. Class `C` coordinate-confirmed typing:
   `get_app_state -> type_text(point, coordinateSpace=capture_pixels, allowFocusedFallback=true, confirm=true, observeAfter=true) -> successorState/get_app_state`
   and assert that the helper mirror shows the typed text without relying on focused child/caret proof.
2. `get_app_state -> click(..., observeAfter=true)` and assert:
   updated screenshot image block exists,
   successor state payload exists,
   new `stateToken` is usable on the next action,
   `refreshStateRecommended=false` because fresh state is already embedded in the action result.
3. repeated `list_apps` on an unchanged helper window should reuse the same `windowId`.
4. helper window drift that changes discovery proof should force a new selector or null publication rather than silent reuse.

### Helper requirements

- add one top-level-only or owner-drawn Class `C` target that visually mirrors typed text after coordinate-confirmed typing but does not publish focused child/caret or strong writable UIA proof;
- keep the existing focusable weak-child control as Class `B` regression coverage;
- keep deterministic mirror labels so smoke can verify visible outcome without OCR.

### Reopened product acceptance proof

- helper smoke remains necessary but no longer sufficient for closure;
- reopened closure requires one cache-installed real-product proof against
  Telegram through the plugin MCP surface, because the remaining gap is
  explicitly Qt/poor-UIA text entry rather than generic screenshot-first
  navigation.
- Telegram proof is ordered: first prove visible `Тест MPC` in the input field,
  then record the separate explicit send-step result.

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

1. Treat stages `0-5` as historical evidence; reopened execution starts at `Stage 6`.
2. Stage 6 must close the proof fork before any new behavior lands: either honest additional focus proof or explicit coordinate-confirmed typing.
3. Stage 7 may implement only the branch frozen by Stage 6; do not mix a focus-probe experiment and a coordinate-confirmed typing contract in the same commit without an explicit stage-report reason.
4. Extend helper/integration coverage only after the exact Telegram-like RED case is written down.
5. Keep cache-installed product proof separate from helper smoke; Stage 8 must run both.
6. Run the full sequential contour only after Stage 7 is green.
7. Re-close the plan only in Stage 8 after real Telegram product acceptance or an explicit user-approved narrowing of that acceptance target.

## 16. Decision summary

- `type_text` fallback: same tool, explicit `allowFocusedFallback`, mandatory `confirm`, default `verify_needed`; `elementIndex == null` may be the narrowest `v1`, but the plan does not freeze that as the only valid fallback shape.
- action+observe: same tools, explicit `observeAfter`, shared post-action observer, nested `successorState`, additive image content; successful successor observe satisfies immediate refresh need even if top-level status remains `verify_needed`.
- continuity UX: reduce selector churn by reusing `windowId` only across repeated strict discovery matches; no public `hwnd + processId` selector.
- screenshot preview: runtime already correct; treat preview as client/operator UX unless an additive hint is strictly needed.
- reopened fork closed by Stage 6: Telegram gives top-level Qt focus with no target-local focused child and no caret, so Stage 7 implements explicit coordinate-confirmed typing inside `type_text` instead of fake child-focus proof.
- reopened root-cause target: close reusable Class `C` poor-UIA text-entry failures (`top-level-only focus with fresh coordinate evidence`), preserve existing Class `A`/`B` paths, defer Class `F` bounded settle/reobserve, and keep Class `D`/`E` out of scope without collapsing into blind active-window typing.
