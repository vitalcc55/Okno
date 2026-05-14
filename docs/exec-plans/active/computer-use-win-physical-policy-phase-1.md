# ExecPlan: Computer Use for Windows physical policy phase 1

Status: `active`  
Branch: `codex/computer-use-win-physical-policy-phase-1`  
Date: `2026-05-14`  
Parent workstream: [computer-use-win-physical-execution-policy-hardening.md](computer-use-win-physical-execution-policy-hardening.md)

## 1. Goal

Сделать **первый implementation-ready phase** большого workstream
`computer-use-win physical execution policy hardening` как один связный пакет,
который усиливает уже shipped public action surface без расширения public tool
zoo.

Phase-1 этой ветки обязан:

- добавить в public action result **явный nested `executionFacts` envelope**;
- сделать `execution mode` частью product truth, а не только audit/event trail;
- честно различать:
  - `semantic`
  - `expected_physical`
  - `fallback_physical`
- протянуть этот слой через:
  - public result model
  - `computer_use_win.action.completed`
  - action JSON artifacts
  - audit completion data
  - generated contract/docs
- сохранить screenshot-first loop, existing `observeAfter=true` и honest
  `verify_needed`;
- добавить рядом **companion minimal proof-smoke**, чтобы phase-1 semantics
  сразу были доказуемы на реальном `computer-use-win` loop.

Жёсткая boundary-formulation для этой ветки:

- это не “approvals work”;
- это не “broad lease substrate”;
- это не “region/UIA/browser next wave”;
- это не “release/install packaging wave”;
- это не “long-horizon leadership plan целиком”.

Это именно **phase-1 concrete plan** для:

```text
executionFacts envelope
+ dispatchClass truth
+ action-lifecycle integration
+ observability/audit sync
+ companion minimal proof-smoke
```

## 2. Non-goals

Phase-1 сознательно **не** делает следующее:

- не добавляет новый public action family;
- не меняет product boundary `computer-use-win` vs internal `windows.*`;
- не трогает packaging/install model как основной scope;
- не смешивает работу с [okno-release-and-install-packaging.md](okno-release-and-install-packaging.md);
- не делает `windows.region_capture`;
- не делает `windows.uia_action`;
- не делает broad app capability memory/profile layer;
- не строит broad lease substrate целиком;
- не делает `actionReceipt`, `semanticDiff`, `recommendedRecovery` как
  отдельный later proof-envelope slice;
- не делает OCR;
- не строит browser/Electron executor lane;
- не строит terminal lane;
- не уходит в “второй курсор”, Raw Input multiplexer, VHF/driver path или
  hidden HID tricks;
- не вводит hidden clipboard path;
- не превращает `verify_needed` в optimistic `done`;
- не превращает tracked docs в каталог внешних repo identities.

## 3. Current repo state relevant to this phase

### 3.1. Product and planning state

По состоянию на `2026-05-14` source of truth уже зафиксирован:

- roadmap [okno-roadmap.md](../../product/okno-roadmap.md) держит slice `13`
  как `computer-use-win physical execution policy hardening` и уже ставит
  рядом minimal proof-smoke как следующий companion step;
- broad umbrella plan
  [computer-use-win-physical-execution-policy-hardening.md](computer-use-win-physical-execution-policy-hardening.md)
  существует, но остаётся слишком верхнеуровневым для package-by-package
  implementation;
- planned leadership document
  [okno-agent-native-runtime-leadership.md](../planned/okno-agent-native-runtime-leadership.md)
  уже требует, чтобы **первый implementation slice** materialize-ил:
  - `executionFacts`
  - `semantic / expected_physical / fallback_physical`
  - companion minimal proof-smoke
- release/install packaging выделен в отдельный active workstream
  [okno-release-and-install-packaging.md](okno-release-and-install-packaging.md)
  и не должен смешиваться с этой веткой.

### 3.2. Current shipped public surface

Current public `computer-use-win` callable subset уже shipped:

- `list_apps`
- `get_app_state`
- `click`
- `press_key`
- `set_value`
- `type_text`
- `scroll`
- `perform_secondary_action`
- `drag`

Правильный phase-1 scope поэтому не в ширину, а в **truth/proof depth**.

### 3.3. Current public result shape is still too thin

Current `ComputerUseWinActionResult` в
[ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)
содержит только:

- `status`
- `stateToken`
- `refreshStateRecommended`
- `failureCode`
- `reason`
- `targetHwnd`
- `elementIndex`
- `successorState`
- `successorStateFailure`

То есть product result пока **не умеет явно сказать**, был ли action:

- semantic;
- expected physical;
- fallback physical.

### 3.4. Current runtime already has partial execution facts, but only in fragments

Current repo уже носит полезные partial facts, но они живут фрагментированно:

- per-action contract/coordinator уже выставляют `riskClass`,
  `dispatchPath`, `fallbackUsed`;
- `ComputerUseWinActionObservabilityContext` уже materialize-ит safe markers:
  - `risk_class`
  - `dispatch_path`
  - `fallback_used`
  - `observe_after_requested`
  - `successor_state_available`
  - `successor_state_failure_code`
  - `confirmation_required`
  - `confirmed`
  - `state_token_present`
  - `capture_reference_present`
- `observeAfter=true` уже shipped и даёт nested `successorState`, но это ещё не
  заменяет unified physical-policy envelope;
- low-level input preflight уже знает:
  - `target_not_foreground`
  - `target_integrity_blocked`
  - `target_minimized`
  - `capture_reference_required`
  - `point_out_of_bounds`
  - `input_dispatch_failed`

Проблема не в полном отсутствии facts, а в том, что **public product truth**
пока не собран в один coherent layer.

### 3.5. Current action semantics are already mixed

Current action wave уже показывает, что phase-1 нужен именно сейчас:

- `set_value` и `perform_secondary_action` — semantic paths;
- `scroll(elementIndex)` — semantic path через UIA;
- `click`, `press_key`, `drag` — physical dispatch paths, даже если target proof
  может быть semantic;
- `type_text` уже имеет bounded fallback paths:
  - focused fallback
  - coordinate-confirmed fallback

То есть repo уже фактически живёт в mixed execution reality, но public result
ещё не называет её своими именами.

### 3.6. Proof gap

Current proof contour силён, но **не phase-1-specific**:

- unit/integration coverage уже большая;
- `scripts/smoke.ps1` already proves engine baseline and click-first
  `windows.input`;
- `McpProtocolSmokeTests` already prove a lot of `computer-use-win` behavior;
- `scripts/codex/prove-computer-use-win-cache-install.ps1` already proves
  installed schema/tool surface.

Но пока нет **узкого proof-smoke companion**, который удерживает именно:

- `executionFacts`
- `dispatchClass`
- risky physical confirmation semantics
- stale/wrong-foreground fail-close
- successor-observe-or-failure truth

## 4. Shipped invariants to preserve

Phase-1 implementation не имеет права ломать следующие already-shipped
invariants:

- `computer-use-win` остаётся quiet public operator surface;
- новые tools не добавляются;
- `list_apps -> get_app_state -> action -> verify` остаётся canonical loop;
- `get_app_state` и successful `observeAfter=true` остаются image-bearing
  observe steps;
- `windowId` остаётся runtime-owned discovery-scoped selector;
- `stateToken` остаётся short-lived observation proof, а не новым selector;
- `observeAfter=true` остаётся advisory successor observe без optimistic rewrite
  top-level outcome;
- existing status literals не меняются:
  - `done`
  - `verify_needed`
  - `failed`
  - `approval_required`
  - `blocked`
- `verify_needed` не downcast-ится в `done` только потому, что dispatch был
  physical;
- `set_value` не получает hidden typing fallback;
- `perform_secondary_action` не получает hidden context-menu fallback;
- `type_text` не получает hidden clipboard path;
- physical input не маскируется под “semantic success”;
- Windows shared cursor/input-stream reality не скрывается narrative-ом про
  “второй курсор”.

## 5. Exact source pack

### A. Product and policy

- [AGENTS.md](../../../AGENTS.md)
- [index.md](../../product/index.md)
- [okno-spec.md](../../product/okno-spec.md)
- [okno-vision.md](../../product/okno-vision.md)
- [okno-roadmap.md](../../product/okno-roadmap.md)

### B. Current surface and policy framing

- [computer-use-win-surface.md](../../architecture/computer-use-win-surface.md)
- [observability.md](../../architecture/observability.md)
- [openai-computer-use-interop.md](../../architecture/openai-computer-use-interop.md)
- [capability-design-policy.md](../../architecture/capability-design-policy.md)
- [reference-research-policy.md](../../architecture/reference-research-policy.md)

### C. Current and future plans

- [computer-use-win-physical-execution-policy-hardening.md](computer-use-win-physical-execution-policy-hardening.md)
- [okno-agent-native-runtime-leadership.md](../planned/okno-agent-native-runtime-leadership.md)
- [okno-native-visual-performance.md](../planned/okno-native-visual-performance.md)
- [okno-release-and-install-packaging.md](okno-release-and-install-packaging.md)

### D. Completed history defining current action surface

- [completed-2026-04-20-windows-input.md](../completed/completed-2026-04-20-windows-input.md)
- [completed-2026-04-28-computer-use-win-next-actions.md](../completed/completed-2026-04-28-computer-use-win-next-actions.md)
- [completed-2026-05-01-computer-use-win-screenshot-first-hardening.md](../completed/completed-2026-05-01-computer-use-win-screenshot-first-hardening.md)

### E. Generated truth

- [computer-use-win-interfaces.md](../../generated/computer-use-win-interfaces.md)
- [project-interfaces.md](../../generated/project-interfaces.md)
- [commands.md](../../generated/commands.md)
- [test-matrix.md](../../generated/test-matrix.md)
- [CHANGELOG.md](../../CHANGELOG.md)

### F. File-level implementation owners

Public contract/publication:

- [ToolNames.cs](../../../src/WinBridge.Runtime.Tooling/ToolNames.cs)
- [ToolDescriptions.cs](../../../src/WinBridge.Runtime.Tooling/ToolDescriptions.cs)
- [ToolContractManifest.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractManifest.cs)
- [ToolContractExporter.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractExporter.cs)
- [ComputerUseWinContracts.cs](../../../src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs)

Shared action lifecycle:

- [ComputerUseWinActionRequestExecutor.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionRequestExecutor.cs)
- [ComputerUseWinActionFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionFinalizer.cs)
- [ComputerUseWinActionObservability.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionObservability.cs)
- [ComputerUseWinToolResultFactory.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinToolResultFactory.cs)
- [ComputerUseWinAuditDataBuilder.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAuditDataBuilder.cs)
- [ComputerUseWinFailureCodeMapper.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinFailureCodeMapper.cs)
- [ComputerUseWinRequestContractValidator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinRequestContractValidator.cs)

Action-specific current owners:

- [ComputerUseWinClickContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinClickContract.cs)
- [ComputerUseWinClickExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinClickExecutionCoordinator.cs)
- [ComputerUseWinPressKeyContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinPressKeyContract.cs)
- [ComputerUseWinPressKeyExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinPressKeyExecutionCoordinator.cs)
- [ComputerUseWinTypeTextContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextContract.cs)
- [ComputerUseWinTypeTextExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTypeTextExecutionCoordinator.cs)
- [ComputerUseWinScrollContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollContract.cs)
- [ComputerUseWinScrollExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinScrollExecutionCoordinator.cs)
- [ComputerUseWinDragContract.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinDragContract.cs)
- [ComputerUseWinDragExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinDragExecutionCoordinator.cs)
- [ComputerUseWinSetValueExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinSetValueExecutionCoordinator.cs)
- [ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinPerformSecondaryActionExecutionCoordinator.cs)

State/proof/continuity:

- [ComputerUseWinGetAppStateHandler.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateHandler.cs)
- [ComputerUseWinGetAppStateFinalizer.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs)
- [ComputerUseWinAppStateObserver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs)
- [ComputerUseWinActionSuccessorObservation.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinActionSuccessorObservation.cs)
- [ComputerUseWinStateStore.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStateStore.cs)
- [ComputerUseWinStoredStateResolver.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinStoredStateResolver.cs)
- [ComputerUseWinWindowContinuityProof.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinWindowContinuityProof.cs)
- [ComputerUseWinTargetPolicy.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinTargetPolicy.cs)
- [ComputerUseWinIdentityModel.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinIdentityModel.cs)
- [ComputerUseWinLiveWindowSelector.cs](../../../src/WinBridge.Server/ComputerUse/ComputerUseWinLiveWindowSelector.cs)

Diagnostics/redaction/audit substrate:

- [AuditLog.cs](../../../src/WinBridge.Runtime.Diagnostics/AuditLog.cs)
- [AuditEvent.cs](../../../src/WinBridge.Runtime.Diagnostics/AuditEvent.cs)
- [AuditConstants.cs](../../../src/WinBridge.Runtime.Diagnostics/AuditConstants.cs)
- [AuditToolContext.cs](../../../src/WinBridge.Runtime.Diagnostics/AuditToolContext.cs)
- [AuditPayloadRedactor.cs](../../../src/WinBridge.Runtime.Diagnostics/AuditPayloadRedactor.cs)
- [IAuditPayloadRedactor.cs](../../../src/WinBridge.Runtime.Diagnostics/IAuditPayloadRedactor.cs)

Low-level input substrate:

- [InputExecutionGate.cs](../../../src/WinBridge.Runtime.Windows.Input/InputExecutionGate.cs)
- [InputForegroundTargetBoundaryPolicy.cs](../../../src/WinBridge.Runtime.Windows.Input/InputForegroundTargetBoundaryPolicy.cs)
- [InputTargetPreflightPolicy.cs](../../../src/WinBridge.Runtime.Windows.Input/InputTargetPreflightPolicy.cs)
- [InputCommittedSideEffectEvidencePolicy.cs](../../../src/WinBridge.Runtime.Windows.Input/InputCommittedSideEffectEvidencePolicy.cs)
- [Win32InputService.cs](../../../src/WinBridge.Runtime.Windows.Input/Win32InputService.cs)
- [Win32InputPlatform.cs](../../../src/WinBridge.Runtime.Windows.Input/Win32InputPlatform.cs)
- [Win32InputSecurityProbe.cs](../../../src/WinBridge.Runtime.Windows.Input/Win32InputSecurityProbe.cs)

Semantic services:

- [Win32UiAutomationSetValueService.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationSetValueService.cs)
- [Win32UiAutomationScrollService.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationScrollService.cs)
- [Win32UiAutomationSecondaryActionService.cs](../../../src/WinBridge.Runtime.Windows.UIA/Win32UiAutomationSecondaryActionService.cs)

Tests/control plane:

- [ToolContractManifestTests.cs](../../../tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs)
- [ToolContractExporterTests.cs](../../../tests/WinBridge.Runtime.Tests/ToolContractExporterTests.cs)
- [AuditLogTests.cs](../../../tests/WinBridge.Runtime.Tests/AuditLogTests.cs)
- [AuditPayloadRedactorTests.cs](../../../tests/WinBridge.Runtime.Tests/AuditPayloadRedactorTests.cs)
- [ComputerUseWinActionAndProjectionTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinActionAndProjectionTests.cs)
- [ComputerUseWinFinalizationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinFinalizationTests.cs)
- [ComputerUseWinArchitectureTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinArchitectureTests.cs)
- [ComputerUseWinObservationTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ComputerUseWinObservationTests.cs)
- [McpProtocolSmokeTests.cs](../../../tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs)
- [smoke.ps1](../../../scripts/smoke.ps1)
- [verify.ps1](../../../scripts/codex/verify.ps1)
- [prove-computer-use-win-cache-install.ps1](../../../scripts/codex/prove-computer-use-win-cache-install.ps1)

### G. Official docs actually studied for this plan

OpenAI:

- [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [Images and vision](https://developers.openai.com/api/docs/guides/images-vision)
- [MCP and Connectors](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Codex config reference](https://developers.openai.com/codex/config-reference)
- [Codex MCP](https://developers.openai.com/codex/mcp)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)

MCP:

- [Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [Schema](https://modelcontextprotocol.io/specification/2025-11-25/schema)
- [Lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)
- [Security best practices](https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices)

Microsoft / Win32 / UIA:

- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos)
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [UI Automation Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)
- [UI Automation Control Patterns Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview)
- [About the Text and TextRange Control Patterns](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-about-text-and-textrange-patterns)
- [Value Control Pattern](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingvalue)
- [Security Considerations for Assistive Technologies](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
- [Raw Input Overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)
- [Virtual HID Framework](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)

### H. Reference usage rule for this plan

Reference repos, если понадобятся уже при implementation, используются только
после этого source pack и только по policy:

- сначала internal docs;
- потом official docs;
- потом targeted local reference cohorts;
- в tracked docs фиксируются **выводы `Okno`**, а не каталог repo names.

## 6. Official constraints

### 6.1. OpenAI constraints

Из official OpenAI docs для phase-1 binding facts такие:

- built-in `computer use` loop нормализует screenshot-first execution:
  - первый turn часто запрашивает screenshot;
  - модель возвращает `actions[]`;
  - harness исполняет их по порядку;
  - потом возвращает **updated screenshot**;
- existing custom harness **не нужно перестраивать** вокруг built-in
  `computer` tool, если уже есть mature local/MCP harness с retries,
  observability и guardrails;
- для computer use screenshots рекомендуется `detail: "original"`; если image
  downscale-ится, coordinate basis должен быть явно remapped обратно в original
  coordinate space;
- tool narrowing бывает на client-side layer:
  - Responses API `allowed_tools`
  - Codex `mcp_servers.<id>.enabled_tools`
  - Codex `mcp_servers.<id>.disabled_tools`
  это не server-owned runtime contract `Okno`;
- approvals для sensitive MCP/connector actions нормальны и belong to human or
  client approval flow, но это не оправдывает server-side ложный success;
- Codex MCP поддерживает **local STDIO servers** как first-class path;
- Codex app on Windows уже нормализует Windows-native + PowerShell-native path.

Вывод для phase-1:

- `Okno` должен сохранить свой current local STDIO runtime path;
- `executionFacts` belong to server truth layer;
- tool narrowing belongs to client layer;
- screenshot-first and successor screenshot remain first-class outputs;
- `computer-use-win` не обязан копировать built-in DTO, но обязан быть
  совместим по discipline.

### 6.2. MCP constraints

Из official MCP spec для phase-1 важны следующие ограничения:

- MCP source pack для этого плана должен быть привязан к актуальной spec line
  `2025-11-25`, а не к более раннему `2025-06-18` snapshot;

- tool-level errors должны materialize-иться как tool result, а не как generic
  transport failure;
- `structuredContent` и `content` могут coexist;
- image content blocks являются нормальной частью tool result contract;
- `isError` отличает tool-level failure от protocol-level failure;
- `outputSchema` существует как spec concept, но phase-1 не должен строиться
  вокруг выдуманного exporter path, если current MCP server SDK ещё не делает
  этот surface canonical;
- local `STDIO` остаётся правильной trust boundary для local MCP servers;
- scope minimization, approval/consent и explicit trust boundary остаются
  обязательными;
- server отвечает за truthful result semantics, а client — за allow-lists,
  approval UX и orchestration policy.

Вывод для phase-1:

- `executionFacts` должны жить в tool result and observability, а не в hidden
  console/debug channel;
- install/publication proof должен проверять реальный materialized server
  surface, а не chat-only claims;
- phase-1 не должен превращать ambiguous runtime states в generic transport
  error.

### 6.3. Microsoft / Win32 / UIA constraints

Из official Microsoft docs следуют жёсткие product constraints:

- `SendInput` работает через **shared keyboard/mouse input stream**;
- `SendInput` subject to UIPI, и Windows не даёт честного отдельного
  `GetLastError`-class proof “это был именно UIPI block”;
- `SetCursorPos` двигает **shared system cursor**, а не “второй курсор”;
- `SetForegroundWindow` ограничен policy Windows и может быть отклонён;
- `GetForegroundWindow` и related focus checks полезны, но их надо трактовать
  как live boundary, а не как вечную гарантию;
- UI Automation — semantic layer, но не universal executor guarantee;
- `TextPattern` является read-oriented pattern, а не общей writable guarantee;
- programmatic text set честно опирается на `ValuePattern` /
  `RangeValuePattern`, а не на wishful semantic typing;
- `ValuePattern` / `TextPattern` реально помогают различать semantic set vs
  physical typing;
- `uiAccess` — не ordinary product path и не общий bypass для shipped local
  runtime;
- `Raw Input` и `VHF` существуют, но выводят продукт в другой hardware/driver
  class и потому служат тут только как non-goal proof.

Вывод для phase-1:

- `expected_physical` должен быть first-class truth, а не awkward fallback;
- `foregroundIntegrity` можно публиковать только как
  `accepted|blocked|unknown`, без fake precise UIPI diagnosis;
- `systemCursorMoved`/physical keyboard usage надо описывать честно как shared
  OS resource effects;
- “second cursor” и driver path не могут входить в эту ветку даже как
  “следующий маленький шаг”.

## 7. Current gap synthesis

Phase-1 должен закрыть не одну багу, а один coherent truth gap:

1. **Public result gap**  
   Public action result пока не сообщает execution mode явно.

2. **Classification gap**  
   `riskClass`, `dispatchPath`, `fallbackUsed` already exist, но разбросаны по
   отдельным coordinators и observability context.

3. **Policy gap**  
   Confirmation semantics already exist, но распределены между contracts,
   `TargetPolicy`, per-action rules и fallback branches.

4. **Proof gap**  
   `observeAfter=true` already exists, но current public result всё ещё не
   связывает action truth, proof basis и physical-policy facts в один слой.

5. **Foreground/integrity gap**  
   Low-level input substrate already знает foreground/integrity boundaries, но
   public `computer-use-win` result почти не объясняет их как product truth.

6. **Control-plane gap**  
   Current repo не держит отдельный minimal proof-smoke companion именно для
   physical-policy semantics.

7. **Scope risk**  
   Без phase-specific plan этот workstream слишком легко расползается в:
   - playbooks/hints
   - `region_capture`
   - `uia_action`
   - lease/identity substrate
   - packaging/publication wave

## 8. Design forks to close before implementation

### A. Public result shape

**Decision**

- `executionFacts` добавляется как **новое nested public field** в
  `ComputerUseWinActionResult`.
- Existing top-level fields **не reshaped** и не переименовываются.
- Existing status literals **не меняются**.
- `actionReceipt` **не входит** в phase-1; это later slice `23`.
- `recommendedRecovery` / `semanticDiff` **не входят** в phase-1.

**Why**

- current public payload уже используется tests, docs и installed proof;
- additive nested field даёт backward-compatible evolution;
- `executionFacts` нужен как new truth layer, а не как повод переделать весь
  result contract.

**Recommended public shape**

```json
{
  "status": "verify_needed",
  "refreshStateRecommended": false,
  "failureCode": null,
  "reason": null,
  "targetHwnd": 123456,
  "elementIndex": 7,
  "executionFacts": {
    "dispatchClass": "expected_physical",
    "executor": "win32_pointer_click",
    "confirmationRequired": true,
    "confirmationSatisfied": true,
    "fallbackUsed": false,
    "targetProof": "uia_revalidated",
    "stateTokenPresent": true,
    "captureReferencePresent": false,
    "windowContinuity": "accepted",
    "foregroundIntegrity": "accepted",
    "physicalPointerUsed": true,
    "physicalKeyboardUsed": false,
    "systemCursorMoved": true,
    "observeAfterRequested": true,
    "successorStateAvailable": true
  },
  "successorState": { "...": "existing payload" }
}
```

**Public vs audit-only split**

Public `executionFacts` includes only safe, operator-relevant truth:

- `dispatchClass`
- `executor`
- `confirmationRequired`
- `confirmationSatisfied`
- `fallbackUsed`
- `targetProof`
- `stateTokenPresent`
- `captureReferencePresent`
- `windowContinuity`
- `foregroundIntegrity`
- `physicalPointerUsed`
- `physicalKeyboardUsed`
- `systemCursorMoved`
- `observeAfterRequested`
- `successorStateAvailable`

Audit/event/artifact-only fields remain:

- `failure_stage`
- `exception_type`
- raw child artifact paths
- current telemetry buckets (`text_length`, `value_length`, etc.)
- `window_id_present`
- `runtime_state`
- any future low-level phase markers

### B. Classification model

**Decision**

Phase-1 public envelope uses **two layers**, not one:

- coarse product truth: `dispatchClass`
- concrete executor fact: `executor`

`dispatchClass` values:

- `semantic`
- `expected_physical`
- `fallback_physical`

`executor` is a safe stable string owned by `Okno`, not a raw leak of every
internal branch name.

**Per-action phase-1 mapping**

| Action path | Phase-1 `dispatchClass` | Phase-1 `executor` direction |
| --- | --- | --- |
| `set_value` success | `semantic` | `uia_value_pattern` or `uia_range_value_pattern` |
| `perform_secondary_action` success | `semantic` | `uia_toggle` / resolved `uia_*` pattern |
| `scroll(elementIndex)` semantic path | `semantic` | `uia_scroll_pattern` |
| `click(elementIndex)` | `expected_physical` | `win32_pointer_click` |
| `click(point)` | `expected_physical` | `win32_pointer_click` |
| `press_key` | `expected_physical` | `win32_sendinput_keypress` |
| `drag(...)` | `expected_physical` | `win32_sendinput_drag` |
| `scroll(point)` | `expected_physical` | `win32_sendinput_wheel` |
| `type_text` normal focused editable path | `expected_physical` | `win32_sendinput_unicode` |
| `type_text` focused fallback | `fallback_physical` | `win32_sendinput_unicode` |
| `type_text` coordinate-confirmed fallback | `fallback_physical` | `capture_pixels_text_input` |

**Key clarifications**

- `press_key` — это `expected_physical`, хотя курсор не двигается.
- `click(elementIndex)` — это **не semantic execution**, потому что semantic
  layer здесь доказывает target, но final dispatch всё равно physical.
- `set_value` / `perform_secondary_action` — semantic-only in phase-1.
- Phase-1 **не вводит** generic browser/terminal executors.

### C. Confirmation semantics

**Decision**

Phase-1 выносит confirmation truth в один общий policy layer, но **не
перепридумывает** всю approval architecture.

Unified phase-1 semantics обязаны покрыть:

- coordinate click;
- dangerous `press_key`;
- `type_text` focused fallback;
- `type_text` coordinate-confirmed fallback;
- coordinate scroll;
- any drag with coordinate endpoint;
- risky semantic targets for `click` and `perform_secondary_action`.

**Important boundary**

`allowFocusedFallback=true` остаётся:

- shipped bounded `type_text` switch;
- first-class example `fallback_physical`;
- **не** общим precedent “любой action можно теперь переводить в focused
  fallback”.

**Policy owner decision**

- existing keyword/process heuristics stay in `ComputerUseWinTargetPolicy`;
- new cross-action confirmation/materialization rules должны жить в отдельном
  phase-1 owner рядом с action lifecycle, а не разрастаться только внутри
  `TargetPolicy`.

### D. Foreground / integrity / stale-state facts

**Decision**

Phase-1 public `executionFacts` уже должен честно materialize-ить:

- `stateTokenPresent`
- `captureReferencePresent`
- `windowContinuity`
- `foregroundIntegrity`

Where:

- `windowContinuity`: `accepted|failed|best_effort|unknown`
- `foregroundIntegrity`: `accepted|blocked|unknown`

**Explicit deferral**

Phase-1 **не** публикует top-level public `foregroundChanged` boolean, because
current runtime cannot promise a stable exact before/after focus-transition fact
for every path without opening a bigger activation state model.

If needed, activation-side traces stay audit-only in phase-1.

**UIPI honesty rule**

Phase-1 does **not** invent public `uipi_blocked` precision. Existing public
failure stays:

- `target_integrity_blocked`

with public `foregroundIntegrity=blocked`, because Windows does not provide a
universally honest separate diagnosis path here.

### E. Minimal proof-smoke

**Decision**

Minimal proof-smoke belongs to the **same phase-1 plan** and must ship as a
companion package in the same branch, not “later отдельной веткой”.

Implementation can still be cut as a separate package/commit inside this branch,
but phase-1 is not complete without it.

### F. Boundary to later slices

Phase-1 intentionally leaves these to later work:

- app playbooks / capability hints consume `dispatchClass`, but do not ship
  here;
- `windows.region_capture` stays the next visual proof slice and will later
  extend `targetProof` with `region`;
- `windows.uia_action` stays a separate semantic executor slice;
- stable target identity / lease substrate stays later; phase-1 may expose
  `windowContinuity`, but does not create lease semantics;
- `actionReceipt`, `semanticDiff`, `recommendedRecovery` stay in later proof
  envelope slice;
- no browser/Electron lane, no terminal lane.

## 9. File-level integration map

### 9.1. Public contract and publication owners

| File group | Phase-1 role |
| --- | --- |
| `ToolNames.cs` | Скорее всего без новых tool names; touch only if result-facing manifest helpers require a new contract note, not a new tool. |
| `ToolDescriptions.cs` | Update wording where public descriptions should mention `executionFacts` and current semantic/physical truth more precisely. |
| `ToolContractManifest.cs` | Keep tool count stable; update public narrative/notes so generated interfaces describe `executionFacts` and physical-policy truth accurately. |
| `ToolContractExporter.cs` | Ensure generated docs/export pick up changed descriptions/notes and any new command surface for proof-smoke. |
| `ComputerUseWinContracts.cs` | Add public `ComputerUseWinExecutionFacts` contract and extend `ComputerUseWinActionResult`. |

### 9.2. Shared action lifecycle owners

| File | Phase-1 responsibility |
| --- | --- |
| `ComputerUseWinActionRequestExecutor.cs` | Attach phase-1 `observeAfter` and successor-state facts into the new envelope path. |
| `ComputerUseWinActionFinalizer.cs` | Materialize `executionFacts` into public result and keep `isError` semantics honest. |
| `ComputerUseWinActionObservability.cs` | Extend event/artifact schema from partial fields to the phase-1 envelope while keeping safe-field discipline. |
| `ComputerUseWinToolResultFactory.cs` | If needed, centralize new payload construction helpers so handlers do not duplicate result shaping. |
| `ComputerUseWinAuditDataBuilder.cs` | Extend audit completion with safe phase-1 facts without leaking raw payloads. |
| `ComputerUseWinFailureCodeMapper.cs` | Reuse current codes where possible; avoid speculative new public failures. |
| `ComputerUseWinRequestContractValidator.cs` | Keep malformed-request boundary unchanged; `executionFacts` is not an excuse to blur `invalid_request`. |

### 9.3. New or extracted phase-1 owners

Phase-1 should **prefer one new shared owner** instead of spreading mapping
logic across all coordinators again.

Recommended additions:

- `ComputerUseWinExecutionFactsBuilder.cs`
  - central mapping from action outcome + stored state + current coordinator
    facts -> public `executionFacts`
- `ComputerUseWinPhysicalExecutionPolicy.cs`
  - central confirmation/dispatch-class policy for risky physical paths

If the implementation can prove that one new file is enough, it is acceptable to
collapse both responsibilities into a single owner. What is **not** acceptable
is keeping all phase-1 decisions copy-pasted inside coordinators.

### 9.4. Per-action integration owners

| File group | Phase-1 role |
| --- | --- |
| `ClickContract` + `ClickExecutionCoordinator` | Reclassify click as `expected_physical`; preserve semantic target proof vs coordinate proof distinction. |
| `PressKeyContract` + `PressKeyExecutionCoordinator` | Mark as `expected_physical` with keyboard-stream facts and unified confirmation for dangerous combos. |
| `TypeTextContract` + `TypeTextExecutionCoordinator` | The most important current fallback owner: distinguish normal expected physical typing from focused/coordinate fallback physical typing. |
| `ScrollContract` + `ScrollExecutionCoordinator` | Distinguish semantic UIA scroll from expected physical wheel path. |
| `DragContract` + `DragExecutionCoordinator` | Classify all drag paths as physical, but differentiate semantic endpoint proof vs coordinate endpoint proof and confirmation. |
| `SetValueExecutionCoordinator` | Classify as semantic; do not add typing fallback. |
| `PerformSecondaryActionExecutionCoordinator` | Classify as semantic; do not add context-menu fallback. |

### 9.5. State / proof / successor-state owners

| File group | Phase-1 role |
| --- | --- |
| `ComputerUseWinGetAppState*` | Preserve screenshot-first successor-state semantics; no broad `get_app_state` redesign. |
| `ComputerUseWinActionSuccessorObservation.cs` | Reuse current owner for `observeAfter` facts and map them into `executionFacts.successorStateAvailable`. |
| `ComputerUseWinStateStore.cs` / `StoredStateResolver.cs` | Source of `stateTokenPresent` and stale-state truth; no lease substrate work here. |
| `ComputerUseWinWindowContinuityProof.cs` | Source of public `windowContinuity`; phase-1 should expose its truth without inventing a new identity model. |
| `ComputerUseWinIdentityModel.cs` / `LiveWindowSelector.cs` | Read-only influence on continuity truth; no new selector family in phase-1. |

### 9.6. Diagnostics / redaction / audit owners

| File group | Phase-1 role |
| --- | --- |
| `AuditLog.cs` / `AuditEvent.cs` / `AuditConstants.cs` | Preserve current event transport and safe naming discipline. |
| `AuditPayloadRedactor.cs` / `IAuditPayloadRedactor.cs` | Ensure new `executionFacts` fields remain safe and do not accidentally carry raw text/value/key or raw exception content. |

### 9.7. Low-level input substrate owners

| File group | Phase-1 role |
| --- | --- |
| `InputTargetPreflightPolicy.cs` | Source for truthful `foregroundIntegrity` / minimized / cross-session / integrity block boundaries. |
| `InputForegroundTargetBoundaryPolicy.cs` | Source for final-dispatch foreground boundary truth. |
| `Win32InputSecurityProbe.cs` | Source for integrity/uiAccess probing limits; phase-1 should surface only what this layer can honestly prove. |
| `InputCommittedSideEffectEvidencePolicy.cs` / `Win32InputService.cs` / `Win32InputPlatform.cs` | Continue to own factual committed physical side-effect evidence; phase-1 consumes this, not rewrites it. |

### 9.8. Tests and control plane owners

| File group | Phase-1 role |
| --- | --- |
| `ToolContractManifestTests.cs` / `ToolContractExporterTests.cs` | Pin docs/export/generated contract drift. |
| `AuditLogTests.cs` / `AuditPayloadRedactorTests.cs` | Pin safe-field and redaction behavior. |
| `ComputerUseWinFinalizationTests.cs` | Primary owner for public result + action artifact + runtime event envelope truth. |
| `ComputerUseWinActionAndProjectionTests.cs` | Primary owner for per-action classification and fallback semantics. |
| `McpProtocolSmokeTests.cs` | Primary live-like integration owner for public schema/result and helper-backed scenarios. |
| `scripts/smoke.ps1` | Keep broad engine smoke separate; only extend if summary wiring is needed. |
| `scripts/ci.ps1` / `scripts/codex/verify.ps1` | Sequential integration of the new companion proof-smoke. |
| `scripts/codex/prove-computer-use-win-cache-install.ps1` | Required if public schema/result shape changes. |

## 10. Sequential implementation plan

Эта ветка должна реализовываться **строго последовательно в одном worktree**.
Причины:

- repo verification loop уже нормализован как sequential;
- classification/proof logic затрагивает общий action lifecycle;
- parallel code edits здесь повышают риск несогласованности public result,
  observability и proof harness.

Package map сохраняется, но используется только как grouping. Реальная
implementation execution order ниже линейный:

- Package A = steps `0-1`
- Package B = step `2`
- Package C = steps `3-6`
- Package D = step `7`
- Package E = steps `8-9`

### 10.1. Where DDD and TDD are actually justified

**DDD is justified** only in the narrow phase-1 domain seam that сейчас
размазан между coordinators:

- public `executionFacts` model;
- `dispatchClass` truth;
- shared physical-execution policy;
- shared mapping from runtime outcome to product truth.

Здесь DDD полезен, потому что он явно отделяет:

- public product truth;
- low-level runtime facts;
- policy decisions;
- observability materialization.

Это **не** повод разворачивать broad aggregate/repository language вокруг всего
`computer-use-win`; достаточно узких domain owners:

- `ComputerUseWinExecutionFacts`
- `ComputerUseWinExecutionFactsBuilder`
- `ComputerUseWinPhysicalExecutionPolicy`

или одного объединённого owner, если это реально проще и не плодит дубли.

**TDD is justified** там, где поведение externally observable и легко pin-ится
до кода:

- public result contract;
- event/artifact shape;
- per-action classification;
- stale/foreground fail-close;
- proof-smoke scenarios;
- installed/publication proof when result surface changes.

TDD **не нужно применять формально** для:

- bulk doc refresh;
- generated-file refresh;
- простых mechanical rewires без новой поведенческой ветки.

Для шагов, помеченных как `TDD: yes`, implementation protocol обязан быть
одинаковым:

1. добавить минимальный failing test/assertion;
2. получить `red`;
3. реализовать минимальный код до `green`;
4. убрать дубли / зафиксировать owner seam;
5. снова прогнать тот же scope до `green`.

### 10.1.1. Progress tracking protocol

Этот plan должен работать не только как design note, но и как execution
control surface. Поэтому progress отмечается прямо в этом файле.

Обязательные правила:

- мастер-прогресс ведётся через markdown checkboxes в subsection
  `10.1.2 Master progress checklist`;
- `10.12 Execution log` использует **один зарезервированный slot на один
  шаг**;
- step считается завершённым только если одновременно выполнены все условия:
  - код/доки этапа закончены;
  - verification gate этого шага зелёный;
  - step slot в `10.12 Execution log` заполнен completed mini-report;
  - создан отдельный commit этого этапа;
- следующий step нельзя начинать, пока предыдущий не получил:
  - `[x]` в master checklist
  - commit hash
  - краткий execution report в log;
- если шаг начат, но ещё не закончен, исполнитель отмечает это только в
  execution log как `status: in_progress`; checkbox переводится в `[x]`
  только после полного completion gate;
- до статуса `done` step slot можно обновлять по месту:
  - `pending -> in_progress -> done`;
- после того как step получил `done`, его slot считается frozen; дальше
  допустимы только:
  - точечная корректировка явной фактической ошибки
  - добавление missing evidence reference, если сам факт completion не меняется.

### 10.1.2. Master progress checklist

- [x] Step `0` — freeze the baseline and failure surface
- [x] Step `1` — introduce the phase-1 domain seam
- [ ] Step `2` — add the additive public `executionFacts` envelope
- [ ] Step `3` — make lifecycle owners the single materialization path
- [ ] Step `4` — migrate semantic paths first
- [ ] Step `5` — migrate expected physical paths
- [ ] Step `6` — migrate fallback physical typing and failure boundaries
- [ ] Step `7` — add the companion minimal proof-smoke
- [ ] Step `8` — sync docs, generated surfaces and installed-copy proof
- [ ] Step `9` — final sequential closure and acceptance pass

### 10.1.3. Commit discipline

Каждый этап этого плана обязан коммититься отдельно. Это часть definition of
done, а не post-facto hygiene.

Жёсткие правила:

- один завершённый step `0..9` -> минимум один отдельный commit;
- по умолчанию один step должен соответствовать одному commit;
- объединять два соседних шага в один commit нельзя, кроме случая, когда
  активный plan будет отдельно обновлён и зафиксирует, почему такое слияние
  было неизбежно и почему оно не ломает traceability;
- commit должен делаться после зелёного verification gate шага и до начала
  следующего шага;
- в mini-report этапа обязательно записываются:
  - commit hash
  - что вошло в commit
  - какой verification scope был зелёным;
- если шаг дал только docs/control-plane delta, он всё равно коммитится
  отдельно, если этот шаг отмечается как completed.

Практический смысл этого правила:

- сохраняется воспроизводимый stage-by-stage rollout;
- review и rollback остаются локальными;
- later closure report может ссылаться на commits по шагам, а не на один
  размытый branch diff.

### 10.1.4. Stage mini-report template

После завершения каждого step исполнитель должен заполнить его slot в
`10.12 Execution log` блоком такого вида:

```md
#### Step N — <step title>
- status: done
- completed_at: YYYY-MM-DD HH:MM UTC
- owner: <agent/user if useful>
- commit: <full or short hash>
- scope:
  - <что реально вошло в шаг>
- verification:
  - <какие тесты/скрипты запущены>
  - <что прошло>
- decisions:
  - <важные локальные решения этого шага>
- residual_risks:
  - <что осталось рискованным, но допустимым>
- next_unblocked_step: <следующий номер шага>
```

Если step ещё выполняется и нужен checkpoint посередине, допускается временный
блок:

```md
#### Step N — <step title>
- status: in_progress
- current_focus: <что делается сейчас>
- blocker: <если есть>
```

Но step slot потом всё равно должен быть доведён до полноценного `done`
report до начала следующего шага.

### 10.2. Step 0 — Freeze the baseline and failure surface

**Package:** `A`

**Preconditions**

- active phase-1 doc accepted as current source of truth;
- branch stays docs/planning-only up to this point;
- current action wave and `observeAfter` behavior remain the shipped baseline.

**Depends on**

- none

**DDD**

- no

**TDD**

- yes
- justification: characterization tests here are the cheapest way to prevent
  accidental drift later.

**Work**

- add or tighten characterization tests that freeze:
  - current top-level `ComputerUseWinActionResult` fields;
  - current event/artifact safe fields;
  - current status literals;
  - current semantic vs physical behavior as it exists today, before phase-1
    rematerializes it into `executionFacts`;
- explicitly pin the absence of:
  - `actionReceipt`
  - new public tool names
  - hidden clipboard/type shortcuts.

**Constraints**

- no contract mutation yet;
- no coordinator refactor yet;
- tests should describe the current baseline, not the final phase-1 shape.

**Expected result**

- repo has a stable behavioral baseline;
- later red bars clearly point to intentional phase-1 deltas instead of
  accidental breakage.

**Verification gate**

- targeted green:
  - `ComputerUseWinFinalizationTests`
  - `ComputerUseWinActionAndProjectionTests`
  - `McpProtocolSmokeTests`

**Unblocks**

- step `1`

### 10.3. Step 1 — Introduce the phase-1 domain seam

**Package:** `A`

**Preconditions**

- step `0` green

**Depends on**

- step `0`

**DDD**

- yes
- justification: this is the narrow point where the current architecture needs a
  shared domain owner instead of more coordinator-local strings.

**TDD**

- yes
- justification: the builder/policy seam is easy to specify with unit tests
  before wiring it into handlers.

**Work**

- introduce the minimal shared phase-1 domain surface:
  - `ComputerUseWinExecutionFacts`
  - stable value sets for `dispatchClass`, `targetProof`,
    `foregroundIntegrity`;
  - one shared owner for building facts;
  - one shared owner for physical confirmation/materialization policy, unless
    both responsibilities can be cleanly collapsed into one file;
- decide now which current coordinator-local fields remain inputs into the new
  seam:
  - `riskClass`
  - `dispatchPath`
  - `fallbackUsed`
  - `confirmationRequired`
  - `confirmed`
  - `stateTokenPresent`
  - `captureReferencePresent`.

**Constraints**

- do not yet thread the new domain object through the full result path;
- do not add speculative fields beyond section `8`;
- do not preserve duplicate legacy mapping helpers “на всякий случай”.

**Expected result**

- the repo has an explicit phase-1 domain boundary;
- later steps can plug actions into one owner instead of duplicating logic.

**Verification gate**

- new unit tests around the new builder/policy owner go `red -> green`;
- existing characterization tests from step `0` remain green.

**Unblocks**

- steps `2-6`

### 10.4. Step 2 — Add the additive public `executionFacts` envelope

**Package:** `B`

**Preconditions**

- step `1` green

**Depends on**

- step `1`

**DDD**

- yes, but only through the new phase-1 domain seam from step `1`

**TDD**

- yes
- justification: public result shape is stable, externally visible and easy to
  pin before code changes.

**Work**

- extend `ComputerUseWinActionResult` with nested `executionFacts`;
- keep these top-level fields unchanged:
  - `status`
  - `refreshStateRecommended`
  - `failureCode`
  - `reason`
  - `targetHwnd`
  - `elementIndex`
  - `successorState`
  - `successorStateFailure`;
- update finalizer/result factory to serialize the additive field;
- keep `isError` semantics exactly as before.

**Constraints**

- additive-only public mutation;
- no new request args;
- no renamed status literals;
- no `actionReceipt`.

**Expected result**

- every action result can carry the new envelope without breaking existing
  top-level consumers;
- the branch has crossed the public contract boundary once, cleanly and
  deliberately.

**Verification gate**

- contract/serialization tests go `red -> green`;
- targeted finalizer tests confirm:
  - additive field exists
  - existing top-level fields still materialize
  - `isError` behavior is unchanged.

**Unblocks**

- steps `3-9`

### 10.5. Step 3 — Make lifecycle owners the single materialization path

**Package:** `C`

**Preconditions**

- step `2` green

**Depends on**

- steps `1-2`

**DDD**

- yes
- justification: this is where domain truth must be owned by lifecycle, not by
  individual action handlers.

**TDD**

- yes
- justification: event/artifact/result parity is externally inspectable and
  easy to regress.

**Work**

- thread the new execution-facts owner through:
  - `ComputerUseWinActionRequestExecutor`
  - `ComputerUseWinActionFinalizer`
  - `ComputerUseWinActionObservability`
  - `ComputerUseWinAuditDataBuilder`
- define one canonical materialization path for:
  - public `executionFacts`
  - event/artifact fields
  - safe audit completion data;
- keep the public vs audit-only split from section `8` explicit in code.

**Constraints**

- no per-handler custom JSON shaping after this point;
- raw text/value/key/point/exception message remain redacted or suppressed;
- current malformed-request boundary remains intact.

**Expected result**

- one lifecycle-owned truth path exists;
- later per-action steps only supply facts into that path.

**Verification gate**

- `AuditLogTests` and `AuditPayloadRedactorTests` stay green;
- finalization tests confirm result/event/artifact parity;
- no new drift in current safe fields.

**Unblocks**

- steps `4-7`

### 10.6. Step 4 — Migrate semantic paths first

**Package:** `C`

**Preconditions**

- step `3` green

**Depends on**

- steps `1-3`

**DDD**

- reuse the shared phase-1 domain seam

**TDD**

- yes
- justification: semantic paths are the least ambiguous positive cases and give
  the cleanest first green on `dispatchClass=semantic`.

**Work**

- integrate `dispatchClass=semantic` for:
  - `set_value`
  - `perform_secondary_action`
  - semantic `scroll(elementIndex)`;
- map stable semantic executors:
  - `uia_value_pattern`
  - `uia_range_value_pattern`
  - `uia_toggle` / resolved `uia_*`
  - `uia_scroll_pattern`;
- expose `targetProof` and `foregroundIntegrity` consistently for these paths;
- remove coordinator-local shaping that becomes redundant once shared
  materialization exists.

**Constraints**

- no hidden typing fallback for `set_value`;
- no context-menu fallback for `perform_secondary_action`;
- no broad semantic refactor beyond these shipped paths.

**Expected result**

- semantic family is the first fully migrated phase-1 slice;
- the builder has proven it can materialize stable non-physical paths before
  moving to more ambiguous physical ones.

**Verification gate**

- targeted integration tests for semantic actions go `red -> green`;
- event/artifact assertions confirm `dispatchClass=semantic`;
- current smoke/helper scenarios remain green.

**Unblocks**

- step `5`

### 10.7. Step 5 — Migrate expected physical paths

**Package:** `C`

**Preconditions**

- step `4` green

**Depends on**

- steps `1-4`

**DDD**

- reuse the shared phase-1 domain seam

**TDD**

- yes
- justification: these are high-signal product paths with many fail-close
  branches and shared-resource side effects.

**Work**

- integrate `dispatchClass=expected_physical` for:
  - `click(elementIndex)`
  - `click(point)`
  - `press_key`
  - `drag`
  - coordinate `scroll(point)`;
- thread truthful shared-resource facts:
  - `physicalPointerUsed`
  - `physicalKeyboardUsed`
  - `systemCursorMoved`;
- connect low-level preflight truth into the public envelope:
  - `foregroundIntegrity`
  - `windowContinuity`
  - confirmation required/satisfied;
- keep click-with-semantic-target classified as physical dispatch, not as
  semantic execution.

**Constraints**

- no fake UIPI precision;
- no second-cursor narrative;
- no coordinator-specific one-off classification strings left behind if the
  shared seam can own them.

**Expected result**

- all non-fallback physical actions speak one consistent product truth model;
- confirmation semantics become cross-action rather than per-handler folklore.

**Verification gate**

- targeted integration tests for click/press/drag/coordinate-scroll go
  `red -> green`;
- stale/wrong-foreground/preflight failure tests still fail closed;
- no regression in `observeAfter` for actions that already support it.

**Unblocks**

- step `6`

### 10.8. Step 6 — Migrate fallback physical typing and failure boundaries

**Package:** `C`

**Preconditions**

- step `5` green

**Depends on**

- steps `1-5`

**DDD**

- reuse the shared phase-1 domain seam

**TDD**

- yes, mandatory
- justification: this is the riskiest current branch because it mixes
  screenshot proof, focus proof, fallback policy and `verify_needed` honesty.

**Work**

- keep normal `type_text` on the main focused-editable path as
  `expected_physical`;
- classify:
  - focused fallback as `fallback_physical`
  - coordinate-confirmed fallback as `fallback_physical`;
- ensure fallback paths still preserve:
  - `confirmationRequired=true`
  - honest `verify_needed`
  - no clipboard default
  - no hidden previous-click reuse;
- integrate fail-close facts for:
  - stale state
  - missing capture proof
  - wrong foreground
  - integrity block.

**Constraints**

- do not widen fallback vocabulary;
- do not reuse `allowFocusedFallback=true` as a generic precedent for other
  tools;
- do not collapse fallback vs expected physical into one class for convenience.

**Expected result**

- the triad `semantic / expected_physical / fallback_physical` becomes complete
  across the whole shipped public action surface;
- the most product-sensitive poor-UIA path is still honest and bounded.

**Verification gate**

- `type_text` focused and coordinate fallback tests go `red -> green`;
- finalization/event/artifact assertions confirm `fallback_physical`;
- no regression in public `verify_needed` semantics.

**Unblocks**

- step `7`

### 10.9. Step 7 — Add the companion minimal proof-smoke

**Package:** `D`

**Preconditions**

- steps `4-6` green

**Depends on**

- steps `0-6`

**DDD**

- no

**TDD**

- yes for scenario assertions;
- no for the purely mechanical script wiring.

**Work**

- add a narrow proof-smoke entrypoint dedicated to phase-1 semantics;
- implement the minimum scenario set from section `12`;
- wire it into the sequential repo loop:
  - after broad `scripts/smoke.ps1`
  - before `refresh-generated-docs.ps1`;
- keep it helper-backed and staged-bundle-based, not release-suite-sized.

**Constraints**

- do not fold this into install/release acceptance;
- do not let broad engine smoke become the only owner of phase-1 proof;
- do not require future slices to wait for a later benchmark mega-suite.

**Expected result**

- phase-1 semantics become measurable on a real `computer-use-win` loop;
- next worker can validate policy truth without running full release packaging.

**Verification gate**

- proof-smoke scenarios pass locally;
- `scripts/ci.ps1` and `scripts/codex/verify.ps1` run them in the intended
  order.

**Unblocks**

- steps `8-9`

### 10.10. Step 8 — Sync docs, generated surfaces and installed-copy proof

**Package:** `E`

**Preconditions**

- step `7` green

**Depends on**

- steps `2-7`

**DDD**

- no

**TDD**

- yes only for exporter/install-proof behavior that changes with the public
  result surface;
- no for pure markdown refresh.

**Work**

- update product/architecture wording to describe the new public truth model;
- refresh generated docs;
- extend installed/publication proof so it validates at least one fresh-thread
  action result carrying `executionFacts`;
- remove phase-0 duplicate mapping/plumbing that remains only as historical
  ballast after the shared owner is in place.

**Constraints**

- do not reopen packaging architecture;
- do not keep duplicate legacy classification branches just because they once
  powered partial observability;
- do not leave generated/docs/install drift for a “later cleanup”.

**Expected result**

- runtime, docs, generated interfaces and installed-copy proof all describe the
  same phase-1 truth surface;
- the branch does not carry redundant phase-0 mapping code.

**Verification gate**

- `scripts/refresh-generated-docs.ps1`
- targeted exporter/install proof tests
- `scripts/codex/prove-computer-use-win-cache-install.ps1` if schema/result
  surface changed

**Unblocks**

- step `9`

### 10.11. Step 9 — Final sequential closure and acceptance pass

**Package:** `E`

**Preconditions**

- steps `0-8` green

**Depends on**

- all previous steps

**DDD**

- no

**TDD**

- no new TDD here; this is the final evidence pass over already stabilized code

**Work**

- run the final linear contour in the canonical order;
- validate that no step reintroduced drift in public result, event/artifact,
  proof-smoke or installed-copy proof;
- only after this move the active record toward completed-state closure.

**Canonical final contour**

1. `scripts/build.ps1`
2. `scripts/test.ps1`
3. `scripts/smoke.ps1`
4. `scripts/computer-use-win-physical-policy-proof-smoke.ps1`
5. `scripts/refresh-generated-docs.ps1`
6. `scripts/codex/verify.ps1`
7. `scripts/codex/prove-computer-use-win-cache-install.ps1` when the public
   schema/result surface changed

**Constraints**

- no parallel verification jobs in the same worktree;
- no docs-only “green” closeout without runtime proof;
- no phase completion while duplicate legacy mapping branches remain live.

**Expected result**

- branch closes with one coherent proof story;
- active doc can move to completed only after the actual implementation evidence
  exists.

**Final stop condition**

- all acceptance criteria in section `15` are satisfied with evidence from the
  final contour above.

### 10.12. Execution log

Reserved per-step execution log for the implementer. Each step owns exactly one
slot below.

#### Step 0 — Freeze the baseline and failure surface
- status: done
- completed_at: 2026-05-14 13:26 UTC
- owner: Codex
- commit: `9097728`
- scope:
  - added characterization test for the current flat public action payload without `executionFacts` or `actionReceipt`
  - added characterization test for current partial top-level observability fields in the action artifact without a nested phase-1 envelope
- verification:
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ComputerUseWinFinalizationTests|FullyQualifiedName~ComputerUseWinActionAndProjectionTests|FullyQualifiedName~McpProtocolSmokeTests"`
  - passed: `181/181`
- decisions:
  - step `0` characterization stayed in `ComputerUseWinFinalizationTests` because that file already owns the current public result, action artifact and runtime-event baseline
  - baseline was frozen by asserting the absence of phase-1 fields rather than by broad file-wide snapshotting, to keep later red bars high-signal
- residual_risks:
  - current baseline for semantic vs physical behavior is still mostly pinned through existing integration coverage rather than many new characterization tests; this is acceptable because step `0` only needs the minimal guardrail before step `1`
- next_unblocked_step: `1`

#### Step 1 — Introduce the phase-1 domain seam
- status: done
- completed_at: 2026-05-14 13:29 UTC
- owner: Codex
- commit: `ccb8687`
- scope:
  - added the narrow internal execution-facts seam with stable value sets for `dispatchClass`, `targetProof`, `windowContinuity` and `foregroundIntegrity`
  - added shared `ComputerUseWinExecutionFactsBuilder` and `ComputerUseWinPhysicalExecutionPolicy`
  - added fail-fast characterization tests for semantic, expected physical, fallback physical and unknown executor mapping
- verification:
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ComputerUseWinExecutionFactsBuilderTests"`
  - passed: `4/4`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ComputerUseWinFinalizationTests|FullyQualifiedName~ComputerUseWinExecutionFactsBuilderTests"`
  - passed: `35/35`
- decisions:
  - step `1` stayed internal-only: the new seam lives under `WinBridge.Server/ComputerUse` and is not yet threaded into the public result contract
  - unknown executors fail closed in the shared policy so later action migration cannot silently widen semantics
- residual_risks:
  - executor taxonomy is intentionally still narrow and will need extension in later steps when the seam is wired into all shipped actions
- next_unblocked_step: `2`

#### Step 2 — Add the additive public `executionFacts` envelope
- status: done
- completed_at: 2026-05-14 13:32 UTC
- owner: Codex
- commit: `d5b0cd5`
- scope:
  - added public `ComputerUseWinExecutionFacts` contract to `WinBridge.Runtime.Contracts`
  - extended `ComputerUseWinActionResult` with additive nested `executionFacts`
  - wired success-path finalization so explicit action observability context now materializes the public envelope
- verification:
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ComputerUseWinFinalizationTests"`
  - passed: `31/31`
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ComputerUseWinFinalizationTests|FullyQualifiedName~ComputerUseWinExecutionFactsBuilderTests"`
  - passed: `35/35`
- decisions:
  - step `2` stayed intentionally narrow: public envelope is opened only on the result path, while event/artifact parity stays for step `3`
  - the new public `executor` currently reuses the current `dispatchPath` vocabulary; later steps can refine per-action values without reopening the envelope shape itself
- residual_risks:
  - failure and approval payloads still rely on the pre-phase-1 partial truth path and do not yet materialize the new public envelope consistently
- next_unblocked_step: `3`

#### Step 3 — Make lifecycle owners the single materialization path
- status: done
- completed_at: 2026-05-14 13:36 UTC
- owner: Codex
- commit: pending step-3 commit hash
- scope:
  - action finalization now feeds `payload.ExecutionFacts` into top-level completion audit
  - `computer_use_win.action.completed` event and action artifact now materialize phase-1 top-level facts from the same public envelope path
  - action artifact/runtime event tests were updated to pin the new lifecycle parity
- verification:
  - `dotnet test tests/WinBridge.Server.IntegrationTests/WinBridge.Server.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ComputerUseWinFinalizationTests"`
  - passed: `31/31`
  - `dotnet test WinBridge.sln --no-restore --filter "FullyQualifiedName~AuditLogTests|FullyQualifiedName~AuditPayloadRedactorTests|FullyQualifiedName~ComputerUseWinFinalizationTests|FullyQualifiedName~ComputerUseWinExecutionFactsBuilderTests"`
  - passed: `WinBridge.Server.IntegrationTests 35/35`, `WinBridge.Runtime.Tests 34/34`
- decisions:
  - step `3` keeps event/artifact phase-1 facts flat and top-level instead of introducing a nested artifact envelope, because MCP runtime events already use flat key/value data and the current action artifact schema is top-level safe-field oriented
  - the public action result is now the canonical source for phase-1 facts, and observability/audit consume it rather than rebuilding a separate truth model
- residual_risks:
  - per-action classification is still based on current partial executor vocabulary and will only become product-complete in steps `4-6`
- next_unblocked_step: `4`

#### Step 4 — Migrate semantic paths first
- status: pending
- commit: —
- verification: —
- mini-report: not started

#### Step 5 — Migrate expected physical paths
- status: pending
- commit: —
- verification: —
- mini-report: not started

#### Step 6 — Migrate fallback physical typing and failure boundaries
- status: pending
- commit: —
- verification: —
- mini-report: not started

#### Step 7 — Add the companion minimal proof-smoke
- status: pending
- commit: —
- verification: —
- mini-report: not started

#### Step 8 — Sync docs, generated surfaces and installed-copy proof
- status: pending
- commit: —
- verification: —
- mini-report: not started

#### Step 9 — Final sequential closure and acceptance pass
- status: pending
- commit: —
- verification: —
- mini-report: not started

## 11. Test ladder

### L1 — Unit / narrow contract

Must cover:

- public `executionFacts` serialization shape;
- no change to existing status literals;
- `dispatchClass` mapping helpers;
- `executor` normalization;
- `windowContinuity` / `foregroundIntegrity` mapping helpers;
- audit/event redaction around the new fields;
- generated contract/export drift.

Primary files:

- `ToolContractManifestTests.cs`
- `ToolContractExporterTests.cs`
- `AuditLogTests.cs`
- `AuditPayloadRedactorTests.cs`
- targeted new unit tests around the phase-1 builder/policy owner

### L2 — Server/integration

Must cover:

- per-action `dispatchClass` mapping;
- semantic vs expected physical vs fallback physical;
- confirmation-required vs confirmation-satisfied paths;
- stale-state fail-close;
- wrong-foreground fail-close;
- successor-state success/failure interaction with the new envelope;
- installed public schema/result consistency where applicable.

Primary files:

- `ComputerUseWinFinalizationTests.cs`
- `ComputerUseWinActionAndProjectionTests.cs`
- `McpProtocolSmokeTests.cs`

### L3 — Live proof-smoke

Must cover helper-backed real MCP session with staged bundle and real
`computer-use-win` profile for the minimal scenarios from step `7` / Package `D`.

### L4 — Generated/docs sync

Run:

- `scripts/refresh-generated-docs.ps1`

and assert generated docs describe the phase-1 contract honestly.

### L5 — Installed/publication proof

If public schema/result surface changed:

- run `scripts/codex/prove-computer-use-win-cache-install.ps1`;
- extend proof to include at least one helper-backed public action result from a
  cache-installed copy and assert the new `executionFacts` materialize there.

## 12. Minimal proof-smoke strategy

### 12.1. Where it lives

Recommended phase-1 control-plane shape:

- new dedicated script:
  `scripts/computer-use-win-physical-policy-proof-smoke.ps1`
- script shells into a narrow `dotnet test` filter or a dedicated small proof
  harness under `WinBridge.Server.IntegrationTests`
- `scripts/ci.ps1` runs it **after** broad `scripts/smoke.ps1` and **before**
  `refresh-generated-docs.ps1`
- `scripts/codex/verify.ps1` inherits it through `ci.ps1`
- `scripts/release-verify.ps1` inherits it indirectly through fast CI, but this
  plan does not otherwise touch release/install scope

### 12.2. What artifacts it must assert

For each proof-smoke scenario assert:

- public `structuredContent.executionFacts`
- matching `computer_use_win.action.completed` event
- matching `artifacts/diagnostics/<run_id>/computer-use-win/action-*.json`
- if applicable:
  - `successorState`
  - `successorStateFailure`
  - child runtime artifact path

### 12.3. What it must not become

This proof-smoke is **not**:

- the full benchmark/proof suite;
- the release/install acceptance suite;
- a broad new smoke umbrella for all future slices.

It is a narrow guardrail for the first physical-policy hardening phase.

## 13. Docs/generated/install-surface sync

### 13.1. Docs that phase-1 implementation must update

Product/architecture docs:

- `docs/architecture/computer-use-win-surface.md`
- `docs/architecture/observability.md`
- `docs/product/okno-roadmap.md` only if slice `13` wording/status needs a
  factual refinement after shipment

Generated docs:

- `docs/generated/computer-use-win-interfaces.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/test-matrix.md`
- `docs/generated/commands.md` if a new proof-smoke script is added

Execution records:

- this active phase-1 plan moves to completed only after implementation closes;
- umbrella plan remains the top-level frame and should only receive a concise
  cross-link if needed, not be rewritten into a monolith.

Operational docs:

- `docs/CHANGELOG.md`
- plugin docs only if public wording materially changes

### 13.2. Install/publication proof rule

Because phase-1 changes **public result surface**, installed plugin proof must
go beyond `tools/list` shape only.

Minimum requirement:

- cache-installed `computer-use-win` copy must prove at least one fresh-thread
  public action result containing the new `executionFacts` envelope.

What is not required:

- no packaging architecture changes;
- no installer-wave redesign;
- no new runtime distribution assets.

## 14. Risks, rollback, out-of-scope

### Main risks

1. **Taxonomy drift risk**  
   Too many action-specific strings can recreate the same fragmentation inside
   the new envelope.

2. **Scope creep risk**  
   `executionFacts` can accidentally drag in `actionReceipt`, playbooks,
   `region_capture`, lease substrate and browser/terminal lanes.

3. **False precision risk**  
   Publishing exact `UIPI` or exact `foregroundChanged` claims where Windows
   cannot honestly prove them.

4. **Control-plane bloat risk**  
   Turning minimal proof-smoke into a second full acceptance suite.

5. **Installed-copy drift risk**  
   Repo-local phase-1 result shape lands, but cache-installed plugin proof still
   only validates schema/tool count.

### Mitigations

- keep `dispatchClass` coarse and product-facing;
- centralize executor mapping in one owner;
- explicitly defer `actionReceipt` and lease work;
- keep `foregroundIntegrity` and `windowContinuity` conservative;
- keep proof-smoke narrow and helper-backed;
- extend installed-copy proof only as much as needed for phase-1.

### Rollback strategy

Phase-1 should remain easy to back out:

- additive public field only;
- no new tool names;
- no new install architecture;
- no new long-lived storage substrate.

If rollback is needed, the branch should be able to revert:

- `executionFacts` addition,
- companion proof-smoke,
- docs sync

without reopening the shipped action wave itself.

### Explicitly out of scope after this plan

- `physical-policy-phase-2`
- playbooks/hints expansion
- `windows.region_capture`
- `windows.uia_action`
- stable target identity / lease substrate
- proof envelope / `actionReceipt`
- browser/Electron lane
- terminal lane
- OCR / second cursor / driver path

## 15. Explicit acceptance criteria

Phase-1 is complete only if **all** conditions below are true:

1. Public `computer-use-win` tool count remains unchanged at nine tools.
2. `ComputerUseWinActionResult` gains a new nested `executionFacts` field and
   does not break existing top-level result fields.
3. Existing public status literals remain unchanged.
4. Every current shipped action materializes one of:
   - `semantic`
   - `expected_physical`
   - `fallback_physical`
5. `set_value` and `perform_secondary_action` materialize as `semantic`.
6. `press_key`, `click`, `drag`, and point-scroll paths materialize as
   `expected_physical`.
7. Focused and coordinate-confirmed `type_text` fallback materialize as
   `fallback_physical`.
8. `executionFacts` publicly expose:
   - `dispatchClass`
   - `executor`
   - `confirmationRequired`
   - `confirmationSatisfied`
   - `fallbackUsed`
   - `targetProof`
   - `stateTokenPresent`
   - `captureReferencePresent`
   - `windowContinuity`
   - `foregroundIntegrity`
   - `physicalPointerUsed`
   - `physicalKeyboardUsed`
   - `systemCursorMoved`
   - `observeAfterRequested`
   - `successorStateAvailable`
9. Public envelope does not claim exact `UIPI` diagnosis or exact
   `foregroundChanged` when runtime cannot prove it honestly.
10. `computer_use_win.action.completed` and action JSON artifacts are extended
    to the same phase-1 truth model without leaking raw sensitive payloads.
11. Malformed requests still fail as `invalid_request` and do not fabricate
    dispatch facts.
12. Stale-state and wrong-foreground paths fail closed and are covered by tests.
13. Companion minimal proof-smoke ships in the same branch and passes.
14. Generated docs and architecture docs are refreshed and consistent with the
    new result surface.
15. Cache-installed/publication proof is updated if public schema/result surface
    changed, and proves that an installed plugin materializes the new
    `executionFacts` shape in a real fresh-thread action path.

## Recommended execution order

1. Step `0` — freeze the baseline and failure surface
2. Step `1` — introduce the phase-1 domain seam
3. Step `2` — add the additive public `executionFacts` envelope
4. Step `3` — make lifecycle owners the single materialization path
5. Step `4` — migrate semantic paths first
6. Step `5` — migrate expected physical paths
7. Step `6` — migrate fallback physical typing and failure boundaries
8. Step `7` — add the companion minimal proof-smoke
9. Step `8` — sync docs, generated surfaces and installed-copy proof
10. Step `9` — final sequential closure and acceptance pass
