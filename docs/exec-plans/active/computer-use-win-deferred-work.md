# ExecPlan: Computer Use for Windows deferred product work

## Контекст

Ветка `codex/computer-use-win` закрыла hardening public `computer-use-win` surface: request boundary, action lifecycle, failure taxonomy, activation cause, install/publish recovery и runtime bundle integrity. В ходе review loop часть замечаний была подтверждена как bugfix текущей ветки и уже закрыта, но несколько тем были сознательно вынесены за пределы ветки.

Этот документ фиксирует deferred work, чтобы следующие волны не возвращались к тем же finding-ам как к локальным bugfix-ам. Здесь описаны темы, которые требуют отдельного product/API design или новой execution model, а не точечного hardening текущего shipped contract.

## Цель

Подготовить следующий пакет работ после текущей ветки:

1. сделать public discovery согласованным с window-level execution;
2. при необходимости разделить pure observation и action-session preparation для `get_app_state`;
3. отдельно пересмотреть policy для advisory/provider failures, если команда решит изменить уже принятый truthful failure invariant.

## Границы

- Входит: описание найденных deferred классов, rationale, desired target model, task decomposition, acceptance criteria и verification contour.
- Не входит: изменение runtime кода в текущей ветке.
- Не входит: срочный bugfix уже подтверждённых contract defects; они закрыты отдельными commits в текущей ветке.
- Не входит: автоматическая миграция клиентов на новый discovery surface.

## Почему это вынесено из текущей ветки

Текущая ветка была contract-hardening веткой. Она должна была сделать уже выбранный public `computer-use-win` surface честным и безопасным: `list_apps -> get_app_state -> click`, lifecycle hints, typed failures, install artifact. Оставшиеся темы меняют не только implementation, но и product model:

- discovery DTO и client workflow;
- адресацию target-а на уровне app vs window instance;
- возможное разделение одного public tool-а на два разных semantic tools;
- policy для того, какие advisory failures считаются truthful product failure, а какие soft-fail evidence.

Такие изменения должны идти отдельной веткой с собственным design review и acceptance matrix.

---

## Deferred class 1: Instance-addressable discovery

### Как нашли

Review findings: `79`, `81`, `92`, `98`.

Суть повторялась в разных формулировках: `list_apps` сейчас группирует видимые окна по process-derived `appId` и публикует один public entry. При multi-window приложениях часть окон становится невидимой для клиента: discovery показывает app-level abstraction, а execution фактически работает с конкретным `HWND` / window identity.

В текущей ветке это не исправлялось как bugfix, потому что existing public loop уже был зафиксирован вокруг `appId`, approval и `stateToken`. Исправление требует изменить public discovery contract.

### Что это такое

Это drift между двумя уровнями identity:

- `appId` как app-level approval / policy key;
- `hwnd` и stable window identity как window-level execution target.

Если discovery прячет window instances за одним `appId`, клиент не может выбрать конкретное background окно без внешнего знания `HWND`. При этом `get_app_state` и `click` уже должны доказывать target на уровне конкретного окна.

### Почему это нужно

Без instance-addressable discovery остаются product gaps:

- несколько окон `notepad`, `explorer` или browser windows не все достижимы через public discovery;
- `ambiguous_target` может быть честным failure, но клиент не получает enough public data, чтобы устранить ambiguity;
- approval model и execution model продолжают жить на разных уровнях abstraction.

### Target design

Нужен public discovery surface, который показывает selectable window instances и при этом сохраняет app-level policy key.

Допустимые варианты:

1. `list_apps` возвращает один entry на window instance.
2. `list_apps` сохраняет app grouping, но внутри каждого app entry публикует `windows[]`.
3. Добавляется новый `list_windows`-style public discovery tool для `computer-use-win`, а `list_apps` остаётся compatibility/discovery summary.

Предпочтительный вариант для отдельной ветки: начать с design spike между вариантами 1 и 2. Не добавлять новый tool, пока не доказано, что existing `list_apps` нельзя расширить без breaking confusion.

### Затрагиваемые файлы

- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `src/WinBridge.Runtime.Contracts/ComputerUseWinContracts.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `docs/architecture/computer-use-win-surface.md`
- `docs/generated/computer-use-win-interfaces.md`
- `docs/generated/computer-use-win-interfaces.json`
- `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`

### Task plan

- [ ] **Task 1: Capture current discovery contract**
  - Read current `list_apps` DTO and generated docs.
  - Document whether `appId`, `hwnd`, title, process identity and approval fields are public.
  - Add a small design note to this plan or a follow-up design doc before implementation.

- [ ] **Task 2: Choose target DTO shape**
  - Decide between per-window entries and grouped `windows[]`.
  - Preserve `appId` as approval key.
  - Introduce a stable public window selector field if needed, but do not expose raw `HWND` as the only semantic selector unless explicitly accepted.

- [ ] **Task 3: Write failing tests for multi-window discovery**
  - Scenario: two visible windows from same process/app identity.
  - Expected: both windows are selectable through public discovery.
  - Expected: `get_app_state` can target each discovered instance without ambiguous fallback.

- [ ] **Task 4: Implement discovery owner-layer**
  - Move grouping/flattening decision into one `BuildAppDescriptors` / discovery materializer.
  - Keep approval semantics app-level.
  - Keep execution resolution window-level.

- [ ] **Task 5: Update schema/docs/generated interfaces**
  - Regenerate public interface docs.
  - Update architecture docs with app-level policy vs window-level execution model.

- [ ] **Task 6: Add acceptance scenarios**
  - Multi-window `notepad`.
  - Multi-window `explorer`.
  - Browser with multiple top-level windows if stable enough for local smoke.

### Acceptance criteria

- Public discovery exposes every visible selectable window instance.
- Client can select a background window using only public discovery data.
- `appId` remains approval/policy key and does not become a fragile window identity surrogate.
- `get_app_state` no longer has to guess foreground instance when discovery already selected a concrete window.
- Docs describe the distinction between app group, approval key and execution target.

### Verification contour

- Targeted integration tests for multi-window app identity.
- Generated docs refresh.
- `scripts/codex/verify.ps1`.
- Optional manual smoke with two real windows if test harness cannot reliably model Explorer/browser.

---

## Deferred class 2: Optional pure observe split for `get_app_state`

### Как нашли

Related finding: `103`.

The confirmed bug in this area was fixed in current branch by making metadata honest: current `get_app_state` is not read-only because it may approve, activate/focus, commit state token and attach session. The deferred work is different: decide whether product needs a separate pure observation tool.

### Что это такое

Current `get_app_state` is an action-session preparation tool. It observes state, but also prepares the state needed for future actions. That is a valid product model, but it means clients must not treat it as safe read-only.

A future product design could split this into:

- pure observe tool: no approval mutation, no activation, no session attach, no durable state token commit;
- prepare tool: explicit approval/activation/session preparation for action-ready state.

### Почему это нужно

This is only needed if we want clients/models to safely auto-run observation without foreground/session side effects. If current product loop intentionally requires action-ready preparation, the split is not necessary.

### Target design

Do not weaken current `get_app_state` metadata. If split is chosen, add a new explicit surface rather than lying through annotations.

Possible model:

- `observe_app_state` or `preview_app_state`: read-only, no activation, no approval mutation, no action token.
- `get_app_state`: remains action-ready and side-effecting.
- Or rename future action-ready path to `prepare_app_state` while keeping compatibility only if product accepts it.

### Затрагиваемые файлы

- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`
- `src/WinBridge.Runtime.Tooling/ToolNames.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `docs/architecture/computer-use-win-surface.md`
- `docs/generated/computer-use-win-interfaces.md`
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`

### Task plan

- [ ] **Task 1: Decide product need**
  - Confirm whether model/client workflows need safe automatic observation.
  - If not needed, keep current metadata-only fix and close this deferred item as rejected.

- [ ] **Task 2: Define pure observe invariants**
  - No approval store writes.
  - No foreground activation.
  - No attached session mutation.
  - No action-ready `stateToken` unless token is explicitly marked non-actionable.

- [ ] **Task 3: Write failing tests**
  - Pure observe call does not call `ActivateAsync`.
  - Pure observe call does not update approval/session stores.
  - Pure observe payload cannot be used for `click` unless promoted through prepare path.

- [ ] **Task 4: Implement separate owner-layer**
  - Share capture/UIA observation where safe.
  - Keep action lifecycle/token semantics only in prepare/action-ready path.

- [ ] **Task 5: Sync metadata and docs**
  - Pure observe tool: `readOnlyHint=true`.
  - Prepare/action-ready tool: `readOnlyHint=false`, `OsSideEffect`.
  - Generated docs and smoke assertions must reflect both.

### Acceptance criteria

- No tool with `readOnlyHint=true` mutates foreground, approval or session state.
- Action-ready state remains explicit and machine-readable.
- Existing `click` cannot consume a pure observe token by accident.
- Docs explain the difference between observing and preparing.

### Verification contour

- Unit/integration tests around approval/session mutation.
- `tools/list` smoke assertions for annotations.
- Generated interface docs refresh.
- Full `scripts/codex/verify.ps1`.

---

## Deferred class 3: Advisory provider failure policy redesign

### Как нашли

Review findings: `56`, `63`.

These findings argued that advisory/provider bugs should always soft-fail. During current branch triage, this was rejected for the current contract because it conflicted with the accepted invariant: unexpected observation/provider failures must materialize truthfully as `observation_failed` instead of being hidden as success.

### Что это такое

There are two different categories:

- expected advisory unavailable: optional instruction/advisory asset missing or intentionally unavailable;
- unexpected provider/runtime bug: capture/UIA/advisory provider throws or returns inconsistent state.

Current branch preserves the distinction:

- expected advisory unavailable can be non-fatal;
- unexpected provider/runtime bug is truthful failure.

### Почему это может понадобиться

If product direction changes toward “best-effort observation must always return something”, then provider failure policy must be redesigned explicitly. That would trade contract truthfulness for higher availability.

This is not a local bugfix. It changes how much uncertainty public payloads may hide.

### Target design

If reopened, define a stage-aware policy table:

| Stage | Expected unavailable | Unexpected provider bug | Public outcome |
|---|---|---|---|
| instruction/advisory optional asset | soft-fail | maybe `observation_failed` | depends on product choice |
| capture proof | fail | fail | `observation_failed` |
| UIA proof for action-ready state | fail unless explicitly optional | fail | `observation_failed` |
| decorative metadata | soft-fail | soft-fail with diagnostics | success + warning |

No implementation should use broad catch-all soft-fail without stage classification.

### Затрагиваемые файлы

- `src/WinBridge.Server/ComputerUse/ComputerUseWinAppStateObserver.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinGetAppStateFinalizer.cs`
- `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`
- `docs/architecture/computer-use-win-surface.md`
- `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*`

### Task plan

- [ ] **Task 1: Decide policy direction**
  - Keep truthful failure semantics, or explicitly choose availability-first soft-fail for some stages.
  - Record decision in architecture docs before code.

- [ ] **Task 2: Build provider stage matrix**
  - Enumerate capture, UIA, advisory, instruction and decorative metadata stages.
  - Mark each stage as required proof or optional enrichment.

- [ ] **Task 3: Write failing tests from matrix**
  - Required proof failure returns `observation_failed`.
  - Optional enrichment failure returns success with explicit warning/evidence, if that policy is chosen.

- [ ] **Task 4: Implement stage-aware materializer**
  - No broad catch-all soft-fail.
  - Failure/warning must carry stage and diagnostic evidence.

- [ ] **Task 5: Sync docs**
  - Public docs must state which data is required proof and which data is optional enrichment.

### Acceptance criteria

- Required proof failures are not hidden as successful action-ready state.
- Optional advisory failures do not prevent successful state only when policy explicitly marks them optional.
- Public payload includes enough machine-readable information for client retry/refresh decisions.

### Verification contour

- Targeted observation failure tests.
- Audit/evidence tests for warning vs failure.
- Full `scripts/codex/verify.ps1`.

---

## Cross-cutting acceptance

- Every future implementation task must start with a fresh source-of-truth check: current code, tests, generated docs and product docs.
- Redesign-grade changes must not be smuggled into bugfix branches.
- New public DTO fields must be reflected in schema, generated docs, smoke assertions and changelog in the same PR.
- If a future review finding belongs to one of these deferred classes, first update this plan or fork it into a dedicated active ExecPlan before coding.

## Suggested branch split

1. `codex/computer-use-win-instance-discovery`
   - Owns deferred class 1.
   - Highest product value and highest API impact.

2. `codex/computer-use-win-observe-prepare-split`
   - Owns deferred class 2 only if product chooses pure read-only observation.

3. `codex/computer-use-win-advisory-policy`
   - Owns deferred class 3 only if current truthful observation failure semantics are intentionally reopened.

## Current recommendation

Do next only the instance-addressable discovery branch. Keep `get_app_state` split and advisory policy redesign parked until there is a concrete client/product need. This keeps the next step focused on the only deferred item that currently blocks real operator reachability in multi-window scenarios.
