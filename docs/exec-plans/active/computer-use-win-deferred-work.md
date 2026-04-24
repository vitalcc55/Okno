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
- MCP spec citations in docs should point to the latest official revision unless a file is explicitly documenting the currently negotiated runtime baseline.
- If a future review finding belongs to one of these deferred classes, first update this plan or fork it into a dedicated active ExecPlan before coding.

## Дополнительный архитектурный контекст

Более широкий статический разбор той же поверхности добавляет один важный сквозной вывод: текущие deferred items для `computer-use-win` не живут изолированно от host-архитектуры. Основное давление на drift возникает из-за того, что server tool classes всё ещё совмещают несколько ролей:

- binding транспорта MCP;
- validation запроса и selector admission;
- продуктовую policy/orchestration;
- materialization результата и failure;
- publication/profile semantics.

Практически это проявляется в двух местах:

1. `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs` уже одновременно является и публичным adapter-слоем продукта, и фактическим application host для `list_apps`, `get_app_state` и `click`, при этом продолжает держать latent next-wave action methods.
2. `src/WinBridge.Server/Program.cs` всё ещё использует late-bound `hostServices` closures для создания MCP tools. Для текущего размера это допустимо, но после декомпозиции tool owners и появления более мелких handler/application units эта форма станет заметно хрупче.

Это **не** означает, что текущий shipped contract неверен. Это означает, что следующие product branches нужно запускать вместе с небольшим объёмом архитектурного hardening, чтобы product redesign не закреплял ещё сильнее текущую transport-hosted orchestration shape.

## Companion hardening tracks

Эти треки — сопутствующая работа для product redesign classes выше. Они не все обязательны перед каждой будущей веткой, но именно они задают самый безопасный порядок следующих волн `computer-use-win`.

| Track | Почему это важно здесь | Основной scope | Критерий завершения | Priority |
| --- | --- | --- | --- | --- |
| `C0. Deferred action surface freeze` | `ComputerUseWinTools` уже содержит callable methods для `type_text`, `press_key`, `scroll` и `drag`, а `ComputerUseWinToolRegistration` уже содержит create-methods для их MCP publication. Хотя сейчас экспортируются только `list_apps/get_app_state/click`, текущая форма кода держит риск случайной публикации выше, чем нужно. | `src/WinBridge.Server/ComputerUse/ComputerUseWinTools.cs`, `src/WinBridge.Server/ComputerUse/ComputerUseWinToolRegistration.cs`, `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`, `tests/WinBridge.Server.IntegrationTests/*ComputerUseWin*` | Непубликуемые action paths либо вынесены за явную internal draft boundary, либо их случайная публикация технически невозможна; tests фиксируют ровно три public implemented tools. | `P1` |
| `A1. Tool layer decompression` | Deferred class 1 почти наверняка затронет discovery DTOs, target resolution и public payload shaping. Делать это внутри текущего монолитного tool host значит усилить drift pressure. | Начать с `ComputerUseWinTools`; трогать engine/shared layers только там, где discovery или state prep требуют общих abstractions. | `ComputerUseWinTools` становится thin transport adapter; app-state observation, click orchestration и result presenters/finalizers живут в отдельных owner-layers. | `P1` |
| `A3. Dependency guard rails` | Текущие границы сильны по соглашениям и tests, но ещё не закреплены явными architectural rules. Deferred redesign branches увеличат число moving parts. | Project-reference rules, forbidden namespace/type usage, publication/profile invariants. | CI/test suite падает, если policy-bearing tools обходят gate, если появляется publication/profile drift или если forbidden refs пересекают согласованный layer matrix. | `P1` |
| `CR1. Composition root stabilization` | Когда handlers и presenters начнут дробиться, текущий `hostServices` closure pattern в `Program.cs` станет более явной хрупкостью composition root. | `src/WinBridge.Server/Program.cs`, MCP tool registration helpers. | Tool registration больше не зависит от late-bound `hostServices` closures для core `computer-use-win` surface. | `P1` |
| `A2. First-class application boundary` | Deferred class 1 — первая redesign branch, где `computer-use-win` уже выгодно получить явные application-layer units вместо дальнейшего роста server tool host. | Scenario/use-case handlers, orchestration coordinators, policy evaluators, result presenters/materializers. | Product orchestration становится first-class boundary, а не скрытой ролью MCP tool classes. | `P1` |
| `S1. Unified result semantics` | Discovery redesign, observe/prepare split и advisory policy все зависят от canonical owner для статусов `ok`, `failed`, `blocked`, `approval_required`, `verify_needed` и будущих observation-only states. | Public result/failure lifecycle owners для `computer-use-win`. | Один canonical source of truth для result semantics управляет runtime payloads, docs/export wording и retry/refresh guidance. | `P1` |
| `C1. Publication + install contract matrix` | Public contract для `computer-use-win` — это не только `tools/list`; сюда же входят launcher args, runtime bundle shape и install/runtime materialization path. | `ToolContractManifest`, tool registration, generated interfaces, launcher docs, plugin install/runtime bundle acceptance. | `manifest == registration == profile == launcher/install surface` закреплён tests и docs. | `P2` |
| `S2. Explicit runtime state model` | Deferred class 2 и class 3 заметно упрощаются, если approval/session/state-token transitions описаны явно, а не выводятся косвенно из текущих flows. | `ComputerUseWinStateStore`, session interaction, approval flow, token semantics. | Allowed states и forbidden transitions записаны и покрыты tests. | `P2` |
| `O1. Safe audit builders` | Advisory policy redesign и будущий observe-only split не должны опираться на вручную собранный audit/result wording внутри больших handlers. | Shared audit/event metadata builders и result-to-audit mapping. | Sensitive payload/event metadata собираются в одном safe owner-layer вместо per-handler maps. | `P2` |
| `I1. Targeted isolation expansion` | Имеет смысл только если later redesign покажет ещё одну host-risky capability boundary, похожую на UIA worker isolation. | Capability-specific, только evidence-driven. | Isolation расширяется только для подтверждённого risky slice, а не как topology-first cleanup. | `P3` |

## MCP 2025-11-25 migration track

Документационные ссылки проекта нужно уже сейчас держать на latest MCP revision `2025-11-25`, но это **не означает**, что runtime уже мигрирован. На текущий момент фактический negotiated/exported baseline по коду и tests всё ещё `2025-06-18`, что видно как минимум в:

- `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`;
- `scripts/smoke.ps1`;
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`;
- `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`;
- generated interface exports.

Primary official sources for this migration block:

- MCP changelog `2025-11-25`;
- MCP tools spec `2025-11-25`;
- MCP lifecycle spec `2025-11-25`;
- MCP transports spec `2025-11-25`.

По официальному changelog и latest spec для `Okno`/`computer-use-win` наиболее релевантны такие дельты:

- `stdio` теперь явно допускает любой logging в `stderr`, а клиент не должен считать `stderr` error-only каналом;
- `Implementation` в `initialize` получил optional `description`, `icons`, `websiteUrl`;
- в `tools/list` появились optional `icons` и `execution.taskSupport`;
- для tool names появилась явная guidance по длине и допустимым символам;
- `inputSchema` и `outputSchema` по умолчанию считаются JSON Schema `2020-12`, если `$schema` не указан;
- input validation errors должны materialize-иться как Tool Execution Errors, а не как protocol errors;
- request payload schemas отделены от RPC method definitions как самостоятельные parameter schemas;
- HTTP/auth/task changes не являются immediate scope для текущего local `STDIO` runtime, но их нужно явно пометить как out-of-scope, а не просто игнорировать.

| Track | Почему это нужно до/вместе с миграцией | Основной scope | Критерий завершения | Priority |
| --- | --- | --- | --- | --- |
| `M0. Spec delta inventory freeze` | Нельзя поднимать `protocolVersion` механически, пока не зафиксировано, какие дельты latest spec реально относятся к текущему `STDIO`-only продукту, а какие сознательно откладываются. | Official MCP delta inventory, repo-local source-of-truth mapping, in-scope vs out-of-scope decisions. | Есть принятый список migration deltas с явным разделением `must implement now` / `explicitly defer`. | `P1` |
| `M1. Negotiated protocol baseline upgrade` | Сейчас smoke/tests/exporter/generated docs жёстко несут `2025-06-18`. Пока это не выровнено, docs и runtime будут расходиться. | `src/WinBridge.Runtime.Tooling/ToolContractExporter.cs`, `scripts/smoke.ps1`, `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`, `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`, generated interface exports. | Все protocolVersion literals/exported baselines синхронизированы на `2025-11-25`, а generated docs не расходятся с live handshake expectations. | `P1` |
| `M2. Initialize metadata and capability audit` | Latest lifecycle расширяет `Implementation` и capability negotiation; это нужно осознанно принять или осознанно не публиковать. | `initialize` request/response expectations, `serverInfo`, docs, smoke assertions, capability negotiation around `tasks`. | Решено и зафиксировано, что сервер публикует или сознательно не публикует из новых optional fields/capabilities; tests отражают это решение. | `P1` |
| `M3. Tool metadata and schema audit` | Latest tools spec добавляет `icons`, `execution.taskSupport`, tool naming guidance и JSON Schema `2020-12` defaults. Это касается contract export, public descriptors и manual schemas. | `ToolContractManifest`, MCP registration, exporter output, tool naming inventory, manual `inputSchema` / `outputSchema` owners. | Public tool metadata и schemas либо поддерживают новые optional fields корректно, либо явно фиксируют отсутствие; имена tools и schema assumptions проверены против latest guidance. | `P1` |
| `M4. Validation and error semantics alignment` | В latest spec input validation errors должны идти как tool execution errors. Для `Okno` это особенно важно из-за честной safety/contract semantics. | Request binders, validators, tool handlers/finalizers, smoke/integration tests across public and deferred surfaces. | Malformed tool arguments materialize-ятся как canonical tool-level failures там, где latest spec требует input-validation semantics; unknown tool и protocol-shape failures остаются protocol-owned. | `P1` |
| `M5. Parameter-schema and generated-contract sync` | После schema/runtime changes нельзя оставить старые generated exports или implicit SDK assumptions. | Exporters, generated docs/json, contract docs, install/publication acceptance, manual schema tests. | `manifest/export/generated docs/smoke/install surface` согласованы с latest protocol baseline и не полагаются на legacy parameter-shape assumptions. | `P2` |

## Sequenced roadmap

Самый безопасный порядок — развести **product-facing redesign** и минимальный **architecture hardening**, который не даёт этим redesign branches закрепить текущий drift.

| Iteration | Цель | Основные работы | Критерий завершения |
| --- | --- | --- | --- |
| `Iteration 1: Surface freeze and handler decompression` | Снизить риск случайной публикации и перестать растить `computer-use-win` внутри одного монолитного tool host. | `C0`, initial `A1` на `ComputerUseWinTools`, параллельный `A3`, затем `CR1`. | Public surface по-прежнему ровно `list_apps/get_app_state/click`; для первых extracted flows уже есть thin transport adapters; architectural rules начинают фиксировать publication и gate usage; `Program.cs` больше не держит core `computer-use-win` registration через текущий late-bound pattern. |
| `Iteration 1.5: MCP 2025-11-25 protocol baseline` | Поднять protocol/docs baseline до latest revision до того, как discovery/public result redesign расширит surface area ещё сильнее. | `M0`, `M1`, `M2`, `M3`, `M4`, затем `M5`. | Runtime/export/tests/generated docs согласованы на `2025-11-25`; latest-relevant deltas либо реализованы, либо явно задокументированы как out-of-scope для текущего `STDIO` продукта. |
| `Iteration 2: Instance discovery redesign on explicit application boundary` | Закрыть deferred class 1 поверх более ясной orchestration shape и уже обновлённого MCP baseline. | Deferred class 1 + `A2` + основной `S1` + `C1`. | Public discovery показывает selectable window instances без скрытого foreground guessing; product orchestration живёт в explicit application units; docs, generated exports, launcher/install surface и publication tests согласованы. |
| `Iteration 3: Optional observation/state-policy redesign` | Открывать optional redesign items только при подтверждённой product need. | Deferred class 2 и/или deferred class 3, плюс `S2`, `O1`, optional `I1`. | Observe-only split или advisory soft-fail policy приняты как product decision, а не как побочный refactor; state transitions и audit semantics описаны явно и проверяются tests. |

## Dependency order

| Track / branch | Зависит от | Когда запускать | Когда считать завершённым |
| --- | --- | --- | --- |
| `C0. Deferred action surface freeze` | nothing | первым | Unpublished action wave больше не может быть экспортирован случайно. |
| Initial `A1` | желательно после старта `C0` | сразу после `C0` | `ComputerUseWinTools` перестаёт быть owner сразу для transport + orchestration + presenter concerns. |
| `A3. Dependency guard rails` | agreed boundary names | параллельно с ранним `A1` | Tests фиксируют allowed direction matrix, gate usage и publication invariants. |
| `CR1. Composition root stabilization` | первые extracted owner layers из `A1` | после первого meaningful extraction seam | Core `computer-use-win` tool registration больше не зависит от `hostServices` closure indirection. |
| `M0. Spec delta inventory freeze` | nothing | можно начать сразу; завершать до code-level migration | Есть accepted inventory relevant MCP deltas для текущего `STDIO` продукта. |
| `M1` + `M2` + `M3` + `M4` + `M5` | `M0`, желательно `C0`; `M2/M3` выигрывают от `A3`, а `M1`/`M5` выигрывают от `CR1` | после Iteration 1 | Protocol baseline, initialize/tool metadata, validation semantics и generated exports согласованы на latest spec. |
| Deferred class 1 + `A2` + main `S1` | ранний `A1`, желательно `CR1`, предпочтительно завершённый `M1-M5` | после Iteration 1.5 | Discovery redesign приземляется на explicit application-layer owners и canonical result semantics уже поверх обновлённого MCP baseline. |
| `C1. Publication + install contract matrix` | DTO/publication shape из deferred class 1 и ownership model из `A2` | ближе к концу Iteration 2 | Все public contract surfaces согласованы: profile, docs, generated interfaces, launcher/install path. |
| Deferred class 2 | class 1 не обязателен, но `S1` крайне желателен | только при явной product need | Observe-only path имеет отдельные invariants и не может быть ошибочно прочитан как action-ready preparation. |
| Deferred class 3 | `S1` крайне желателен, `O1` полезен | только после явного product decision переоткрыть truthful failure invariant | Stage-aware policy для required proof vs optional enrichment описана явно и машинно читаема. |
| `S2`, `O1`, optional `I1` | лучше после `S1`; `I1` только по evidence | Iteration 3 | State transitions, audit semantics и любая новая isolation boundary появляются осознанно и подкреплены tests. |

## Recommended queue inside iterations

### Iteration 1

1. `C0` first.
   - Сначала заморозить deferred action wave, а уже потом делать более широкий refactor.
   - Считать “callable but not published” smell формы, а не harmless dead code.
2. Запустить `A1` внутри `ComputerUseWinTools`.
   - Первыми extraction targets должны быть:
     - discovery materialization,
     - app-state observation orchestration,
     - click orchestration,
     - result/failure finalization.
   - **Не** начинать с полного repo-wide split `WindowTools`.
3. Запустить `A3` параллельно, когда первые owner-layer names уже стабилизированы.
4. Применить `CR1` после первого реального extraction seam.
   - Цель — не перенести текущую `Program.cs` composition shape в новый graph обработчиков.

### Iteration 2

1. Запустить отдельную MCP migration branch для `M0-M5`.
   - Не смешивать её с instance discovery redesign.
   - Внутри migration branch сначала выровнять `protocolVersion`, потом `initialize`/capabilities, затем tool metadata/schema/error semantics.
2. После этого запускать deferred class 1 как первую product-facing redesign branch.
3. Использовать эту branch, чтобы сделать `A2` реальным для discovery/app-state path.
4. Довести `S1`, пока branch ещё затрагивает все релевантные public statuses и retry semantics.
5. Закрывать `C1` только после того, как DTO shape, publication semantics и launcher/install model уже согласованы.

### Iteration 3

1. Возвращаться к deferred class 2 только если есть конкретная client/model need для safe automatic observation.
2. Возвращаться к deferred class 3 только если продукт явно выбирает availability-first soft-fail для части stages.
3. Использовать `S2`, чтобы определить allowed states и forbidden transitions, например:
   - no action from stale state;
   - approval не заменяет fresh observation cycle;
   - blocked/stale paths не могут быть эскалированы в successful action-ready state без нового live proof.
4. Использовать `O1`, чтобы убрать вручную собранный audit/result wording из future redesign handlers.
5. Рассматривать `I1` только если появится новая подтверждённая host-risky boundary.

## Suggested branch split

1. `codex/computer-use-win-surface-freeze`
   - Владеет `C0`.
   - Может включать минимальные `A3` assertions, нужные для фиксации текущего public surface.

2. `codex/mcp-2025-11-25-baseline`
   - Владеет `M0`, `M1` и `M5`.
   - Поднимает negotiated/exported protocol baseline и синхронизирует generated contract surface.

3. `codex/mcp-2025-11-25-tool-semantics`
   - Владеет `M2`, `M3` и `M4`, если эти изменения не помещаются в baseline branch.
   - Должна явно маркировать `STDIO` in-scope и HTTP/auth/task items out-of-scope, если они не реализуются.

4. `codex/computer-use-win-instance-discovery`
   - Владеет deferred class 1.
   - Предпочтительное место для первого серьёзного `A1/A2/S1` extraction для `computer-use-win`.

5. `codex/computer-use-win-observe-prepare-split`
   - Владеет deferred class 2 только если продукт выбирает pure read-only observation.
   - Не должен стартовать раньше class 1, если нет concrete urgent client need.

6. `codex/computer-use-win-advisory-policy`
   - Владеет deferred class 3 только если текущая truthful observation failure semantics переоткрывается осознанно.

## Current recommendation

Следующей product-facing branch всё ещё должна стать instance-addressable discovery, но теперь перед ней появился ещё один обязательный инженерный слой: protocol migration branch на MCP `2025-11-25`. Рекомендуемая последовательность такая:

1. сначала заморозить deferred action wave (`C0`);
2. сделать narrow decomposition `ComputerUseWinTools` плюс initial dependency rules (`A1` + `A3`);
3. стабилизировать composition root (`CR1`) после появления первого extraction seam;
4. затем провести отдельную MCP migration branch (`M0-M5`), не смешивая её с product redesign;
5. только после этого запускать `instance-addressable discovery` branch как первую redesign branch, используя её для формирования explicit application boundary у `computer-use-win`.

Держать `get_app_state` observe/prepare split и advisory policy redesign parked до появления явной product/client need. Это всё ещё валидные deferred classes, но они не должны конкурировать ни с discovery branch, ни с ранним hardening, ни с protocol migration branch, которая нужна, чтобы дальнейшие public changes уже приземлялись на latest MCP baseline.
