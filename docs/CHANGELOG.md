# CHANGELOG

Политика: фиксировать только инженерно значимые изменения, влияющие на operating model, control plane, архитектуру, проверки или контракт инструментов.

## 2026-04-08 08:40

- После финальной verification wave exec-plan [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md) архивирован из `active` в `completed`, чтобы lifecycle `docs/exec-plans` снова оставался консистентным: в `active` остаются только живые workstream-ы, а полностью завершённый `windows.launch_process` хранится рядом с остальными completed plans.

## 2026-04-06 18:08

- Закрыт `Package E` в active workstream [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md): shipped rollout `windows.launch_process` теперь доказан end-to-end через реальный L3 smoke story, где `scripts/smoke.ps1` сначала делает safe dry-run preview, затем live helper launch через сам tool, cross-check-ит `processId` / `mainWindowHandle`, canonical `launch/<launch_id>.json`, полный safe parity set `launch.runtime.completed` и только потом переиспользует то же окно в attach/UIA/capture/wait части сценария. В том же цикле smoke получил explicit negative proof для dry-run path: preview-only branch теперь фиксируется internal event `launch.preview.completed`, factual runtime по-прежнему доказывается отсутствием `launch.runtime.completed`, а cleanup dry-run/live leaks больше не зависит от MCP transport и не special-case-ится на непустой baseline. Follow-up hardening усилил сам smoke harness на трёх осмысленных местах: live audit-marker polling теперь читает `events.jsonl` только до последней завершённой строки, чтобы append-in-progress не давал ложные JSON parse failures; ownership helper-а переведён с user-visible title на hidden run-scoped marker `--smoke-run-id`, чтобы cleanup не путал текущий smoke run с чужими helper-процессами; а fallback reconciliation теперь строится как OS-only bounded protocol, который сразу после dry-run/live tool call сохраняет marker-scoped ownership и ранний candidate snapshot по exact helper `ExecutablePath`, а в proof/cleanup фазе крутит единый resolution loop: на каждой итерации повторно открывает candidate processes и уже на текущем candidate set boundedly проверяет `MainWindowTitle`. Этот loop использует тот же helper window materialization budget, что и основной launch proof, поэтому orphan cleanup не требует follow-up `windows.list_windows` roundtrip даже при marker-loss, delayed process discovery, delayed title materialization и transport failure. Review-driven follow-up выровнял сам diagnostics layer: для runtime/proof markers добавлен единый `AuditLog.TryRecordRuntimeEvent(...)`, на него переведены launch/wait/uia runtime event writers и оба новых internal proof markers, так что observability write снова остаётся best-effort side effect и не меняет factual result ни для dry-run preview, ни для `visual_changed`. `docs/architecture/observability.md` теперь тоже явно разделяет public runtime events и internal proof markers, включая best-effort wording для `launch`, `wait` и `uia` investigation flow. Deterministic docs/export drift закрыт на source-of-truth уровне через `ToolContractExporter`, exporter tests и generated docs (`project-interfaces`, `commands`, `test-matrix`), product docs синхронизированы с shipped state (`okno-roadmap` row 13 -> `реализовано` / `85%`, `okno-spec` получил компактный контракт `windows.launch_process` и explicit best-effort wording для `artifactPath`), а completed exec-plan заархивирован рядом с остальными завершёнными workstream-ами. По пути пришлось сделать только минимальные blocker-fixes вокруг smoke harness и verification UX, не меняя public launch contract: visual helper переведён с endless heartbeat на bounded burst с explicit smoke-provided budget, helper arm теперь синхронизируется с real baseline-ready marker `wait.visual.baseline_captured` вместо arm-delay эвристики, helper lifetime по-прежнему передаётся явно из smoke и страхуется self-close timer-ом, foreground/focus waits в smoke стали менее flaky за счёт более устойчивых timeout budgets, а `PollingWaitService` coverage осталась сфокусированной на релевантных shipped contracts — bounded timeout plumbing, visual baseline runtime marker и process-isolated worker timeout — без branch-local теста, который нормализовал бы wait дольше публичного `timeoutMs`. Финальный sequential contour `scripts/build.ps1 -> scripts/test.ps1 -> scripts/smoke.ps1 -> scripts/refresh-generated-docs.ps1 -> scripts/codex/verify.ps1` пройден зелёным.

## 2026-04-06 11:20

- Закрыт `Package C` в active workstream [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md) без захода в evidence/artifact/smoke rollout: `windows.launch_process` переведён из internal contract freeze в реальный public MCP tool, `WindowTools` получил gated boundary только через `RunGatedAsync(...)`, а publication surface синхронизирован между `ToolContractManifest.All`, implemented/exported `okno.contract` и фактическим `tools/list`. В том же цикле boundary начал materialize-ить canonical payloads для `blocked`, `needs_confirmation`, `dry_run_only`, allowed dry-run preview и allowed live runtime path через `IProcessLaunchService`, причём semantic handoff для allowed-path failure закрыт явно: если shared gate уже дал `allowed`, но дальше boundary/runtime возвращает failure, public payload публикует `Decision=failed`, а исходный gate verdict остаётся только во внутреннем audit metadata. Follow-up внутри того же пакета убрал остаточный transport drift полностью: `windows.launch_process` больше не использует attribute/scalar auto-binding и зарегистрирован как programmatic MCP tool с explicit flat `inputSchema`, а canonical `LaunchProcessRequest` теперь всегда bind-ится из raw MCP `arguments`. Благодаря этому `environment`, произвольные extra fields, missing required `executable` и type-mismatch в published полях снова materialize-ятся как tool-level `failed` result вместо generic SDK error или ложного `needs_confirmation`. Узкие runtime/integration tests закрепили manifest/export/admin-visible sync, synthetic launch boundary coverage и реальный `tools/call` reject-path для extra transport fields и malformed published fields, но `launch.runtime.completed`, launch artifacts, smoke story и broad docs rollout по-прежнему остаются в следующих пакетах.

## 2026-04-06 13:50

- Закрыт `Package D` в active workstream [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md) без захода в smoke и broad product/docs rollout: `windows.launch_process` получил dedicated observability слой поверх уже shipped runtime/boundary semantics, причём factual source of truth остался один и тот же `LaunchProcessResult`. В `WinBridge.Runtime.Windows.Launch` добавлены `launch` artifact writer/materializer и runtime event `launch.runtime.completed`, поэтому каждый factual live runtime result теперь materialize-ит `artifacts/diagnostics/<run_id>/launch/launch-<timestamp>-<nonce>.json` и согласованный runtime event с safe metadata (`status`, `decision`, `resultMode`, `failureCode`, `executableIdentity`, `processId`, `startedAtUtc`, `hasExited`, `exitCode`, `mainWindowObserved`, `mainWindowHandle`, `mainWindowObservationStatus`, `artifactPath`, optional `failureStage` / `exceptionType`). Follow-up в том же пакете довёл redaction-first path до launch runtime evidence: existing `launch_payload` reuse-ится и для tool completion, и для `launch.runtime.completed`, `executable_identity` дополнительно normalizes-ится до basename, `working_directory`/`arguments` не попадают в audit event-data, а failure summary для launch runtime event больше не может утекать через `summary.md`. Для `artifact_write` failure выбран explicit fail-safe contract без semantic regression: public/result status не downcast-ится, artifact path остаётся `null`, runtime event несёт только `failure_stage=artifact_write` и `exception_type`, а launch artifact не пишет raw `exception_message`. Узкие L1/L2 tests закрепили runtime artifact/event alignment и completion-audit linkage по `artifact_path`, а docs sync в рамках пакета ограничен observability/generated interfaces/active exec-plan/changelog; smoke, roadmap/spec и full verification contour по-прежнему остаются на `Package E`.
- Follow-up hardening внутри того же `Package D` закрыл три точечных contract drift-а, найденных review pass: validation-only failures больше не materialize-ят launch artifact/event до входа в factual runtime path, runtime event write стал best-effort side effect и больше не может превратить уже состоявшийся launch в generic boundary failure, а `summary.md` теперь действительно несёт safe launch identifiers (`executableIdentity`, `processId`, `resultMode`, `artifactPath`) вместо wholly generic runtime message. Заодно strategy записи launch artifact переведена на staged temp-file publish с cleanup temp-path на failure, чтобы `artifact_write` не оставлял partially-published final artifact.
- Ещё один follow-up в том же `Package D` довёл эту policy до shipped public path целиком: `WindowTools` больше не считает completion audit обязательной semantic частью already-computed live launch result, поэтому поздний сбой `tool.invocation.completed` не может переписать factual `windows.launch_process` payload в generic failure после уже состоявшегося side effect. Это закреплено отдельным L2 integration test на blocked audit file после возврата runtime result.

## 2026-04-01 12:42

- Закрыт `Package B` в active workstream [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md) без захода в handler/publication/evidence rollout: добавлен отдельный capability project `WinBridge.Runtime.Windows.Launch` с публичным seam `IProcessLaunchService`, а runtime теперь умеет честно materialize-ить live `LaunchProcessResult` поверх frozen request contract из `Package A`. Direct launch path строится только через `ProcessStartInfo` с `UseShellExecute = false` и `ArgumentList`, optional `waitForWindow` реализован как runtime-only observation step через `WaitForInputIdle`, `Refresh` и `MainWindowHandle` без auto-focus, auto-attach и без premature public MCP publication. Follow-up hardening в том же пакете убрал lossy `File.Exists` / `Directory.Exists` preflight в пользу authoritative `Process.Start(...)` failure mapping, сделал `WaitForInputIdle` bounded/cancel-aware через короткие slices вместо одного долгого blocking wait и довёл time abstraction до конца через provider-aware observation delay, выровнял process-state snapshot между started/window observation ветками, включая cancel-path после старта, синхронизировал safe executable identity с validator-классификацией, чтобы rejected URI больше не тащили query/fragment в failure result, убрал ложноположительный `main_window_observation_not_supported`, развёл `input-idle timeout` от финального window verdict и принудил final deadline snapshot идти через свежий `Refresh()`-aware observation path. В результате `WaitForInputIdle(...)=false` теперь считается только промежуточным readiness-signal, late-after-deadline window больше не даёт ложный `window_observed`, exited snapshot больше не может маскироваться под `window_observed`, ambiguous `Win32Exception(3)` больше не сводится ни к `executable_not_found`, ни к `working_directory_not_found` без достаточного сигнала, а started-result ветки снимают fresh factual snapshot без legacy cached-state assumptions. В том же цикле `AddWinBridgeRuntime(...)` получил DI registration для нового launch service, active exec-plan checklist выровнен под фактически закрытые пункты `Package B`, а узкие L1 runtime tests закрепили direct start info semantics, `process_started` / `process_started_and_exited` / `window_observed` mapping и failure branches `process_exited_before_window`, `main_window_timeout` и `main_window_not_observed`.

## 2026-03-31 18:55

- Добавлен новый active exec-plan [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md) для следующего shipped action slice после safety baseline: документ фиксирует `windows.launch_process` как отдельный explicit executable/process launch tool, удерживает split с `windows.open_target`, переиспользует existing `RunGated(...)` / launch-readiness / `launch_payload` foundation и задаёт file-level integration map, official constraints, public DTO proposal, evidence/redaction contract, delivery packages, test ladder и smoke story без focus-dependent acceptance.

## 2026-03-31 18:32

- Закрыт `Package A` в active workstream [docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md](docs/exec-plans/completed/completed-2026-04-08-windows-launch-process.md) без захода в runtime/service/handler rollout: в `WinBridge.Runtime.Contracts` добавлен frozen DTO/value-set/validator seam для `windows.launch_process`, а в `WinBridge.Runtime.Tooling` зафиксирован internal-only `FutureLaunchProcessDescriptor` с canonical summary, parameter wording и final execution-policy metadata. Поверх этого boundary request contract доведён до canonical shape, чтобы следующий пакет не латал семантику задним числом: `timeoutMs` теперь distinguishable optional override, `args` получили canonical empty default и reject null-elements, request boundary fail-closed сохраняет и отклоняет любые unsupported extra fields, rooted directory/document/shell-open targets и drive-relative paths больше не проходят как launchable executable intent, а preview/result явным именованием фиксируют safe executable identity вместо raw path. При этом `windows.launch_process` намеренно не переведён в `ToolContractManifest.All`, `okno.contract`, `okno.health`, exporter и фактический `tools/list`, чтобы current shipped surface оставался честно синхронизирован с отсутствующим handler boundary. В том же цикле добавлены узкие runtime/integration tests на contract freeze и explicit absence из agent-visible surface, а active exec-plan checklist выровнен под фактически завершённые пункты `Package A`.

## 2026-03-31 18:05

- Структура `docs/exec-plans` приведена в порядок по фактическому lifecycle: завершённые планы `windows.uia_snapshot`, `windows.wait`, `okno.health + runtime guard layer`, `Okno Codex Plugin Surface` и `okno-safety-baseline` перенесены из `docs/exec-plans/active/` в `docs/exec-plans/completed/`, чтобы в `active` оставались только действительно живые workstream-ы.
- Архивированные планы получили явные `Статус: completed` и `Архивирован: ...`, а ссылки в changelog и self-reference внутри `windows.uia_snapshot` синхронизированы с новыми путями.

## 2026-03-31 16:31

- В repo docs зафиксирована отдельная compatibility track для OpenAI `computer use` без изменения ближайшего delivery order V1: `README`, product docs, architecture index и plugin README теперь явно разводят `shell`, `skills`, `MCP`, `computer use` и `Okno` по слоям ответственности вместо implicit chat-only договорённости.
- Добавлен новый source-of-truth документ [docs/architecture/openai-computer-use-interop.md](docs/architecture/openai-computer-use-interop.md), который фиксирует adapter boundary, vocabulary expectations для будущего `windows.input`, split `windows.launch_process` / `windows.open_target` и правило, что OpenAI-specific interop не должен протекать внутрь `WinBridge.Runtime` / `WinBridge.Server`.
- В roadmap и exec-plans интегрирован planned compatibility track: [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md) теперь явно удерживает `computer use` interop вне ближайшего `launch -> input -> clipboard` delivery order, а [docs/exec-plans/active/openai-computer-use-interop.md](docs/exec-plans/active/openai-computer-use-interop.md) задаёт activation criteria для будущего adapter-слоя после shipped `windows.input(click first)`.
- `AGENTS.md` получил минимальный reusable guardrail по этой теме: source of truth для OpenAI interop теперь явно привязан к architecture/exec-plan docs, а future `computer use` compatibility закреплена как отдельный adapter-слой без протекания OpenAI-specific contracts в core runtime и без подмены repo-local MCP/plugin path.

## 2026-03-31 10:30

- Закрыт `Package E` и вместе с ним весь workstream [docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md](docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md): shared launch-readiness policy перенесена из placeholder-модели в reusable guard-layer decision matrix без реализации `windows.launch_process`, `RuntimeGuardPolicy.BuildLaunch(...)` теперь честно различает `ready / degraded / blocked / unknown` по existing runtime facts (`desktop_session`, `session_alignment`, `integrity`), а `launch_elevation_boundary_unconfirmed` стал warning для medium-integrity live launch path вместо unconditional hard block. В том же цикле обновлены unit/integration tests на launch matrix, `okno.health` projection и synthetic gated boundary, smoke подтвердил live `launch=degraded` и отсутствие `launch` в `blockedCapabilities`, generated docs не дали tracked diff после `refresh-generated-docs.ps1`, а roadmap и checklist синхронизированы с фактическим ответом `да` на proof question для будущего `windows.launch_process`.

## 2026-03-31 15:20

- Post-baseline hardening перед следующим action slice устранил три остаточных риска, найденных review sprint: raw execution boundary теперь fail-fast запрещает вызывать policy-bearing tools без `RunGated(...)` / `RunGatedAsync(...)`, launch redaction дополнительно нормализует `executable` в runtime/completion event-data до безопасного basename вместо полного пути, а smoke helper `prepare_focus` переведён на синхронный handshake, чтобы `windows.wait(focus_is)` оставался строгим и при этом детерминированным verification-signal. В том же цикле добавлены узкие runtime tests на mandatory gate misuse и launch event-data redaction, а capability design policy теперь явно фиксирует gated-boundary invariant для tools с `ExecutionPolicy`.

## 2026-03-30 15:57

- Закрыт `Package C` из [docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md](docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md) без захода в `Package D`: в `WinBridge.Runtime.Diagnostics` добавлен минимальный tool-aware redaction seam (`AuditPayloadRedactor`, `AuditRedactionResult`, tool context resolution), `AuditLog` и `ToolExecution` переведены на sanitized request/failure path с fail-safe suppression, gated invocation теперь пишет canonical `decision`/`risk_level`/`guard_capability` markers через existing `ToolExecutionDecision`, а runtime/tool events больше не утекают raw `exception_message` в `events.jsonl` и `summary.md`. В том же цикле обновлены узкие unit/integration tests на redaction markers и отсутствие leakage для `windows.wait`, `windows.uia_snapshot` и synthetic gate boundary, плюс синхронизированы только минимально нужные architecture docs и checklist workstream-а.

## 2026-03-30 13:25

- Закрыт `Package B` из [docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md](docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md) без захода в `Package C/D`: добавлен reusable execution gate поверх existing `execution_policy` и `RuntimeGuardAssessment`, `ToolExecution` получил gated sync/async overload для canonical `allowed / blocked / needs_confirmation / dry_run_only` routing без rollout в shipped observe tools, а unit/integration tests теперь доказывают decision matrix, reuse runtime snapshot и synthetic server-boundary path без публикации нового action tool.

## 2026-03-30 11:03

- Закрыт `Package A` из [docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md](docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md) без захода в runtime gate/redaction rollout: в `WinBridge.Runtime.Tooling` добавлен typed `execution_policy` metadata layer для deferred action tools, `ToolDescriptor`/`ContractToolDescriptor` и общий export path (`okno.contract` + generated `project-interfaces`) теперь публикуют nested policy fields в canonical snake_case, а manifest заранее хранит internal-only presets для future `windows.launch_process` и `windows.open_target` без premature publication новых public tool names. В том же цикле обновлены узкие unit/integration tests на manifest/export alignment и literal set, а shipped behavior существующих tools и `okno.health` остались без изменения.

## 2026-03-30 10:20

- Добавлен новый exec-plan [docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md](docs/exec-plans/completed/completed-2026-03-31-okno-safety-baseline.md), который зафиксировал policy-first workstream после shipped reporting-first guard layer: canonical tooling metadata для future action tools, единый execution gate с `allowed / blocked / needs_confirmation / dry_run_only`, redaction-first diagnostics model, file-level integration map, official constraints, delivery packages, verification ladder и implementation checklist для follow-up `windows.launch_process`.

## 2026-03-27 08:34

- Добавлен repo-local Codex plugin `okno`: создан marketplace `.agents/plugins/marketplace.json`, plugin root `plugins/okno/.codex-plugin/plugin.json`, plugin-local MCP manifest `plugins/okno/.mcp.json`, launcher script `plugins/okno/run-okno-mcp.ps1`, plugin README и bundled skill `okno-runtime-workflow` с `agents/openai.yaml`.
- Plugin теперь публикует отдельный MCP server `okno`, который запускает уже собранный `Okno.Server.dll` через plugin-local launcher, не переписывая legacy home-level `windows` server.
- В корневом `README.md` обновлён раздел про repo-local Codex plugin и закреплено, что user-facing plugin surface использует продуктовое имя `Okno`, а repo-local MCP identity — `okno`.

## 2026-03-27 13:28

- Package D для `okno.health + runtime guard layer` закрыт как verification/docs sync wave без расширения runtime surface: raw MCP integration теперь валидирует legacy top-level summary contract и canonical projection `blockedCapabilities` / `warnings` на live MCP boundary, а `scripts/smoke.ps1` дополнительно закрепляет отсутствие выдуманного `artifactPath`, отсутствие dedicated health runtime event beyond generic `tool.invocation.*` и раннюю materialization health evidence.
- Smoke harness теперь materialize-ит health evidence в `artifacts/smoke/<run_id>/report.json` и `summary.md` сразу после успешного `okno.health`, а затем обновляет эти артефакты финальным full-run report, поэтому downstream failure больше не оставляет readiness investigation совсем без следа.
- Source-of-truth docs выровнены под фактический reporting-first rollout: roadmap больше не держит health workstream в `запланировано`, `README` и `architecture/index` описывают `okno.health` как public readiness/guard summary, `observability` явно фиксирует отсутствие dedicated health artifact/event как текущую contract boundary, а exec-plan Package D закрыт по фактическим verification/docs результатам без подмены evidence соседним repo-local plugin surface.

## 2026-03-30 11:40

- Review-driven hardening устранил несколько реальных root-cause проблем без возврата legacy/shim paths: plugin-local MCP manifest `plugins/okno/.mcp.json` больше не хранит machine-specific absolute paths и стартует через `powershell -NoProfile -NonInteractive`, launcher перенесён в plugin root как переносимая repo-local indirection, bundled skill теперь явно требует запуск repo control-plane команд из корня репозитория, а runtime guard desktop probe стал identity-aware — `Win32RuntimeGuardPlatform` теперь различает non-`Default` input desktop и policy больше не рекламирует live GUI observe paths как usable в non-active session. Дополнительно `launch` blocked summary всегда публикует intrinsic `launch_elevation_boundary_unconfirmed`, даже в otherwise healthy environment, а unit tests закрепляют эти contract edges.

## 2026-03-25 11:05

- `okno.health` расширен до contract-first readiness summary без runtime probes и hidden enforcement: `HealthResult` теперь публикует typed `Readiness`, `BlockedCapabilities` и `Warnings`, а live wording в `ToolDescriptions` и `ToolContractManifest` выровнен под conservative readiness/guard snapshot для shipped observe paths и ближайших deferred `input` / `clipboard` / `launch` capability.

## 2026-03-23 18:52

- Добавлен новый active exec-plan `okno.health + runtime guard layer`, который фиксирует следующий cross-cutting workstream после shipped `windows.uia_snapshot` и `windows.wait` как расширение существующего `okno.health`, а не как новый isolated tool slice: в плане зафиксированы boundary/non-goals, reporting-first модель readiness/guard domains, file-level integration map, official Windows/MCP constraints, delivery packages, test ladder, docs sync и implementation checklist для будущего authoritative runtime readiness snapshot.

## 2026-03-23 15:27

- `visual_changed` переведён на целевую модель `detection + best-effort evidence`: public flat contract теперь публикует `lastObserved.visualEvidenceStatus` (`materialized | timeout | failed | skipped`), baseline/current PNG paths стали optional и больше не определяют success-path, visual probe разбит на lightweight comparison sample и отдельный evidence frame, compare path materialize-ит fingerprint только внутри visual probe через direct `LockBuffer`/`IMemoryBufferByteAccess` path с официальным WinRT ABI contract без copy-based fallback в latency-sensitive poll loop, PNG evidence кодируется через WinRT `BitmapEncoder` c budget-aware async bridge, orchestration теперь делает wall-clock post-budget downgrade для late evidence completion, а `wait.runtime.completed` дополнительно пишет visual baseline/current artifact paths вместе со статусом evidence; runtime/integration tests, smoke и source-of-truth docs синхронизированы с новой semantics.

## 2026-03-23 13:07

- Review-driven hardening для shipped `windows.wait` закрыл подтверждённые семантические дыры без смены публичной schema: UIA multi-match classification теперь condition-specific (`element_exists` требует identity-overlap между candidate и recheck, `element_gone` продолжает polling пока matches остаются, `text_appears` считает только text-qualified candidates, а ambiguous result больше не публикует произвольный `matchedElement`), late UIA downgrade использует `WorkerCompletedAtUtc`, wait-specific visual probe больше не наследует скрытый `3s` capture cap и репортит effective threshold/evidence-path честно, а tool-boundary unexpected failures в `WindowTools.Wait(...)` теперь проходят через тот же canonical wait artifact + `wait.runtime.completed`, а не через голый sanitized handler path; новые runtime/integration tests закрепляют эти контракты регрессионно.

## 2026-03-23 10:02

- В локальный `AGENTS.md` добавлен точечный operational guardrail для verification loop: в этом репозитории нельзя параллелить `dotnet build/test`, `scripts/smoke.ps1`, `scripts/refresh-generated-docs.ps1`, `scripts/ci.ps1` и `scripts/codex/verify.ps1` в одном worktree, потому что они делят `bin/obj`, локальные runtime/fixture процессы и generated artifacts; canonical порядок для smoke/docs refresh теперь закреплён явно как строго последовательный.
- Review-driven hardening для `windows.wait` довёл internal diagnostics path и L3 smoke до архитектурно честного состояния: controlled helper commands теперь делают `element_gone` smoke-предусловие невакуумным, `focus_is` переставлен сразу за первый authoritative `active_window_matches` вместо позднего OS-dependent foreground restore path, UIA-based smoke waits используют отдельный semantic budget для mandatory recheck, `stack-inventory` генерируется из текущего repo/tooling state без захардкоженной snapshot-date, а canonical runtime path для `runtime_unhandled` failures сохраняет `exception_type` / `exception_message` во внутреннем wait artifact и `wait.runtime.completed`, тогда как для `artifact_write` эти diagnostics остаются только в runtime event, не раскрывая детали в публичный MCP `WaitResult`.

## 2026-03-23 08:59

- Package D для `windows.wait` закрыт как direct public rollout без сохранения legacy stub: `WindowTools.Wait(...)` теперь публикует final MCP schema `condition + selector + expectedText + hwnd + timeoutMs`, напрямую вызывает canonical `IWaitService` через existing `ResolveWaitTarget(...)`, возвращает runtime `WaitResult` как `structuredContent` + один `TextContentBlock` и строго маппит `isError` так, что только `done` остаётся success-path; `windows.wait` переведён в `Implemented` и `SmokeRequired`, `okno.contract` / exporter / tools-list больше не расходятся по lifecycle, `WinBridge.SmokeWindowHost` получил deterministic focus target и geometry-backed visual heartbeat для shipped `focus_is` / `visual_changed`, `scripts/smoke.ps1` теперь реально проверяет все public wait conditions с artifact assertions, а generated/manual docs и bootstrap status синхронизированы по фактическому shipped state.

## 2026-03-20 17:15

- Package C для `windows.wait` закрыт как runtime-only hardening без premature public rollout: `focus_is` теперь подтверждается только через authoritative focused-element path с retry/revalidation только для действительно transient `ElementNotAvailable`-класса ошибок, exact selector match внутри resolved window и корректной immediate-parent lineage metadata, `visual_changed` использует window-scoped raw-only visual probe с гарантированным положительным confirmation gap и noise policy на `16x16` grayscale grid (`per-cell delta >= 12`, populated-cell scaled threshold, geometry-change shortcut) без per-tick PNG bloat, без лишних raw allocations в обычном `windows.capture` и без удержания raw baseline frame между poll-итерациями, `WaitOptions` больше не допускает non-positive polling cadence, baseline и final visual artifact write path теперь используют общий remaining-budget helper и целиком нормализуют filesystem/encode failures в доменный `failed`, process-isolated UIA wait probe разделяет host completion и worker completion timestamps для корректного timeout enforcement, а DI/runtime tests закрепляют новый visual probe seam при сохранении lifecycle `windows.wait` в `Deferred/unsupported`.

## 2026-03-20 15:29

- Follow-up hardening закрепил ownership timeout и evidence contract на правильных границах: `PollingWaitService` теперь явно передаёт worker boundary remaining budget на каждый UIA probe и повторно классифицирует просроченный probe result как `timeout` по execution metadata, process-isolated wait probe больше не наследует скрытый snapshot-centric 3s cap, а worker-level `diagnostic_artifact_path` больше не теряется и попадает в wait observation / wait artifact / runtime audit trail.

## 2026-03-20 15:50

- Финальная review-driven правка для Package B убрала смешение deadline и failure semantics в `PollingWaitService`: service теперь сначала строит семантический outcome UIA probe (`failed` / `ambiguous` / `pending` / `candidate` / `timeout`) и только потом понижает просроченный результат до `timeout` для success-like веток, поэтому late `worker_process` и другие runtime failures больше не маскируются под `timeout`, а существующий red-test на delayed failure фиксирует этот контракт регрессионно.

## 2026-03-20 15:29

- Follow-up hardening закрепил ownership timeout и evidence contract на правильных границах: `PollingWaitService` теперь явно передаёт worker boundary remaining budget на каждый UIA probe и повторно классифицирует просроченный probe result как `timeout` по execution metadata, process-isolated wait probe больше не наследует скрытый snapshot-centric 3s cap, а worker-level `diagnostic_artifact_path` больше не теряется и попадает в wait observation / wait artifact / runtime audit trail.

## 2026-03-20 14:43

- Review-driven доработка execution boundary довела timeout hardening до архитектурно честного состояния: UIA wait probe больше не остаётся внутрипроцессным cooperative-only MTA path, а исполняется через process-isolated worker boundary, общий по launch/timeout/kill semantics с существующим isolated UIA slice; это убирает расхождение между reported `timeout` и фактическим завершением blocked UIA/COM execution unit.

## 2026-03-20 14:12

- Review-driven hardening для Package B убрал три реальные P1-дыры в `windows.wait`: UIA-backed probes теперь bounded оставшимся `timeoutMs` через отдельную deadline policy на execution boundary, `active_window_matches` подтверждает именно resolved top-level `HWND`, а `text_appears` требует тот же `matched_text_source` на финальном recheck; для всех трёх случаев добавлены red-first runtime tests, чтобы regression path был доказуемо закрыт.

## 2026-03-20 11:14

- Package B для `windows.wait` закрыт как runtime-only slice без premature public rollout: wait contracts перешли на typed selector + expectedText shape, `PollingWaitService` добавил bounded poll loop с final same-source revalidation и честным разделением `done` / `timeout` / `ambiguous` / `failed`, `Windows.UIA` получил минимальный live wait probe seam для `active_window_matches`, `element_exists`, `element_gone` и `text_appears`, а diagnostics слой теперь пишет отдельный JSON artifact в `artifacts/diagnostics/<run_id>/wait/` и runtime audit event `wait.runtime.completed`, сохраняя `windows.wait` в lifecycle `Deferred/unsupported` до Package D.

## 2026-03-20 09:16

- Package A для `windows.wait` закодирован без premature rollout: добавлены typed `Wait*` contracts, `IWaitService` больше не пустой, shell resolver получил capability-specific `ResolveWaitTarget(...)` с precedence `explicit -> attached -> active` и без fallback из stale explicit/attached target, а deferred manifest/exported contract теперь честно помечает `windows.wait` как `os_side_effect`, сохраняя lifecycle `Deferred`.

## 2026-03-20 08:30

- Source-of-truth docs для следующего slice `windows.wait` выровнены между exec-plan, product spec и roadmap: V1 теперь везде описан как один публичный tool `windows.wait`, а не zoo из `wait_for_*`; target model синхронизирован как `explicit -> attached -> active` без hidden activation/auto-attach drift, summary-row roadmap больше не теряет `text appears`, а сам exec-plan честно меняет MCP annotation expectation на `ReadOnly = false`, потому что wait обязан писать diagnostics artifact.

## 2026-03-19 18:23

- Добавлен отдельный exec-plan для shipped public slice `windows.wait` в [docs/exec-plans/completed/completed-2026-03-23-windows-wait.md](docs/exec-plans/completed/completed-2026-03-23-windows-wait.md): план фиксирует V1 boundary, polling-first target model `explicit -> attached -> active`, condition matrix для `active/focus/element/text/visual`, honest status/error model, evidence contract, file-level integration map, L1/L2/L3 ladder, docs sync и rollback без преждевременного захода в `windows.uia_action` или broad `windows.input`.

## 2026-03-19 14:34

- Финальный branch-аудит по `windows.uia_snapshot` добрал остаточный doc drift вне основного rollout-контура: `architecture/index`, `architecture/layers`, `product/okno-roadmap`, `generated/stack-inventory` и `generated/stack-research` больше не описывают UIA как future seam/deferred path там, где текущий код уже публикует shipped public `windows.uia_snapshot`.
- Review-driven hardening для `windows.uia_snapshot` закрыл четыре корневые проблемы public rollout сразу на boundary-уровне: shared `UiaSnapshotRequestValidator` теперь одинаково валидирует request в server/runtime и держит публичный `maxNodes` в диапазоне `1..1024`, runtime service больше не публикует stale pre-resolution descriptor и теперь берет `window` только как sparse runtime-observed metadata из того же backend capture path, который построил фактический root/subtree, diagnostics boundary получил отдельный sanitized-failure path с сохранением `exception_type`/`exception_message` в audit trail, а smoke-контуру добавлен polling до materialized semantic subtree вместо single-shot UIA assertions.

## 2026-03-19 13:25

- `windows.uia_snapshot` доведён до shipped public slice: `Okno.Server` теперь публикует live MCP handler с `hwnd + depth + maxNodes`, `CallToolResult`, `structuredContent` и одним `TextContentBlock` без image block; target policy `explicit -> attached -> active` честно резолвится через existing shell seam, а stale/ambiguous target cases больше не маскируются deferred stub-ом.
- Public rollout осознанно изменил server runtime contract: `Okno.Server` получил host-facing зависимость на `WinBridge.Runtime.Windows.UIA.Hosting`, runtime config теперь legitimately включает `Microsoft.WindowsDesktop.App`, а boundary protection перенесён из pre-rollout запрета в positive guard на staged UIA worker artifacts и новый WindowsDesktop-backed shipped state.
- Smoke и generated/docs слой выровнены под live contract: helper window теперь содержит предсказуемый semantic subtree на WinForms standard controls, `McpProtocolSmokeTests` и `scripts/smoke.ps1` проверяют attached-source UIA snapshot и JSON evidence artifact, `project-interfaces`/`bootstrap-status`/`test-matrix` больше не держат UIA в deferred scope, а `observability`/`okno-spec`/`okno-roadmap` синхронизированы по фактическому shipped behavior.

## 2026-03-19 11:16

- Review-driven hardening для `windows.uia_snapshot` развёл semantics bounded traversal по двум независимым границам: `UiaSnapshotTreeBuilder` больше не зондирует child nodes после исчерпания `MaxNodes`, `Truncated` остаётся только флагом доказанного clipping, а новый `node_budget_boundary_reached` фиксирует strict no-probe budget boundary так же явно, как `depth_boundary_reached` фиксирует depth boundary.
- Isolated worker transport boundary доведён до evidence-grade поведения: parent/worker stdio теперь жёстко зафиксированы на UTF-8, stdin/process/payload failures маппятся в typed `worker_process` result без unhandled leak, raw worker stderr больше не публикуется как operator-facing `Reason`, а сохраняется в отдельный diagnostic artifact с `diagnostic_artifact_path` в runtime audit event.
- Worker packaging переведён на publish-aware consumer graph: `WinBridge.Runtime.Windows.UIA.Worker` включён в hosting graph как non-copying sidecar dependency, build и publish path stage-ят build/published artifacts раздельно, а runtime launcher теперь использует явный dual launch contract (`worker.exe` или apphost-less `worker.dll` через текущий `dotnet` host), поэтому valid framework-dependent `UseAppHost=false` consumer publish больше не ломает UIA sidecar path.

## 2026-03-19 09:51

- Consumer-facing DI boundary для `windows.uia_snapshot` доведён до self-contained registration contract: в `WinBridge.Runtime.Diagnostics` выделен общий `AddWinBridgeRuntimeDiagnostics(...)`, `WinBridge.Runtime` и `WinBridge.Runtime.Windows.UIA.Hosting` переиспользуют один и тот же diagnostics/time-provider fragment, а `AddWinBridgeRuntimeWindowsUia(...)` теперь явно принимает `contentRootPath` и `environmentName` вместо скрытой зависимости от чужого composition root.
- Добавлен минимальный consumer regression test на разрешение `IUiAutomationService` через hosting boundary без ручного дотягивания `AuditLog`/`AuditLogOptions`/`TimeProvider`; staging worker-а и DI resolution теперь проверяются как один согласованный hosting contract.

## 2026-03-19 09:30

- Follow-up hardening для `windows.uia_snapshot` довёл packaging/runtime boundary до consumer-facing контракта: `WinBridge.Runtime.Windows.UIA.Hosting` теперь stage-ит `WinBridge.Runtime.Windows.UIA.Worker.exe/.dll/.deps.json/.runtimeconfig.json` в собственный output и в output потребителей через явный MSBuild target, поэтому helper worker больше не появляется случайно только из-за прямых test references.
- В том же цикле deployment failure перестал ломать DI construction: `ProcessIsolatedUiAutomationBackend` проверяет наличие worker-а только на execution path и переводит misdeployment в `failed` result + `uia.snapshot.runtime.completed`, а `UiAutomationMtaRunner` закреплён как cooperative MTA utility, тогда как единственным strict-timeout owner остаётся isolated process boundary.

## 2026-03-18 16:58

- `windows.uia_snapshot` получил только Package B runtime/evidence слой без premature public rollout: добавлены `Win32UiAutomationService`, managed UIA backend на отдельном MTA thread, `ElementFromHandle` root acquisition, bounded `control view` traversal, enforcement `Depth`/`MaxNodes`/`Truncated` и JSON artifact под `artifacts/diagnostics/<run_id>/uia/`.
- Follow-up hardening этого же пакета убрал unintended host dependency drift и diagnostic gaps: UIA registration вынесена в отдельный host-facing project `WinBridge.Runtime.Windows.UIA.Hosting`, bootstrap `Okno.Server` снова не требует `Microsoft.WindowsDesktop.App` до Package C, worker artifact больше не ищется в `repo Debug` layout, production execution boundary переведена на isolated worker process, evidence shape теперь включает `requested_max_nodes`, `depth_boundary_reached` и `failure_stage`, а tree builder больше не перегружает `Truncated` ложноположительными depth-boundary срабатываниями.

## 2026-03-18 16:04

- Ещё один review-driven hardening проход довёл UIA target policy до строгой семантики explicit target: `explicitHwnd <= 0` в `windows.uia_snapshot` больше не нормализуется в “target absent”, а возвращает явный `stale_explicit_target`; при этом общий resolver для существующих `focus/capture` путей остаётся без этого UIA-specific правила.
- Deterministic generation для tracked docs усилена до byte-level policy: `scripts/refresh-generated-docs.ps1` теперь не только пишет фиксированный UTF-8, но и нормализует line endings, а `bootstrap-status.json` получает полный JSON string escaping без runtime-dependent serializer drift между `powershell.exe` и `pwsh`.

## 2026-03-18 15:41

- Review-driven hardening локализовал `explicitHwnd <= 0` обратно в `windows.uia_snapshot` policy path: общий `ResolveExplicitOrAttachedWindow(...)` больше не меняет semantics для существующих `windows.focus_window` и `windows.capture`, а regression tests теперь явно страхуют `hwnd: 0` при attached session window.
- `scripts/refresh-generated-docs.ps1` доведён до кросс-shell deterministic generation: tracked generated files теперь пишутся через общий UTF-8 writer с фиксированной BOM-policy, а `bootstrap-status.json` больше не зависит от различий `ConvertTo-Json` между `powershell.exe` и `pwsh`.

## 2026-03-18 15:22

- Review-driven fix для harness encoding вернул `scripts/refresh-generated-docs.ps1` в совместимый с Windows PowerShell режим: `.editorconfig` теперь фиксирует `utf-8-bom` для `*.ps1`, а сам скрипт повторно сохранён с BOM, чтобы `powershell.exe -File` больше не падал на non-ASCII литералах.
- В том же цикле deterministic generated docs остались без run-specific smoke metadata, но `refresh-generated-docs.ps1`, `ci.ps1` и `scripts/codex/verify.ps1` снова проходят именно через стандартный Windows PowerShell execution path, а не только через текущую `pwsh`-сессию Codex.

## 2026-03-18 12:59

- `windows.uia_snapshot` переведён на package-aware execution plan: active exec-plan теперь явно разделяет `Package A` (target policy + typed groundwork), `Package B` (runtime/evidence) и `Package C` (server rollout/smoke/generated docs), чтобы текущий цикл больше не притворялся полным rollout до `Implemented`.
- Для `windows.uia_snapshot` зафиксировано official-docs-driven meaning `active = foreground top-level window`, а product wording в spec/roadmap выровнен под precedence `explicit -> attached -> active` без преждевременного опубликования live MCP contract.
- В коде добавлены typed `UiaSnapshot*` DTO, typed `IUiAutomationService` seam и capability-specific `UiaSnapshotTargetResolution`; при этом public handler, manifest lifecycle и generated docs намеренно оставлены в честном deferred state.

## 2026-03-18 15:10

- Review-driven hardening для `windows.uia_snapshot` убрал внутреннюю двусмысленность groundwork: service-level request больше не несёт второй authoritative target, defaults централизованы в `UiaSnapshotDefaults`, а `UiaSnapshotResult` больше не может родиться с implicit success status.
- Active-path policy в `WindowTargetResolver` сделана snapshot-consistent: explicit `hwnd <= 0` нормализуется как absent target, active candidate выбирается из одного `ListWindows` snapshot, duplicate foreground entries по тому же `HWND` дедуплицируются, а `missing_target` и `ambiguous_active_target` больше не смешиваются.
- `scripts/refresh-generated-docs.ps1`, `docs/generated/*`, `docs/bootstrap/bootstrap-status.json` и tool contract export переведены на deterministic generation: tracked docs больше не содержат run-specific smoke ids, absolute artifact paths и `generated_at_utc`, а конкретное evidence остаётся только в локальных `artifacts/`.

## 2026-03-18 11:08

- Добавлен отдельный exec-plan для shipped capability slice `windows.uia_snapshot` в [docs/exec-plans/completed/completed-2026-03-19-windows-uia-snapshot.md](docs/exec-plans/completed/completed-2026-03-19-windows-uia-snapshot.md): план фиксирует contract-first goal, non-goals, official Microsoft/MCP constraints, file-level integration map, typed DTO/result shape, L1/L2/L3 ladder, docs sync и rollback policy без преждевременного захода в `windows.wait`, `windows.input` или `windows.uia_action`.
- В тот же exec-plan добавлена явная target policy `explicit -> attached -> active` для `windows.uia_snapshot` с запретом на silent fallback из stale attached/explicit target и расширен обязательный docs-sync до полного source-of-truth набора по slice: `product/index`, `okno-spec`, `okno-roadmap`, `okno-vision`, `observability` и generated docs.

## 2026-03-18 09:50

- `docs/product/okno-roadmap.md` переведён из чисто greenfield narrative в repo-aligned operational roadmap: документ теперь явно фиксирует текущий реализованный bootstrap baseline (`Server` / `Contracts` / `Diagnostics` / `Session` / `Windows.Display` / `Windows.Shell` / `Windows.Capture`) и различает `реализовано`, `частично`, `только_seam` и `запланировано` для capability slices.
- В roadmap добавлена таблица приоритетов в реальных именах репозитория и tool surface, чтобы execution order больше не расходился с текущим contract/runtime: следующий рубеж формулируется как `windows.uia_snapshot -> windows.wait -> environment/safety -> launch -> input`, а не как старый абстрактный порядок из greenfield-версии.
- Порядок приоритета при нехватке времени и итоговая формулировка V1 выровнены под текущий repo state: `launch` разделён на `windows.launch_process` и `windows.open_target`, а преждевременное расширение `windows.input` прямо зафиксировано как риск до появления snapshot/wait/guard layers.
- `docs/product/okno-vision.md` и `docs/product/okno-spec.md` дополнительно выровнены по concrete tool naming: V1 core tools теперь используют `windows.list_monitors`, `windows.focus_window`, `windows.activate_window`, `windows.clipboard_get` и `windows.clipboard_set`, а clipboard/paste терминология больше не расходится между vision/spec/roadmap.

## 2026-03-17 16:07

- WGC one-shot capture path больше не считает первый пришедший frame автоматически годным: runtime теперь валидирует `Direct3D11CaptureFrame.ContentSize`, допускает ровно один `Direct3D11CaptureFramePool.Recreate(...)` и сохраняет PNG только после стабилизации геометрии кадра.
- Persistent WGC size drift после single `Recreate` теперь считается отдельным acquisition failure class: для `desktop` runtime уходит в существующий `Graphics.CopyFromScreen` fallback, а для `window` возвращает честный tool-level error без подмены window semantics screen-copy screenshot'ом.
- Metadata и artifact materialization больше не опираются на pre-acquisition target snapshot: после WGC stabilization runtime пере-выравнивает target fields под финальный PNG, а перед desktop fallback заново резолвит текущий monitor target вместо silent reuse устаревших bounds.
- Для успешного `desktop` WGC path monitor identity больше не может silently перепрыгнуть на новый live monitor после refresh: refreshed topology используется только если она подтверждает тот же target, по которому уже был создан capture item.
- Добавлены unit tests на sizing/failure policy и authoritative target materialization, чтобы WGC stabilization не расходилась с MCP metadata path.

## 2026-03-17 10:30

- Review-driven hardening закрыл подтверждённые P1/P2 gaps: MCP server теперь явно включает per-monitor DPI awareness в startup bootstrap, а integration smoke ждёт достижение этого process invariant перед валидацией contract.
- Display identity diagnostics переведены на отдельный builder/state machine: query failures больше не могут ложно репортиться как `display_config_strong`, а деградация `GetTargetName` теперь сохраняется в typed diagnostics без понижения strong identity до `gdi_fallback`.
- `okno.contract` обогащён до структурированных `ContractToolDescriptor` вместо списка имён, а `ToolContractManifest` и generated interfaces теперь публикуют те же полные описания, что и MCP `tools/list`.
- Manual docs синхронизированы с новым contract: `observe-capture`, `observability` и `okno-spec` больше не описывают удалённый monitor `dpi scale` и теперь отражают `coordinateSpace`, window-authoritative DPI и display identity diagnostics.

## 2026-03-17 10:44

- Закрыт residual bug в `DisplayIdentityDiagnosticsBuilder`: mixed-case `GetTargetName` degradation + `gdi_fallback` больше не может выдавать противоречивое сообщение в духе "strong identity preserved"; добавлен red/green unit test на этот сценарий.

## 2026-03-17 10:53

- Stage 3 contract surface доведён до консистентности: `okno.contract` и export/generated contract теперь используют общий `ContractToolDescriptorFactory`, а enum-like поля `lifecycle` и `safetyClass` публикуются в одном canonical snake_case literal format без drift между live tool и exported interfaces.

## 2026-03-17 11:40

- Review-driven closeout для workstream 1/2/3 завершён: `Win32WindowManager` больше не маскирует `GetDpiForWindow == 0` значением `96`, coverage-driven `gdi_fallback` теперь получает typed reason `display_config_coverage_gap`, а manual spec `windows.list_monitors` / `windows.list_windows` синхронизирована с фактическими diagnostics и window DPI fields.

## 2026-03-17 12:15

- Verification gaps закрыты локальными test seams: добавлен pipeline-level runtime test на mixed-case display failures (`GetTargetName` -> `GetSourceName`) и behavioural integration test на `windows.capture(scope="desktop", hwnd=...)`, подтверждающий monitor resolution explicit HWND поверх attached window. Smoke по-прежнему проверяет tools/list metadata и end-to-end protocol flow, а не подменяет эти два таргетных контракта.

## 2026-03-17 09:39

- Перестроен display/window contract: `MonitorDescriptor` больше не несёт authoritative `DpiScale`, `WindowDescriptor` теперь содержит `EffectiveDpi`, а `windows.capture` возвращает `coordinateSpace=physical_pixels` и window-authoritative DPI metadata только для window targets.
- `WinBridge.Runtime.Windows.Display` теперь возвращает typed `DisplayTopologySnapshot` с `DisplayIdentityDiagnostics`; `windows.list_monitors`, `okno.health`, audit `events.jsonl` и summary получили evidence о `display_config_strong` vs `gdi_fallback` без изменения safe fallback behavior.
- MCP surface стал самодокументируемым: добавлен единый `ToolDescriptions` source of truth, `DescriptionAttribute` на ключевые tools/parameters и smoke/integration checks на наличие descriptions в `tools/list`.
- `IsWindowArranged` переведён в optional metadata probe через runtime export lookup, чтобы `windowState=arranged` оставался enrichment-сигналом и не создавал жёсткой платформенной зависимости inventory.

## 2026-03-17 15:15

- `refresh-generated-docs.ps1` больше не публикует ложный `green` в `docs/generated/test-matrix.md`: матрица стала декларативной, а не evidence-claiming. В том же цикле добавлена `Normalize-SmokeReport`, чтобы generated docs не падали на legacy/partial smoke reports, а `GetMonitorInfo` degradation теперь поднимается в typed diagnostics вместо тихого skip-case.

## 2026-03-17 09:21

- Добавлен exec-plan `docs/exec-plans/completed/completed-2026-03-17-display-window-contract-hardening.md` для линейного refactor-контура по DPI/coordinate semantics, display identity diagnostics, MCP self-documentation и optional `IsWindowArranged`.

## 2026-03-15 14:57

- `windows.activate_window` теперь маркирует `ambiguous` как tool-level error на MCP boundary, а не как успешный result.
- `WinBridge.Server.IntegrationTests` получил явный build dependency на `WinBridge.SmokeWindowHost`, чтобы deterministic helper window собирался вместе с integration graph.
- Display seam переведён на source/view-oriented `monitorId` для captureable desktop views; alias fallback для stale strong ids убран, а `windows.list_windows` перешёл на batch-friendly monitor lookup без full scan на каждый HWND.
- Smoke harness переведён на helper как канонический attach/capture target: сначала normal window capture, затем `minimize -> activate_window -> helper capture`, при этом acceptance для activation path выровнен с платформой как `done | ambiguous` вместо безусловочного `done`.

## 2026-03-16 14:58

- `windows.activate_window` теперь возвращает `done` только когда финальный live snapshot подтверждает usable state: окно остаётся foreground и уже не находится в `minimized`.
- `WinBridge.Runtime.Windows.Display` зарегистрирован как полноценный runtime slice в `WinBridge.sln`, а `scripts/refresh-generated-docs.ps1` больше собирает `runtime_projects` автоматически по `WinBridge.Runtime*` csproj, чтобы bootstrap inventory не дрейфовал от фактической архитектуры.

## 2026-03-16 15:55

- Helper-based smoke harness переведён на contract-true readiness: helper считается готовым только после появления в `windows.list_windows(includeInvisible=false)`, а не сразу после появления `MainWindowHandle`.
- Post-activation helper capture в smoke и raw MCP integration smoke теперь ждёт фактическую capturable ready-state через `windows.capture(scope="window")`, вместо безусловочного принятия любого `ambiguous` как достаточного для следующего шага.

## 2026-03-16 16:14

- Smoke harness и raw MCP integration smoke переведены на session-scoped JSON-RPC request ids и строгий response matching по ожидаемому `id`, чтобы retry-циклы не могли переиспользовать корреляционные идентификаторы и ловить ответы не от своей фазы.

## 2026-03-16 16:56

- `windows.activate_window` больше не собирает final verdict из разрозненных raw-handle probes: финальная verification теперь опирается на единый platform-backed snapshot с identity (`ProcessId`/`ThreadId`/`ClassName`) и usability (`IsForeground`/`IsMinimized`) signals, а `done` выдаётся только по нему.

## 2026-03-15 12:36

- `windows.attach_window` теперь разводит invalid selector и ambiguous match: отсутствие `hwnd`/`titlePattern`/`processName` возвращается как `failed`, а `ambiguous` остаётся только для реального multi-match path.
- Добавлены integration tests на `windows.focus_window` и contract semantics `windows.attach_window`, включая success/failure paths, `already_attached` и гарантию, что focus не мутирует session state.
- `docs/generated/test-matrix.md` переведён в generated-артефакт через `scripts/refresh-generated-docs.ps1`, чтобы coverage-summary не расходился с `commands.md`.

## 2026-03-15 13:58

- Добавлен новый `Windows.Display` seam с monitor inventory и strong `monitorId` на основе `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo`, а `windows.list_windows` и `windows.capture` теперь становятся monitor-aware.
- Добавлены новые MCP tools `windows.list_monitors` и `windows.activate_window`; `windows.capture(scope="desktop")` теперь поддерживает explicit `monitorId`, а `window capture` для minimизированного окна честно требует предварительный `activate_window`.
- Smoke harness и raw stdio integration smoke расширены до deterministic helper-window сценария: explicit desktop capture по `monitorId`, `minimize -> activate_window -> window capture`.

## 2026-03-12 16:16

- Инициализирован harness bootstrap: созданы `docs/`, ExecPlan, верхнеуровневый `AGENTS.md`, solution/project skeleton и baseline-конфиг для `net8.0-windows`.
- Добавлен рабочий `STDIO` MCP skeleton runtime на `C# / .NET 8`, включая `windows.list_windows`, `windows.attach_window`, `windows.focus_window` и честный deferred tool contract.
- Нормализованы `scripts/bootstrap.ps1`, `scripts/build.ps1`, `scripts/test.ps1`, `scripts/smoke.ps1`, `scripts/ci.ps1`, `scripts/investigate.ps1` и `scripts/codex/*`.
- Проверки подтверждены командами `dotnet build`, `dotnet test`, `powershell -File scripts/smoke.ps1` и `powershell -File scripts/ci.ps1`.
- Продуктовое имя выровнено на `Okno`, product docs перенесены в `docs/product/`, добавлены `README.md` и `docs/architecture/engineering-principles.md`.

## 2026-03-12 21:02

- Реализован первый observe-loop `windows.list_windows -> windows.attach_window -> windows.capture` с `CallToolResult`, `structuredContent`, `image/png` и локальным PNG artifact в `artifacts/diagnostics/<run_id>/captures/`.
- `windows.capture` переведён из deferred в implemented tool contract, добавлен в smoke-required контур и покрыт server-side/integration тестами.
- Для `window` и `desktop` capture выбран `Windows.Graphics.Capture` как основной backend, с native GDI fallback для target-specific случаев, где WGC не даёт стабильный frame вовремя.
- `scripts/smoke.ps1`, generated docs и bootstrap status синхронизированы под новый observe/capture slice.

## 2026-03-13 14:15

- Добавлена универсальная policy для проектирования новых capability slices в [docs/architecture/capability-design-policy.md](architecture/capability-design-policy.md): identity, lifecycle, fallback, false-success, evidence и verification matrix.
- `architecture/index`, `engineering-principles` и `AGENTS.md` теперь явно ссылаются на эту policy как на обязательный baseline для следующих `focus`, `clipboard`, `input`, `wait`, `uia` и других feature.

## 2026-03-13 09:40

- Ужесточена семантика `window capture`: minimизированный `HWND` теперь считается явной ошибкой, а не допустимым success target.
- Fallback policy разделена по `scope`: `Graphics.CopyFromScreen` оставлен только для `desktop monitor` path, но больше не подменяет `window` semantics.
- Desktop fallback теперь достигается и в `WGC unsupported` средах, а не только после timeout/native error в WGC path.
- Untargeted `desktop` capture теперь явно выбирает primary monitor через Win32 monitor enumeration, а не через virtual-origin heuristic.
- `desktop` capture теперь корректно падает в primary monitor и при stale attached window, вместо того чтобы требовать ручного исправления session state.
- Attached target теперь переоценивается по `HWND + ProcessId + ThreadId + ClassName`; `Title` снова не считается обязательной частью identity.
- MCP annotations для `windows.capture` выровнены с реальным поведением: tool больше не `readOnly`/`idempotent`, и теперь помечен как `openWorld`.
- Expected operational failures в capture path нормализованы в `CaptureOperationException`, чтобы клиент получал tool-level `isError=true`, а не transport-level exception.
- `FrameArrived` теперь освобождает лишние `Direct3D11CaptureFrame`, а smoke/integration selection выбирают candidate через реальные attach/capture success criteria вместо геометрических эвристик.
