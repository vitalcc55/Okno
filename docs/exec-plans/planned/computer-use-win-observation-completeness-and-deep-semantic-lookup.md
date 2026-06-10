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
- [ComputerUseWinScrollTargetResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollTargetResolver.cs)
- [ComputerUseWinActionRequestExecutor.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs)
- [ComputerUseWinSetValueExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinSetValueExecutionCoordinator.cs)
- [ComputerUseWinScrollExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollExecutionCoordinator.cs)
- [ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs)
- [ComputerUseWinTypeTextExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs)
- [ComputerUseWinActionability.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionability.cs)
- [ComputerUseWinAffordanceResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAffordanceResolver.cs)
- [ComputerUseWinToolRegistration.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs)
- [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)
- [AutomationSnapshotNode.cs](../../../src/WinBridge.Runtime.Windows.UIA/AutomationSnapshotNode.cs)
- [UiaSnapshotTreeBuilder.cs](../../../src/WinBridge.Runtime.Windows.UIA/UiaSnapshotTreeBuilder.cs)
- [Win32UiAutomationBackend.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationBackend.cs)
- [Win32UiAutomationWaitProbe.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationWaitProbe.cs)
- [UiaSnapshotDefaults.cs](../../../src/WinBridge.Runtime.Contracts/UiaSnapshotDefaults.cs)
- [UiaSnapshotToolResult.cs](../../../src/WinBridge.Runtime.Contracts/UiaSnapshotToolResult.cs)
- [WaitElementSelector.cs](../../../src/WinBridge.Runtime.Contracts/WaitElementSelector.cs)
- [WaitRequestValidator.cs](../../../src/WinBridge.Runtime.Contracts/WaitRequestValidator.cs)

Current tests:

- [ComputerUseWinObservationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs)
- [ComputerUseWinActionAndProjectionTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs)
- [UiaSnapshotTreeBuilderTests.cs](../../../tests/WinBridge.Runtime.Tests/UiaSnapshotTreeBuilderTests.cs)
- [WindowUiaSnapshotToolTests.cs](../../../tests/WinBridge.Server.IntegrationTests/WindowUiaSnapshotToolTests.cs)
- [WindowWaitToolTests.cs](../../../tests/WinBridge.Server.IntegrationTests/WindowWaitToolTests.cs)
- [Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs)

Current proof and publication harness:

- [computer-use-win-physical-policy-proof-smoke.ps1](../../../scripts/computer-use-win-physical-policy-proof-smoke.ps1)
- [prove-computer-use-win-cache-install.ps1](../../../scripts/codex/prove-computer-use-win-cache-install.ps1)
- [SKILL.md](../../../plugins/computer-use-win/skills/computer-use-win/SKILL.md)
- [plugin.json](../../../plugins/computer-use-win/.codex-plugin/plugin.json)

Local reference repos actually used in this planning pass:

- [INDEX.md](../../../references/INDEX.md)
- [SnapshotBuilder.cs](../../../references/repos/FlaUI-MCP/src/FlaUI.Mcp/Core/SnapshotBuilder.cs)
- [ElementRegistry.cs](../../../references/repos/FlaUI-MCP/src/FlaUI.Mcp/Core/ElementRegistry.cs)
- [SnapshotTool.cs](../../../references/repos/FlaUI-MCP/src/FlaUI.Mcp/Tools/SnapshotTool.cs)

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

## 8. Target model and engineering approach

Closed design decisions for the implementation wave:

1. `get_app_state` must become a successful visual observation path whenever
   screenshot capture succeeds and target/window identity is still product-valid,
   even if semantic preview is incomplete or unavailable.
2. Top-level state status should stay `ok`; semantic readiness belongs in an
   explicit nested product-owned envelope, not in a new top-level `partial`
   status.
3. Product-owned semantic completeness must be additive and explicit. The target
   public shape is:

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

4. Raw provider/UIA reasons stay audit-only. Public state and public
   successor-state failures go through the same owner-layer materializer that
   already protects action failures.
5. The first implementation should not widen public `get_app_state` with an
   arbitrary `depth` control. The target model is a compact preview tree plus a
   bounded semantic lookup lane, not a deeper preview-by-default tree.
6. The selector model should not be invented ad hoc. The repo already has a
   bounded selector shape in [WaitElementSelector.cs](../../../src/WinBridge.Runtime.Contracts/WaitElementSelector.cs)
   and matching logic in
   [Win32UiAutomationWaitProbe.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationWaitProbe.cs).
   The implementation wave should either reuse that shape directly or extract a
   shared generic selector concept from it once, then migrate both wait and
   `computer-use-win` to the shared owner.
7. The first target actions for selector-driven reachability should be
   `set_value`, `perform_secondary_action`, and semantic `scroll`, because they
   have the clearest semantic success criteria; but the plan must include an
   explicit closure pass for `click`, `drag`, and ordinary `type_text`, because
   they reuse the same target-proof class and cannot be ignored silently.
8. `observeAfter` must inherit the same visual/semantic split and completeness
   envelope; a committed action may yield a valid visual successor state even
   when semantic preview remains incomplete.
9. Focused fallback and coordinate-confirmed fallback are not separate root
   causes. They are downstream consumers of the same observation model and must
   be checked in the same closure pass.
10. The slice must not become a raw-tree dump, an OCR workaround, a browser
    detour, or a second UI runtime.

Where DDD is justified:

- Shared public failure materialization is a real domain seam because the same
  product invariant spans state, action, successor state, audit, and MCP
  payload wording.
- The semantic completeness envelope is a domain model, not a transport detail,
  because it defines action-readiness truth for the whole `computer-use-win`
  loop.
- The selector concept is a domain contract because the same identity semantics
  are already relevant to `windows.wait` and now become relevant to
  `computer-use-win`.
- A bounded semantic lookup service is a domain-owned runtime seam because it
  defines what "element truly not found" means for this product.

Where DDD is not justified:

- WinForms smoke fixture code in [Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs)
  should stay a deterministic test harness, not a new domain layer.
- Proof scripts and generated docs should reuse the runtime truth surface and
  not grow their own model objects.
- Low-level traversal loops should remain small runtime services; no aggregate or
  repository pattern is needed there.

Where TDD is justified:

- Public failure sanitization.
- Semantic completeness payload/state-token carry-through.
- Shared selector contract and bounded lookup rules.
- Action integration on selector-driven semantic targets.
- `observeAfter` successor-state propagation of the new model.

Where TDD is not mandatory:

- Pure docs/generated refresh.
- Helper proof scripts after the runtime behavior is already green.
- Small smoke-fixture layout changes that are only there to support already-red
  integration tests.

Open decisions that the implementation branch still must close explicitly:

- Final public field names for the semantic completeness envelope.
- Whether the selector type is direct reuse of `WaitElementSelector` or
  extraction of a new shared generic selector record.
- Whether the bounded lookup lane should traverse control view only, or should
  use a separately bounded raw-view search for selector-only lookups.
- Exact lookup budgets: max depth, max nodes, max matches, timeout.
- Whether ordinary `type_text` receives the selector branch in the same slice or
  in the immediately following closure step.

## 9. File-level integration map

| Area | Files | Required future responsibility |
| --- | --- | --- |
| Public state contract | `ComputerUseWinContracts.cs` | Add the semantic completeness envelope to `ComputerUseWinGetAppStateResult`; expand stored observation contract to carry realized completeness and public failure code. |
| State observation owner | `ComputerUseWinAppStateObserver.cs` | Split capture success from semantic preview readiness, produce visual-success outcomes with semanticPreview metadata, and stop leaking `snapshot.Reason` directly. |
| State finalization | `ComputerUseWinGetAppStateFinalizer.cs`, `ComputerUseWinGetAppStateHandler.cs` | Commit `stateToken` only after successful visual materialization, keep image-bearing success payloads, and preserve current approval/activation/selector semantics. |
| Shared failure owner | `ComputerUseWinToolResultFactory.cs`, `ComputerUseWinFailureCodeMapper.cs`, `ComputerUseWinObservationFailureTranslator.cs`, `ComputerUseWinFailureDetails.cs` | Materialize the same product-owned failure wording for state, action, and successor-state paths. |
| Shared selector seam | `WaitElementSelector.cs`, `WaitRequestValidator.cs`, `Win32UiAutomationWaitProbe.cs` | Reuse or extract the generic selector shape and selector-match policy instead of creating a second ad hoc selector contract. |
| State storage | `ComputerUseWinStateStore.cs` | Replace requested-only observation envelope with a richer stored semanticPreview/completeness envelope that downstream actions can trust. |
| Preview projection | `ComputerUseWinAccessibilityProjector.cs`, `ComputerUseWinAffordanceResolver.cs`, `ComputerUseWinActionability.cs` | Keep compact preview tree/operator readability, but stop treating preview omission as proof that the element does not exist. |
| Preview revalidation | `ComputerUseWinFreshElementResolver.cs` | Narrow its role to preview-element continuity; do not let it remain the only semantic reachability mechanism after selector lookup exists. |
| Deep lookup runtime | `Win32UiAutomationBackend.cs`, `AutomationSnapshotNode.cs`, `UiaSnapshotTreeBuilder.cs`, possible new `IUiAutomationSemanticLookupService` + `Win32UiAutomationSemanticLookupService` | Introduce bounded descendant lookup rooted at the current window, with explicit ambiguity and budget policy. |
| Semantic target resolution | `ComputerUseWinSetValueExecutionCoordinator.cs`, `ComputerUseWinScrollTargetResolver.cs`, `ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs`, possible new `ComputerUseWinSemanticTargetResolver` | Share one target-resolution path that can choose preview element or selector-driven lookup and return typed failures. |
| Additional action closure | `ComputerUseWinScrollExecutionCoordinator.cs`, `ComputerUseWinTypeTextExecutionCoordinator.cs`, `ComputerUseWinClickExecutionCoordinator.cs`, `ComputerUseWinDragExecutionCoordinator.cs` | Reuse the same target-proof model where the action still depends on semantic target reachability before physical dispatch. |
| Successor observation | `ComputerUseWinActionRequestExecutor.cs`, `ComputerUseWinActionSuccessorObservation.cs` | Carry the new semantic completeness envelope and public failure mapping through `observeAfter`. |
| Proof and publication | `computer-use-win-physical-policy-proof-smoke.ps1`, `prove-computer-use-win-cache-install.ps1`, `Program.cs` in `WinBridge.SmokeWindowHost` | Add deterministic characterization and cache-install proof without falsely widening what the physical-policy proof-smoke claims. |
| Public docs and operator surface | `computer-use-win-surface.md`, `observability.md`, `okno-roadmap.md`, `computer-use-win-interfaces.md`, `project-interfaces.md`, `test-matrix.md`, plugin `SKILL.md`, `plugin.json` | Sync only after runtime/test/smoke truth is green and the final public contract is known. |

## 10. Sequential implementation plan

### Step 0. Freeze the baseline and add red characterization

Target level:

- General invariant. This is not a local fix; it defines the defect family and
  prevents the rest of the implementation from drifting into isolated patches.

Preconditions:

- Current branch for implementation is created after the planning branch.
- Working tree is clean enough to separate this slice from unrelated edits.
- The sequential verification rule from `AGENTS.md` remains in force: no
  parallel `dotnet build` / `dotnet test` / `smoke` runs in one worktree.

Dependencies:

- None.

Implementation focus:

- Re-run the targeted existing tests that already pin the current baseline:
  `ComputerUseWinObservationTests`, `ComputerUseWinActionAndProjectionTests`,
  `UiaSnapshotTreeBuilderTests`, `WindowUiaSnapshotToolTests`.
- Add future-behavior tests first so they fail before production code changes:
  - state failure sanitization for initial `get_app_state`;
  - visual-success/UIA-incomplete `get_app_state`;
  - semantic selector reaching a deep target outside compact preview;
  - `observeAfter` carrying visual successor state with semantic completeness.
- Add the smallest synthetic fixtures needed for these tests, but do not change
  production behavior yet.

Concrete integration points:

- [ComputerUseWinObservationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs)
- [ComputerUseWinActionAndProjectionTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs)
- [WindowUiaSnapshotToolTests.cs](../../../tests/WinBridge.Server.IntegrationTests/WindowUiaSnapshotToolTests.cs)
- [Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs), only if a new deterministic fixture is required for later smoke proof

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected red signals:

- Public initial state still leaks raw/semi-raw UIA reason text.
- Successful capture + failed/incomplete UIA still does not return `status=ok`.
- Deep target outside preview cannot be reached through any bounded selector.
- `observeAfter` still fails the whole observation lane instead of returning a
  visual successor state with semantic completeness metadata.

Expected result:

- The defect family is pinned by executable tests before the model changes.

Closure pass:

- Confirm that the red suite covers initial state, successor state, semantic
  action reachability, and publication proof seams.
- If any scenario is left untested, justify explicitly why it belongs to a later
  step and not to the same class.

### Step 1. Unify public failure materialization for state, action, and successor state

Target level:

- General invariant. The problem is repeated across state and successor paths and
  should be closed once, not patched per caller.

Preconditions:

- Step 0 red tests exist for state failure sanitization.

Dependencies:

- Step 0.

Implementation focus:

- Introduce one shared owner for product-owned public failure wording.
- Route `get_app_state` state failures through the same owner-layer mapping
  discipline that action failures already use.
- Keep raw provider/UIA/capture text in diagnostics and audit only.
- Preserve current `failureCode` taxonomy unless a red test proves it is
  insufficient.

Concrete integration points:

- [ComputerUseWinToolResultFactory.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs)
- [ComputerUseWinFailureCodeMapper.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinFailureCodeMapper.cs)
- [ComputerUseWinObservationFailureTranslator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinObservationFailureTranslator.cs)
- [ComputerUseWinFailureDetails.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinFailureDetails.cs)
- [ComputerUseWinAppStateObserver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs)

Constraints:

- Do not weaken existing action-path sanitization.
- Do not change MCP `isError` behavior just to hide failure text.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected result:

- Initial `get_app_state` failure no longer leaks raw/semi-raw provider reason.
- `successorStateFailure` keeps its current safe behavior.
- The public failure wording becomes one consistent product surface.

Closure pass:

- Recheck capture failure, UIA traversal failure, state-target resolution
  failure, successor observe failure, and unexpected exception paths.
- Confirm there is no symmetric leak left in state, action, or successor-state
  materialization.

### Step 2. Introduce a product-owned semantic completeness model

Target level:

- General model. This is a new domain truth layer for the whole
  `computer-use-win` loop.

Preconditions:

- Shared public failure materializer from Step 1 exists.

Dependencies:

- Step 1.

Implementation focus:

- Add a product-owned semantic completeness envelope to the public state payload.
- Replace the current requested-only observation storage with a stored envelope
  that also carries realized depth/node/truncation status and a product-owned
  semantic failure code.
- Keep `capture`, `accessibilityTree`, and `stateToken` as separate existing
  concepts.

Concrete integration points:

- [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)
- [ComputerUseWinStateStore.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStateStore.cs)
- [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs)
- [UiaSnapshotToolResult.cs](../../../src/WinBridge.Runtime.Contracts/UiaSnapshotToolResult.cs), as the low-level source of factual completeness fields

Constraints:

- No public raw `reason` field inside `semanticPreview`.
- No top-level `status=partial`.
- No global `Depth` bump as a substitute for a real model.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected result:

- Public payload and stored state can distinguish `complete`, `incomplete`,
  `unavailable`, and `failed` semantic preview.
- Downstream actions can tell the difference between "not in preview" and "no
  semantic data available at all."

Closure pass:

- Confirm the model is present on initial state and successor state.
- Confirm approval-required, blocked, and hard observation-failure paths do not
  create ghost state tokens.
- Confirm the model uses only product-owned failure codes.

### Step 3. Split visual observation success from semantic preview readiness

Target level:

- General invariant. The problem affects all poor-UIA state acquisition, not one
  specific app.

Preconditions:

- Step 2 semantic completeness model exists.

Dependencies:

- Step 2.

Implementation focus:

- Refactor `ComputerUseWinAppStateObserver` so capture is the critical
  observation boundary and UIA is a semantic preview sub-stage.
- On screenshot success + UIA incomplete/unavailable/failure, return a successful
  prepared state with:
  - image content;
  - `stateToken`;
  - warnings;
  - `semanticPreview`;
  - empty or partial `accessibilityTree` by explicit policy.
- Keep capture failure as hard `observation_failed`.

Concrete integration points:

- [ComputerUseWinAppStateObserver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs)
- [ComputerUseWinGetAppStateHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs)
- [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs)
- [ComputerUseWinAccessibilityProjector.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAccessibilityProjector.cs)

Constraints:

- Keep image-bearing MCP content blocks on success.
- Preserve current approval, activation, and `windowId` continuity policy.
- Do not silently promote semantic-only actions to ready just because visual
  observation succeeded.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected result:

- Poor-UIA windows can continue through an honest visual loop.
- Public payload makes semantic incompleteness explicit instead of pretending the
  observe step failed completely.

Closure pass:

- Recheck initial state, activation warning path, advisory instruction failure,
  unexpected instruction-provider bug, and hard capture failure.
- Confirm `observeAfter` is still pending and has not silently diverged from the
  new model.

### Step 4. Extract the shared bounded selector domain

Target level:

- General model/refactor. The repo already has the same selector concept in
  `windows.wait`; duplicating it would create a new local path instead of a
  shared model.

Preconditions:

- Step 3 is green.

Dependencies:

- Step 3.

Implementation focus:

- Reuse or extract the generic selector contract currently represented by
  [WaitElementSelector.cs](../../../src/WinBridge.Runtime.Contracts/WaitElementSelector.cs).
- Move selector-match semantics out of
  [Win32UiAutomationWaitProbe.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationWaitProbe.cs)
  into a shared owner that both wait and `computer-use-win` can rely on.
- Keep the selector bounded to `name`, `automationId`, and `controlType` unless
  characterization later proves `frameworkId` is necessary.
- Introduce one typed ambiguity policy rather than multiple ad hoc coordinator
  branches.

Concrete integration points:

- [WaitElementSelector.cs](../../../src/WinBridge.Runtime.Contracts/WaitElementSelector.cs)
- [WaitRequestValidator.cs](../../../src/WinBridge.Runtime.Contracts/WaitRequestValidator.cs)
- [Win32UiAutomationWaitProbe.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationWaitProbe.cs)
- [ComputerUseWinToolRegistration.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs), later when additive selector shapes are exposed

Constraints:

- Do not break `windows.wait` public JSON shape.
- Do not create a second selector DTO purely for inertia.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected result:

- One canonical selector shape and one canonical selector-match policy exist in
  the repo.

Closure pass:

- Re-run `WindowWaitToolTests` and selector validation tests.
- Confirm exact-match semantics stay consistent across `name`, `automationId`,
  and `controlType`.

### Step 5. Implement bounded deep semantic lookup in the UIA runtime

Target level:

- General runtime seam. This closes the class "preview tree omitted the target"
  without widening the preview surface itself.

Preconditions:

- Step 4 shared selector seam is green.

Dependencies:

- Step 4.

Implementation focus:

- Add a bounded semantic lookup service rooted at the current window.
- Use the same cache strategy as the current UIA snapshot where possible.
- Make lookup outcomes typed:
  - unique match;
  - zero matches;
  - ambiguous matches;
  - lookup failed due to provider/runtime issue;
  - lookup aborted due to budget or timeout.
- Keep diagnostics factual, but keep raw provider text out of public payloads.

Concrete integration points:

- [Win32UiAutomationBackend.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationBackend.cs)
- [AutomationSnapshotNode.cs](../../../src/WinBridge.Runtime.Windows.UIA/AutomationSnapshotNode.cs)
- [UiaSnapshotTreeBuilder.cs](../../../src/WinBridge.Runtime.Windows.UIA/UiaSnapshotTreeBuilder.cs)
- possible new `IUiAutomationSemanticLookupService` and
  `Win32UiAutomationSemanticLookupService`
- [reference-research-policy.md](../../architecture/reference-research-policy.md), because any reference-driven choice must remain secondary to repo state and official docs

Constraints:

- No desktop-root traversal.
- No unbounded `FindAll(TreeScope.Descendants, TrueCondition)` over arbitrary
  surfaces.
- No preview tree expansion disguised as lookup.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected result:

- The runtime can prove that a selector-resolved target exists even when the
  compact preview tree did not include it.

Closure pass:

- Recheck cancellation, timeout, node-budget exhaustion, ambiguous matches,
  element-not-available, and invalid operation paths.
- Confirm the new service does not introduce a parallel public result surface.

### Step 6. Integrate the selector lane into semantic actions first

Target level:

- General repeated class across semantic executors.

Preconditions:

- Step 5 bounded lookup service is green.

Dependencies:

- Step 5.

Implementation focus:

- Extend public request schemas additively so semantic actions can accept either
  `elementIndex` or a selector branch.
- Start with:
  - `set_value`;
  - `perform_secondary_action`;
  - semantic `scroll`.
- Replace coordinator-local "elementIndex only" assumptions with one shared
  semantic target resolver that can:
  - resolve from preview element when present;
  - resolve from selector when preview omitted the target;
  - return typed stale/ambiguous/unsupported failures.

Concrete integration points:

- [ComputerUseWinSetValueExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinSetValueExecutionCoordinator.cs)
- [ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs)
- [ComputerUseWinScrollTargetResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollTargetResolver.cs)
- [ComputerUseWinScrollExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollExecutionCoordinator.cs)
- [ComputerUseWinFreshElementResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinFreshElementResolver.cs)
- [ComputerUseWinToolRegistration.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs)

Constraints:

- Preserve existing `elementIndex` success behavior.
- Keep semantic action success semantics unchanged: `set_value` and
  `perform_secondary_action` remain semantic, and semantic `scroll` remains
  semantic.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected red signals:

- Schema rejects the new selector branch.
- Deep semantic target outside preview cannot be reached by `set_value`.
- Ambiguous selector does not fail closed.

Expected result:

- Strong-semantic deep targets can be acted on without widening the preview tree.

Closure pass:

- Recheck `set_value`, `perform_secondary_action`, and semantic `scroll` across
  happy path, stale-state, unsupported-action, ambiguous selector, and
  post-activation revalidation failure.
- Confirm the shared resolver did not leave local duplicate branches behind.

### Step 7. Close the same reachability class across proof-backed physical actions

Target level:

- General class closure. This step exists so the slice does not stop at the
  easiest semantic-only path and leave symmetric holes elsewhere.

Preconditions:

- Step 6 is green.

Dependencies:

- Step 6.

Implementation focus:

- Evaluate target-bearing physical/public actions that still depend on semantic
  proof before dispatch:
  - `click(elementIndex)` and future `click(selector)` branch;
  - `drag` endpoints where source/destination semantic proof matters;
  - ordinary `type_text` when it still depends on semantic/focus proof.
- Extend the shared target-resolution model to these paths unless tests prove a
  path is materially outside the same class.
- Keep `press_key` out of selector scope unless new evidence appears, because it
  does not resolve descendants.

Concrete integration points:

- [ComputerUseWinClickExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinClickExecutionCoordinator.cs)
- [ComputerUseWinDragExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinDragExecutionCoordinator.cs)
- [ComputerUseWinTypeTextExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs)
- [ComputerUseWinActionability.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionability.cs)
- [ComputerUseWinAffordanceResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAffordanceResolver.cs)

Constraints:

- Do not add selector semantics to coordinate-only paths that do not need them.
- Do not create a separate per-tool target model.

TDD:

- Required for any path whose behavior changes.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`
- Not required for `press_key` if it remains unchanged; a regression run is
  enough because no new behavior is being introduced there.

Expected result:

- The reachability class is closed for all target-bearing paths that share the
  same proof problem.

Closure pass:

- Explicitly check `click`, `drag`, ordinary `type_text`, semantic `scroll`,
  `set_value`, and `perform_secondary_action`.
- If any one of them is deliberately left out, document why it is not part of
  the same class.

### Step 8. Carry the new observation model through `observeAfter` and stored successor state

Target level:

- General invariant across the action loop.

Preconditions:

- Steps 3, 6, and any Step 7 behavioral changes are green.

Dependencies:

- Steps 3, 6, and 7.

Implementation focus:

- Update the successor observation path so it uses the same semantic completeness
  model and the same public failure materializer.
- Allow `observeAfter` to return a visually successful successor state even when
  semantic preview is incomplete.
- Store the new completeness envelope in the successor `stateToken`.

Concrete integration points:

- [ComputerUseWinActionRequestExecutor.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs)
- [ComputerUseWinActionSuccessorObservation.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionSuccessorObservation.cs)
- [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs)

Constraints:

- Top-level action outcome stays factual.
- `refreshStateRecommended` semantics must remain honest.

TDD:

- Required.
- Cycle:
  `test -> red -> implementation -> green -> refactoring -> green`

Expected result:

- `observeAfter` no longer inherits the old "screenshot + full UIA or fail"
  coupling.

Closure pass:

- Recheck `click`, `type_text`, `scroll`, and `drag` `observeAfter` branches.
- Confirm successor state, successor failure, audit, and artifact paths all use
  the same new model.

### Step 9. Update deterministic characterization, smoke proof, and cache-installed proof

Target level:

- General proof closure for the same slice.

Preconditions:

- Runtime and integration changes are green through Step 8.

Dependencies:

- Steps 0 through 8.

Implementation focus:

- Extend the smoke host with deterministic surfaces for:
  - screenshot success + poor semantic preview;
  - deep semantic target outside compact preview.
- Keep the physical-policy proof-smoke honest. If it remains a phase-1 physical
  policy proof, do not let it implicitly claim deep semantic usability.
- Extend cache-installed proof only with scenarios that reflect the final shipped
  public contract.

Concrete integration points:

- [Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs)
- [computer-use-win-physical-policy-proof-smoke.ps1](../../../scripts/computer-use-win-physical-policy-proof-smoke.ps1)
- [prove-computer-use-win-cache-install.ps1](../../../scripts/codex/prove-computer-use-win-cache-install.ps1)
- [test-matrix.md](../../generated/test-matrix.md), only after behavior is green

Constraints:

- Do not make Karing/R130SH the only acceptance floor.
- Do not widen the physical-policy proof-smoke narrative beyond what it actually
  proves.

TDD:

- Required for new integration assertions and helper-backed characterization
  tests.
- Not required for the final generated-doc refresh or for script text edits that
  merely mirror already-green runtime behavior.

Expected result:

- The repo has deterministic local proof for the new observation model and the
  new selector reachability lane.

Closure pass:

- Recheck runtime/tests/smoke symmetry, fresh-thread cache-install proof,
  install-surface materialization, and artifact wording.

### Step 10. Final closure pass, cleanup, and roadmap handoff

Target level:

- Class-wide closure, not a local patch.

Preconditions:

- All previous steps are green.

Dependencies:

- Steps 0 through 9.

Implementation focus:

- Remove obsolete narrow paths and duplicate owners once the target model is
  green:
  - direct state-path publication of `snapshot.Reason`;
  - any duplicate selector DTO or selector-match helper that became redundant;
  - coordinator-local stale/selector branches superseded by the shared resolver;
  - temporary completeness shims left after the final model lands.
- Run the full sequential verification ladder and only then update roadmap/docs.

Concrete integration points:

- All files touched in Steps 1 through 9
- [computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [observability.md](../../architecture/observability.md)
- [okno-roadmap.md](../../product/okno-roadmap.md)
- [computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
- [project-interfaces.md](../../generated/project-interfaces.md)
- [test-matrix.md](../../generated/test-matrix.md)

Constraints:

- No legacy compatibility layer should survive only because the old code existed.
- Do not keep both the old narrow model and the new target model active at once.

TDD:

- No new TDD cycle is required here; this is the cleanup and proof step on top
  of already-green behavior.

Expected result:

- The slice lands as one coherent model with no symmetric holes left in state,
  action, successor-state, docs, tests, smoke, or cache-install proof.

Closure pass:

- Explicitly check happy-path/failure-path, dry-run/live where relevant,
  runtime/docs/tests/smoke, proof/cleanup, marker/fallback symmetry, and
  state/action/successor-state consistency.
- If any remaining gap is intentionally deferred, record it as a new slice with
  exact boundaries instead of leaving an implicit TODO.

## 11. Verification strategy by step

Step-to-test mapping:

- Step 0:
  tests required.
  Reason: the implementation wave needs executable red proof for the defect
  family before runtime changes start.
- Step 1:
  tests required.
  Reason: public failure wording is a stable product contract and easy to pin
  with focused integration tests.
- Step 2:
  tests required.
  Reason: the semantic completeness envelope is a shared state contract and must
  be pinned before refactoring downstream consumers.
- Step 3:
  tests required.
  Reason: this is the central behavioral shift of the slice.
- Step 4:
  tests required.
  Reason: selector semantics are shared and easy to regress silently if not
  pinned first.
- Step 5:
  tests required.
  Reason: bounded lookup is the highest-risk runtime seam in the slice.
- Step 6:
  tests required.
  Reason: semantic action integration changes user-visible behavior and schema.
- Step 7:
  tests required for changed actions, regression-only for unchanged actions.
  Reason: the class closure must be explicit, but no new cycle is needed where
  behavior stays identical.
- Step 8:
  tests required.
  Reason: successor-state truth and `refreshStateRecommended` are public result
  semantics.
- Step 9:
  tests required for characterization/integration changes, not for generated
  docs/script wording that only mirrors already-green behavior.
- Step 10:
  no new tests required beyond the final full verification ladder.
  Reason: this step validates and cleans up the already-proven model.

Recommended verification ladder:

1. Targeted unit/contract tests for the step currently in progress.
2. Targeted integration tests in `WinBridge.Server.IntegrationTests`.
3. If the step changes runtime/public behavior, run the affected helper-backed
   smoke or cache-install proof path.
4. After all steps are green, run the sequential contour:
   `build -> test -> smoke -> physical-policy-proof-smoke -> refresh-generated-docs -> verify`.

## 12. Characterization and smoke strategy

Primary acceptance floor:

- Deterministic local fixtures in
  [Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs).

Secondary acceptance floor:

- Optional live repro notes for Karing/Flutter and R130SH/PySide6, stored only
  as artifacts or investigation notes, not as the sole proof of the slice.

Required deterministic characterization:

- A poor-UIA surface where screenshot capture succeeds but semantic preview is
  unavailable or deliberately incomplete.
- A deep-tree surface where the preview omits the target but a bounded selector
  lookup can still resolve it uniquely.
- At least one successor-state path that proves `observeAfter` now carries the
  new model.

Why this strategy is required:

- It closes the same defect family without tying proof to third-party app drift.
- It lets the cache-installed proof and the repo-local proof speak the same
  language.

## 13. Docs and generated sync policy

Planning branch:

- Only this planned exec-plan changes.

Implementation branch:

- Update tracked product/architecture docs only after the runtime truth is green.
- Refresh generated surfaces only after:
  - contracts compile;
  - tests are green;
  - smoke and cache-install proof are green.

Tracked docs that must be checked together:

- [computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [observability.md](../../architecture/observability.md)
- [okno-roadmap.md](../../product/okno-roadmap.md)
- [computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
- [project-interfaces.md](../../generated/project-interfaces.md)
- [test-matrix.md](../../generated/test-matrix.md)
- [SKILL.md](../../../plugins/computer-use-win/skills/computer-use-win/SKILL.md), only if public operator guidance changes
- [plugin.json](../../../plugins/computer-use-win/.codex-plugin/plugin.json), only if public interface wording changes

## 14. Roadmap placement proposal

Current practical order starts with:

1. `computer-use-win physical execution policy hardening phase 2 / remaining closure beyond phase-1`
2. app playbooks expansion + lightweight capability hints
3. `windows.region_capture`
4. `windows.clipboard_get` / `windows.clipboard_set`
5. `windows.uia_action`

Recommended insertion after implementation proof is green:

1. Finish the current slice `13` closure first.
2. Insert a new explicit R2 slice immediately after it:
   `computer-use-win observation completeness / deep semantic lookup`.
3. Move app playbooks, `windows.region_capture`, clipboard, and
   `windows.uia_action` after that new slice.

Suggested capability-map wording:

```text
| 13b | `plugins/computer-use-win` + observation completeness / deep semantic lookup | public `get_app_state` separates screenshot-backed visual observation from semantic preview readiness, publishes product-owned semantic completeness metadata, sanitizes state/successor failure materialization through the shared public failure owner, and gives target-bearing actions a bounded lookup lane beyond compact preview `elementIndex` without raw-tree dumps or broad OCR/browser fallback. | `запланировано` | `0%` | `R2` |
```

Why this ordering is still correct:

- The slice strengthens the already shipped public operator surface, not a later
  private runtime.
- App playbooks cannot classify targets honestly while semantic completeness is
  still hidden.
- `region_capture`, clipboard, and `windows.uia_action` all depend on the same
  observation/readiness boundary and should not outrun it.

## 15. Risks, rollback, and explicit out-of-scope

Main risks:

- Too much semantic metadata can make the public state noisy.
- A raw/deep lookup lane can become expensive or provider-hostile if budgets are
  weak.
- Selector ambiguity can create false confidence if it is not fail-closed.
- A visual-success state can accidentally look action-ready if semantic-only
  actions are not hardened at their own boundary.

Rollback policy:

- Public additions remain additive where possible.
- `elementIndex` remains supported.
- Capture failure remains a hard observation failure.
- The deep lookup lane stays behind selector-based target resolution and must
  never become a raw-tree public tool.
- If the deep lookup runtime proves unstable, do not keep half-integrated local
  selector hacks alive; keep the red characterization, ship only the completed
  earlier steps, and reopen the lookup work as a bounded follow-up slice.

Explicitly out of scope for this slice:

- Broad OCR provider.
- Browser/Electron substrate.
- `windows.region_capture`.
- Clipboard integration.
- `windows.uia_action`.
- Stable target lease substrate.
- New public raw-tree inspection tool.
