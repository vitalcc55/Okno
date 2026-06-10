# ExecPlan: computer-use-win observation completeness / deep semantic lookup

Branch: `codex/computer-use-win-observation-completeness-plan`
Status: `planned`
Created: `2026-06-10`
Recommended implementation branch: `codex/computer-use-win-observation-completeness`

## 1. Goal

Разобрать и поставить отдельный bounded product slice для defect family в public
`computer-use-win` observation/action loop:

- `get_app_state` сейчас слишком жестко связывает visual observation и semantic readiness;
- compact preview tree сейчас местами подменяет semantic reachability;
- state failure materialization расходится с action failure materialization;
- public state/action model не публикует достаточно completeness/proof metadata, чтобы агент мог отличить "элемент реально отсутствует" от "элемент не попал в preview".

Это не "починить Karing" и не "починить Qt". Это новый slice про честный public
observation contract и bounded semantic lookup lane поверх уже shipped
`computer-use-win` loop.

Verdict for placement: это новый explicit R2 slice, а не часть текущего
`computer-use-win physical execution policy hardening` slice `13`.

## 2. Non-goals

- Не писать implementation в planning wave.
- Не расширять текущую ветку slice `13` этим defect family.
- Не переключать `get_app_state` глобально на raw tree dump.
- Не поднимать `UiaSnapshotDefaults.Depth` глобально как единственное решение.
- Не превращать `get_app_state` в OCR/browser/region-capture subsystem.
- Не ломать existing `stateToken + elementIndex` contract.
- Не делать `windows.uia_action` раньше отдельного roadmap slice.
- Не утверждать, что live Karing/R130SH являются единственным acceptance floor.
- Не claim-ить, что текущий green proof-smoke доказывает deep Qt/PySide6 semantic usability.

## 3. Current repo state relevant to the findings

По текущему repo state:

- `computer-use-win` public surface уже shipped как quiet loop:
  `list_apps -> get_app_state -> action -> verify` plus optional
  `observeAfter=true` successor state.
- Phase-1 physical policy уже shipped: public action results несут
  `executionFacts` и различают `semantic`, `expected_physical`,
  `fallback_physical`.
- `observeAfter=true` уже shipped для selected actions и возвращает nested
  `successorState` + screenshot image block при успешном post-action observe.
- `type_text` poor-UIA fallback уже shipped через explicit
  `allowFocusedFallback=true` + `confirm=true`, focused proof или
  `capture_pixels` point proof.
- strict `windowId` continuity reuse уже shipped и не должен быть ослаблен этим
  slice.
- `get_app_state` success сейчас требует оба слоя сразу: successful screenshot
  capture и successful UIA snapshot.
- Public `get_app_state` принимает `maxNodes`, но не принимает `depth`.
- `UiaSnapshotDefaults.Depth` сейчас равен `3`.
- Internal `windows.uia_snapshot` уже публикует completeness metadata:
  requested depth/maxNodes, realized depth, node count, truncation и boundary flags.
- Public `computer-use-win` state envelope хранит только requested depth/maxNodes
  и не переносит realized completeness в result/stateToken.
- Semantic action selectors сейчас завязаны на `elementIndex`; `set_value`,
  `scroll(elementIndex)`, `perform_secondary_action` и обычные semantic
  revalidation paths не имеют public selector по `AutomationId`.
- `observeAfter` reuse-ит тот же `ComputerUseWinAppStateObserver`, поэтому
  наследует все ограничения current state observation lane.

Source of truth checked in this planning wave:

- [okno-roadmap.md](../../product/okno-roadmap.md)
- [computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [observability.md](../../architecture/observability.md)
- [computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
- [project-interfaces.md](../../generated/project-interfaces.md)
- [test-matrix.md](../../generated/test-matrix.md)
- [computer-use-win-physical-execution-policy-hardening.md](../active/computer-use-win-physical-execution-policy-hardening.md)
- [completed-2026-05-14-computer-use-win-physical-policy-phase-1.md](../completed/completed-2026-05-14-computer-use-win-physical-policy-phase-1.md)

## 4. Current shipped invariants to preserve

- `get_app_state` and successful `observeAfter=true` remain image-bearing MCP
  results, not path-only metadata wrappers.
- `stdout` remains MCP-only; diagnostics stay in structured payloads,
  stderr, audit events and artifacts.
- `stateToken` remains short-lived observation proof, not a stable global
  target id or lease.
- `elementIndex` remains compact preview selector for backward compatibility.
- Semantic-only actions must fail closed when their semantic target cannot be
  proven.
- Physical fallback paths must remain explicit, confirmed where risky, and
  marked as `verify_needed` unless accepted proof exists.
- Raw provider/UIA exception text must not become public contract wording.
- `observeAfter` must not rewrite top-level action outcome; successor observe is
  advisory proof/enrichment.
- `list_apps` and `get_app_state` side effects around selector issuance,
  approval and activation remain explicit in metadata and docs.
- Public tool count should remain stable unless a later architecture decision
  proves a new public tool is necessary.

## 5. Exact source pack

Local product and policy:

- [AGENTS.md](../../../AGENTS.md)
- [index.md](../../product/index.md)
- [okno-spec.md](../../product/okno-spec.md)
- [okno-vision.md](../../product/okno-vision.md)
- [okno-roadmap.md](../../product/okno-roadmap.md)
- [computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [observability.md](../../architecture/observability.md)
- [openai-computer-use-interop.md](../../architecture/openai-computer-use-interop.md)
- [reference-research-policy.md](../../architecture/reference-research-policy.md)
- [capability-design-policy.md](../../architecture/capability-design-policy.md)

Current implementation:

- [ComputerUseWinAppStateObserver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs)
- [ComputerUseWinGetAppStateHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs)
- [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs)
- [ComputerUseWinToolResultFactory.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs)
- [ComputerUseWinObservationFailureTranslator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinObservationFailureTranslator.cs)
- [ComputerUseWinStateStore.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStateStore.cs)
- [ComputerUseWinAccessibilityProjector.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAccessibilityProjector.cs)
- [ComputerUseWinFreshElementResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinFreshElementResolver.cs)
- [ComputerUseWinActionRequestExecutor.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs)
- [ComputerUseWinToolRegistration.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs)
- [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)
- [AutomationSnapshotNode.cs](../../../src/WinBridge.Runtime.Windows.UIA/AutomationSnapshotNode.cs)
- [UiaSnapshotTreeBuilder.cs](../../../src/WinBridge.Runtime.Windows.UIA/UiaSnapshotTreeBuilder.cs)
- [Win32UiAutomationBackend.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationBackend.cs)
- [UiaSnapshotDefaults.cs](../../../src/WinBridge.Runtime.Contracts/UiaSnapshotDefaults.cs)
- [UiaSnapshotToolResult.cs](../../../src/WinBridge.Runtime.Contracts/UiaSnapshotToolResult.cs)

Current tests:

- [ComputerUseWinObservationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs)
- [ComputerUseWinActionAndProjectionTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs)
- [UiaSnapshotTreeBuilderTests.cs](../../../tests/WinBridge.Runtime.Tests/UiaSnapshotTreeBuilderTests.cs)
- [WindowUiaSnapshotToolTests.cs](../../../tests/WinBridge.Server.IntegrationTests/WindowUiaSnapshotToolTests.cs)

Official docs used:

- OpenAI: [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- OpenAI: [Images and vision](https://developers.openai.com/api/docs/guides/images-vision)
- OpenAI: [Codex app on Windows](https://developers.openai.com/codex/app/windows)
- MCP: [Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- MCP: [Schema](https://modelcontextprotocol.io/specification/2025-11-25/schema)
- MCP: [Lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)
- MCP: [Transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
- MCP: [Security best practices](https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices)
- Microsoft: [UI Automation Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)
- Microsoft: [UI Automation Tree Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview)
- Microsoft: [Obtaining UI Automation Elements](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-obtainingelements)
- Microsoft: [TreeWalker.GetFirstChild](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treewalker.getfirstchild?view=windowsdesktop-10.0)
- Microsoft: [TreeWalker.ControlViewWalker](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treewalker.controlviewwalker?view=windowsdesktop-10.0)
- Microsoft: [TreeWalker.RawViewWalker](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.treewalker.rawviewwalker?view=windowsdesktop-10.0)
- Microsoft: [IUIAutomation::ElementFromHandle](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfromhandle)
- Microsoft: [IUIAutomation::ElementFromHandleBuildCache](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfromhandlebuildcache)
- Microsoft: [UI Automation Error Codes](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-error-codes)

## 6. Official constraints

OpenAI constraints:

- Computer use is screenshot-first: the model inspects current UI screenshots,
  the harness executes actions, then returns an updated screenshot for the next
  decision.
- Custom harnesses are first-class when a product already has mature execution,
  observability and guardrails; Okno should not rebuild itself around a second
  runtime just to match built-in computer use.
- Screenshot-bearing state is first-class input, not only artifact metadata.
- For computer-use screenshots, high fidelity matters; if screenshots are
  downscaled, coordinate remapping back to original geometry is part of the
  harness contract.
- Human-in-the-loop confirmation belongs to product design and should happen at
  the point of risk.

MCP constraints:

- Tool results can carry `structuredContent` and `content` together.
- Image content is a valid tool content block.
- Tool execution failures should be returned as tool results with `isError=true`
  where the model can self-correct; malformed protocol/tool lookup failures are
  the protocol-error lane.
- Tool outputs should be sanitized; clients should treat tool annotations as
  untrusted unless the server is trusted.
- STDIO transport reserves stdout for valid MCP messages; logging belongs on
  stderr or artifacts.

Microsoft UIA constraints:

- UIA exposes a tree, but that tree is view-filtered and mutable.
- Raw, control and content views are intentionally different; control view is a
  subset of raw view.
- `ControlViewWalker` can legitimately skip children that do not match the
  current view condition.
- `FindAll(TreeScope.Descendants, TrueCondition)` is closer to raw descendant
  discovery and can walk hundreds or thousands of elements; using it must be
  bounded by scope, budget and starting point.
- `ElementFromHandle` gives an element for the specified window handle; it does
  not by itself prove deep descendant reachability.
- UIA provider failures such as element-not-available, invalid operation,
  no-clickable-point and not-supported are normal runtime failure classes and
  should be mapped to product-owned public wording.

## 7. Findings synthesis: root causes vs symptoms

Root causes:

1. Public observation conflates visual state and semantic readiness.
   `ComputerUseWinAppStateObserver` captures screenshot first, then requires UIA
   snapshot `done` before returning success. This makes a usable visual state
   fail if semantic preview is incomplete.

2. Public semantic reachability is tied to compact preview tree.
   `AutomationSnapshotNode` uses `ControlViewWalker` and
   `Automation.ControlViewCondition`; `ComputerUseWinAccessibilityProjector`
   flattens only that tree into `elementIndex` selectors.

3. Public state lacks completeness metadata.
   `UiaSnapshotToolResult` has realized depth/node/truncation/boundary flags,
   but `ComputerUseWinGetAppStateResult` and `ComputerUseWinStoredState` do not
   publish/carry them.

4. Public action selectors have no bounded semantic selector lane beyond
   `elementIndex`.
   Actions can revalidate a preview element by `ElementId` or fallback
   `ControlType + Name + AutomationId`, but they cannot search deeper by a
   stable selector if the target never entered the preview tree.

5. State failure materialization lacks the same public mapper/redactor discipline
   as action failure materialization.
   Action failures sanitize selected structured reasons through
   `ComputerUseWinFailureCodeMapper`; state failures can forward
   `snapshot.Reason` or capture exception messages directly as public reason.

6. `observeAfter` inherits the same observation model.
   It calls the same app-state observer, then maps failure for advisory
   `successorStateFailure`; this is better sanitized than initial state, but it
   still cannot return visual successor state when UIA is incomplete.

Symptoms:

- Karing/Flutter `get_app_state` failure is a symptom of root causes `1` and `5`.
- R130SH/PySide6 deep-tree miss is a symptom of root causes `2`, `3` and `4`.
- Focused fallback awkwardness is a downstream symptom of root causes `1`, `2`
  and `3`; it should not be patched with hidden retry or blind focus trust.
- Existing green targeted tests are not proof that the public model is sufficient
  for poor-UIA/deep-tree targets; they pin current behavior and miss this
  defect family.

## 8. Design forks to close

Closed recommendations for the implementation plan:

1. `get_app_state` should be allowed to return successful visual observation
   when screenshot capture succeeds and UIA preview is incomplete or unavailable,
   provided target/window identity remains product-valid.

2. Do not introduce `status=partial` as a top-level state status in the first
   implementation wave. Keep top-level `status=ok` for visual observation
   success and publish semantic readiness/completeness as explicit nested
   metadata plus warnings.

3. Add a product-owned semantic/completeness envelope to public state, not raw
   provider text. Recommended shape:

   ```text
   semanticPreview.status = complete | incomplete | unavailable | failed
   semanticPreview.view = control
   semanticPreview.requestedDepth
   semanticPreview.requestedMaxNodes
   semanticPreview.realizedDepth
   semanticPreview.nodeCount
   semanticPreview.truncated
   semanticPreview.depthBoundaryReached
   semanticPreview.nodeBudgetBoundaryReached
   semanticPreview.failureCode
   ```

   `failureCode` is product-owned. Raw reason stays in diagnostics/audit only.

4. Do not expose public `depth` control in the first wave unless tests prove
   that selector lookup cannot be solved without it. The preferred first design
   is a bounded semantic lookup lane, not a larger preview tree.

5. Deep semantic lookup should start as an internal lane behind current public
   action tools and additive selector shapes, not as a new public tool.

6. Stable targeting beyond `elementIndex` should use a bounded selector object:
   `automationId + controlType + optional name`, with optional `frameworkId`
   only if characterization proves it reduces ambiguity for Qt/PySide6 without
   overfitting.

7. State and action failure materialization should share one public failure
   materializer/redactor owner. State failures should not bypass it.

8. `observeAfter` must inherit visual-vs-semantic split and completeness metadata.
   A committed action can have successful visual successor state even if semantic
   preview is incomplete.

9. `allowFocusedFallback` should treat preview-incomplete state as low-confidence
   visual state, not as a reason to invent hidden focus trust. It may still
   require explicit confirm/focus proof depending on action path.

10. The slice must not become a raw-tree dump, broad OCR workaround or new
    browser/Electron lane.

Unresolved forks for implementation owner:

- Exact public field names for semantic completeness envelope.
- Whether to include `semanticPreview.reason` at all; default recommendation is
  no public free-form provider reason, only product-owned code + warning.
- Whether `semanticPreview.status=failed` should still publish an empty
  `accessibilityTree` or omit it; default recommendation is empty array for
  model simplicity, with explicit status.
- Whether optional `frameworkId` belongs in first selector shape.
- Whether selector lane should search control view first, then bounded raw view,
  or run one explicit raw scoped lookup for selector-only paths.
- Exact budget knobs: max descendant count, max depth, timeout, max matches.
- Whether `set_value` should be the first action to receive selector targeting,
  or whether all semantic actions should receive the additive selector branch in
  one package.

## 9. File-level integration map

| Area | Files | Expected future responsibility |
| --- | --- | --- |
| Public state contract | `ComputerUseWinContracts.cs` | Add additive semantic/completeness envelope to `ComputerUseWinGetAppStateResult`; keep backward-compatible existing fields. |
| State observation owner | `ComputerUseWinAppStateObserver.cs` | Split visual capture success from semantic preview readiness; build partial state when capture succeeds; collect sanitized semantic status. |
| State finalization | `ComputerUseWinGetAppStateFinalizer.cs` | Commit state only after successful visual materialization; include image block; preserve `isError=false` for visual success. |
| Failure materialization | `ComputerUseWinToolResultFactory.cs`, `ComputerUseWinFailureCodeMapper.cs`, `ComputerUseWinObservationFailureTranslator.cs`, `ComputerUseWinFailureDetails.cs` | Introduce shared public failure materializer for state/action/successor paths; raw reasons audit-only. |
| State storage | `ComputerUseWinStateStore.cs` | Carry realized semantic completeness through `stateToken`, not only requested params. |
| Public schema | `ComputerUseWinToolRegistration.cs` | Keep `get_app_state` schema narrow; add selector schema to semantic actions only if chosen. |
| Preview projection | `ComputerUseWinAccessibilityProjector.cs` | Continue compact preview tree; surface completeness separately; do not dump raw tree. |
| Fresh revalidation | `ComputerUseWinFreshElementResolver.cs` | Keep preview-element revalidation; use selector lane when no preview element exists or when selector branch is used. |
| Deep lookup runtime | `Win32UiAutomationBackend.cs`, `AutomationSnapshotNode.cs`, `UiaSnapshotTreeBuilder.cs`, possible new `UiAutomationSemanticLookupService` | Add bounded selector lookup over current window with explicit view/budget policy. |
| UIA contracts | `UiaSnapshotRequest.cs`, `UiaSnapshotToolResult.cs`, `UiaSnapshotDefaults.cs` | Preserve low-level snapshot contract; reuse completeness metadata; avoid global default-depth bump. |
| Action coordinators | `ComputerUseWinSetValueExecutionCoordinator.cs`, `ComputerUseWinScrollExecutionCoordinator.cs`, `ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs`, `ComputerUseWinTypeTextExecutionCoordinator.cs`, `ComputerUseWinClickExecutionCoordinator.cs` | Consume semantic completeness and optional selector lane; fail closed for semantic-only actions when selector is absent/ambiguous/unreachable. |
| Successor observation | `ComputerUseWinActionRequestExecutor.cs`, `ComputerUseWinActionSuccessorObservation.cs` | Reuse visual-vs-semantic split and publish successor completeness. |
| Observability | `ComputerUseWinActionObservability.cs`, `ComputerUseWinAuditDataBuilder.cs`, `observability.md` | Add safe completeness/failure markers without raw provider text. |
| Generated docs | `ToolDescriptions.cs`, `ToolContractManifest.cs`, `scripts/refresh-generated-docs.ps1`, `docs/generated/*` | Sync changed public contract and test matrix after implementation. |
| Tests | listed test files plus new helper fixtures | Add missing owner-layer tests and characterization cases. |

## 10. Delivery packages

### Package A: defect-family confirmation

Scope:

- Validate supplied Karing/R130SH findings against current code, tests and docs.
- Add characterization tests that prove current defect family before changing
  behavior.
- Separate root causes from downstream symptoms.
- Confirm roadmap placement and update this plan if implementation evidence
  changes the placement.

Acceptance:

- Red characterization exists for state failure sanitization.
- Red characterization exists for screenshot-success/UIA-incomplete public state.
- Red characterization exists for preview-incomplete vs semantic lookup.
- No production behavior changed yet.

### Package B: state failure sanitization

Scope:

- Introduce one public failure materializer/redactor owner for state and action
  paths.
- Route `ComputerUseWinAppStateObserver` snapshot/capture/UIA failures through
  product-owned mapping.
- Keep raw provider/UIA text in audit/diagnostics only.
- Preserve current `failureCode` taxonomy unless a new code is required by
  tests.

Acceptance:

- Initial `get_app_state` failure with raw UIA/provider reason does not leak the
  raw reason.
- `successorStateFailure` keeps current sanitization behavior.
- Action failure tests continue to pass.

### Package C: visual observation vs semantic readiness split

Scope:

- Make screenshot-backed visual observation success first-class.
- Publish semantic preview readiness/completeness metadata.
- Store completeness envelope in state token.
- Keep semantic-only actions fail-closed when semantic proof is unavailable.

Acceptance:

- `get_app_state` returns `status=ok`, image content and stateToken when capture
  succeeds but UIA preview is incomplete/unavailable.
- Payload explicitly tells the agent semantic preview is incomplete/unavailable.
- `accessibilityTree` is empty or partial by design, not silently authoritative.
- Failed capture remains `isError=true` observation failure.
- State commit still happens only after successful visual materialization.

### Package D: deep semantic lookup / selector lane

Scope:

- Add bounded selector model beyond preview `elementIndex`.
- Implement lookup scoped to current target window.
- Prefer `automationId + controlType + optional name`; fail on zero or multiple
  matches.
- Keep lookup bounded by depth/node/time/match budgets and diagnostics.
- Integrate first with semantic-only actions, with `set_value` as the likely
  first proof path.

Acceptance:

- A target absent from compact preview but present in bounded lookup can be
  resolved by selector.
- Ambiguous selector fails closed with product-owned reason.
- Lookup does not publish raw tree dumps.
- Existing `elementIndex` paths remain backward compatible.

### Package E: carry-through and proof

Scope:

- Apply new observation model to `observeAfter`.
- Re-evaluate focused fallback consistency under preview-incomplete state.
- Add helper/synthetic characterization for poor-UIA and deep-tree behavior.
- Refresh docs/generated surfaces and install/publication proof.

Acceptance:

- `observeAfter=true` can return visual successor state with semantic
  completeness metadata.
- Focused fallback does not become blind focus trust.
- Cache-installed proof includes a poor-UIA or deep-tree characterization case.
- Roadmap and generated docs match actual shipped behavior.

## 11. Test ladder

L1 unit / contract:

- `ComputerUseWinFailureCodeMapper` / new failure materializer tests for state,
  action and successor failure sanitization.
- `ComputerUseWinObservationEnvelope` tests for completeness carry-through.
- Selector validation tests for allowed fields, missing required fields,
  oversized strings and ambiguous shape.
- `UiaSnapshotTreeBuilderTests` remain the low-level proof that completeness
  flags are factual.

L2 integration:

- `ComputerUseWinObservationTests`:
  - capture success + UIA failed/incomplete -> public visual success with
    semantic completeness metadata;
  - capture failure -> public `observation_failed`;
  - raw snapshot reason does not leak;
  - state token stores realized completeness.
- `ComputerUseWinActionAndProjectionTests`:
  - semantic action by `elementIndex` unchanged;
  - semantic action by selector resolves deep target;
  - selector zero-match/ambiguous-match fail closed;
  - `observeAfter` carries completeness and sanitized failure.
- `ComputerUseWinArchitectureTests`:
  - schema exposes additive selector branches only where intended;
  - `get_app_state` schema does not accidentally publish broad raw-tree/depth
    knobs unless explicitly chosen.

L3 smoke / live harness:

- Real STDIO helper window with synthetic poor-UIA mode:
  screenshot succeeds, UIA preview fails or returns root-only tree.
- Real STDIO helper window with synthetic deep-tree mode:
  compact preview omits the target, bounded selector lookup reaches it.
- Existing `scripts/smoke.ps1` remains sequential and green.
- Existing `scripts/computer-use-win-physical-policy-proof-smoke.ps1` remains
  green and does not claim deep semantic usability.

Release/install proof:

- `scripts/codex/prove-computer-use-win-cache-install.ps1` must prove the
  cache-installed plugin exposes the additive public surface and can run the
  characterization path from fresh materialization.
- If plugin install surface changes, acceptance must include cache-installed
  copy, restart/new-thread materialization and at least one real read-only or
  state-first tool call.

## 12. Smoke / characterization strategy

Do not use live Karing/R130SH as the only acceptance floor. They are useful
real-world characterization targets, but deterministic proof should be local:

- Add a smoke helper mode that can deliberately return poor semantic preview
  while still allowing screenshot capture.
- Add a deep-tree helper mode where a semantic target is outside compact depth
  but reachable through bounded selector lookup.
- Keep optional live Karing/R130SH repro notes in artifacts only, because these
  apps can drift independently of the repo.
- Record evidence under `artifacts/smoke/...` and `artifacts/diagnostics/...`.
- Summary must explicitly say whether the run proves:
  - visual state success under UIA incomplete;
  - semantic selector reachability beyond preview;
  - sanitized state/successor failures;
  - unchanged existing physical-policy `executionFacts`.

## 13. Docs/generated sync

Planning wave:

- This planned exec-plan is the only required tracked planning artifact.
- `docs/CHANGELOG.md` records that the new planned slice exists.
- No generated docs should be refreshed in this branch because no runtime
  contract has changed yet.

Implementation wave:

- Update [computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
  for visual-vs-semantic observation split.
- Update [observability.md](../../architecture/observability.md) for
  completeness/failure sanitization markers.
- Update [okno-roadmap.md](../../product/okno-roadmap.md) with exact placement
  after owner accepts this plan.
- Refresh generated docs:
  - [computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
  - [project-interfaces.md](../../generated/project-interfaces.md)
  - [commands.md](../../generated/commands.md), if new smoke/proof commands are added.
  - [test-matrix.md](../../generated/test-matrix.md)
- Update plugin skill/manifest wording only if public operator instructions
  change.

## 14. Roadmap placement proposal

Current practical order starts with:

1. `computer-use-win physical execution policy hardening phase 2 / remaining closure beyond phase-1`
2. app playbooks expansion + lightweight capability hints
3. `windows.region_capture`
4. `windows.clipboard_get` / `windows.clipboard_set`
5. `windows.uia_action`

Recommended insertion:

1. Keep current item `1` unchanged and finish slice `13` closure first.
2. Insert new explicit R2 item immediately after current item `1`:
   `computer-use-win observation completeness / deep semantic lookup`.
3. Shift app playbooks, region capture, clipboard and `windows.uia_action`
   after that new item.

Suggested capability-map wording:

```text
| 13b | `plugins/computer-use-win` + observation completeness / deep semantic lookup | public `get_app_state` separates screenshot-backed visual observation from semantic preview readiness, publishes product-owned semantic completeness metadata, sanitizes state/successor failure materialization through the shared public failure owner, and gives semantic actions a bounded lookup lane beyond compact preview `elementIndex` without raw-tree dumps or broad OCR/browser fallback. | `запланировано` | `0%` | `R2` |
```

Rationale:

- This is closer to shipped public slice `09` than to future `windows.uia_action`,
  but it should not be implemented as a retroactive private patch to slice `09`.
- It should land before app playbooks because capability hints cannot honestly
  classify strong-semantic vs poor-UIA targets while observation completeness is
  hidden.
- It should land before `region_capture`, clipboard and `windows.uia_action`
  because all of them will consume the same visual/semantic readiness boundary.

Do not edit roadmap automatically in the planning branch beyond this proposal.
Roadmap update belongs to the implementation or architecture-approval wave.

## 15. Risks / rollback / out-of-scope

Risks:

- Publishing too much UIA metadata can make the public state noisy and unstable.
- Raw/deep lookup can become slow or provider-hostile if not strictly bounded.
- Selector targeting can create false confidence if ambiguity policy is weak.
- Partial visual success can accidentally make semantic-only actions look
  action-ready unless readiness is checked at action boundary.
- Adding selector branches to every action at once can make schema/testing too
  broad for one slice.

Rollback strategy:

- Keep all public additions additive.
- Keep existing `elementIndex` paths unchanged.
- Preserve capture-failure behavior as hard observation failure.
- Keep deep lookup behind semantic action resolution, not preview tree expansion.
- If deep lookup proves unstable, ship Package B/C first and leave Package D
  planned with red characterization retained.

Out of scope:

- Broad OCR provider.
- Browser/Electron substrate.
- `windows.region_capture`.
- Clipboard integration.
- `windows.uia_action` implementation.
- Stable target lease substrate.
- New public "raw UI tree" or "inspect all descendants" tool.
