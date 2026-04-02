# ExecPlan: windows.launch_process

Статус: in_progress
Создан: 2026-03-31
Актуально на: 2026-04-01

## 1. Goal

Спроектировать и подготовить shipped public slice `windows.launch_process` как explicit executable/process launch tool поверх уже закрытого safety foundation.

Жёсткая формулировка цели:

- опубликовать отдельный tool именно для explicit process launch через `ProcessStartInfo` и `Process.Start(...)`;
- не смешивать его с будущим `windows.open_target`;
- переиспользовать уже существующие shared gate, launch readiness и redaction;
- сохранить ближайший delivery order `windows.launch_process -> windows.open_target -> windows.input`;
- оставить focus/attach/elevated interaction отдельными явно проверяемыми шагами, а не скрытыми post-launch эффектами.

## 2. Non-goals

- не реализовывать `windows.open_target`;
- не добавлять shell-open для файлов, документов или URL;
- не реализовывать `windows.input`;
- не вводить OpenAI-specific adapter logic в `WinBridge.Runtime` или `WinBridge.Server`;
- не строить V1 вокруг alternate credentials, `ProcessStartInfo.UserName`/`Password`, `Verb`, `Verb=runas` или target-specific elevation choreography;
- не дублировать mini-policy или отдельную reason taxonomy внутри handler-а;
- не делать stdout/stderr capture частью public feature `windows.launch_process`;
- не делать automatic attach/focus/activate частью success semantics.

## 3. Current repo state

По состоянию на `2026-03-31` slice нельзя планировать как greenfield:

- roadmap уже ставит `windows.launch_process` следующим planned slice после shipped safety baseline и до `windows.open_target` / `windows.input`;
- product spec уже закрепил split `windows.launch_process` / `windows.open_target`;
- completed safety baseline прямо отвечает `да` на вопрос, можно ли строить `windows.launch_process` без новой safety-логики внутри handler-а;
- shared launch-readiness уже materialized в `RuntimeGuardPolicy.BuildLaunch(...)` и различает `ready / degraded / blocked / unknown`;
- medium-integrity live path уже выражается не hard block-ом, а `launch=degraded` + warning `launch_elevation_boundary_unconfirmed`;
- raw execution для policy-bearing tools fail-fast запрещён: будущий handler обязан входить только через `ToolExecution.RunGated(...)` / `RunGatedAsync(...)`;
- `launch_payload` redaction class уже существует и покрывает request/event-data path для `executable`, `args`, `workingDirectory`, `environment`, full path suppression и fail-safe marker path;
- `ToolContractManifest` уже хранит internal-only future policy preset для `windows.launch_process`, но `ToolNames` пока не публикует новый public tool name и manifest surface ещё не advertises его как implemented;
- `WindowTools` уже является текущей boundary для публичных `windows.*` tools и содержит deferred-stub pattern для будущих action tools;
- в smoke и integration harness уже есть детерминированный helper `WinBridge.SmokeWindowHost.exe`, плюс готовые паттерны `ProcessStartInfo.UseShellExecute = false`, `WaitForInputIdle`, `Refresh`, `MainWindowHandle` polling.

Следствие:

- новый exec-plan должен описывать именно tool semantics, DTO seam, runtime launch service seam, evidence contract и docs/test rollout;
- shared gate, readiness, decision matrix и redaction path считаются готовыми foundation и не перепридумываются.

## 4. Official constraints

### .NET process launch

- `Process.Start(ProcessStartInfo)` ассоциирует новый `Process` с started resource; метод может вернуть `null`, если resource фактически не стартовал, и может вернуть non-null `Process`, у которого `HasExited == true`, если старт вызвал already-running instance и новый process уже завершился.
- `Process.Start(...)` и `ProcessStartInfo.FileName` считаются security-sensitive API: Microsoft прямо помечает вызов с untrusted data как risk и требует trusted input only.
- `ProcessStartInfo.UseShellExecute = false` означает direct process creation from executable file, а не shell-open behavior. Для .NET Core default уже `false`; это совпадает с нужной semantics для `windows.launch_process`.
- При `UseShellExecute = false` `FileName` допускает только executable semantics; document/file association и file-verb behavior относятся к shell-open path и должны остаться в будущем `windows.open_target`.
- `ArgumentList` и `Arguments` взаимоисключающие; `ArgumentList` автоматически экранирует элементы и therefore предпочтителен для V1 contract с `args: string[]`.
- `WorkingDirectory` при `UseShellExecute = false` не участвует в resolution executable и имеет смысл только как working directory уже запущенного процесса.
- `Environment` можно модифицировать только при `UseShellExecute = false`; иначе `Start()` бросит `InvalidOperationException`. Это делает env-overrides возможными технически, но не обязательными для V1 и дорогими по security/redaction surface.
- `HasExited` и `ExitCode` доступны только для local process handle и могут бросать при invalid/no-process path; они пригодны как post-launch observation, но не как единственная success-definition.

### GUI/window observation

- `MainWindowHandle` даёт handle main window только для local process с graphical interface; значение `0` допустимо для non-GUI, hidden и tray-like cases.
- Microsoft прямо требует `Refresh()` перед чтением `MainWindowHandle`, чтобы не читать stale cached value.
- `WaitForInputIdle(...)` применим только к процессам с GUI и message loop; для non-GUI он бросает `InvalidOperationException`, а при early exit тоже может бросать. Это хороший optional post-launch readiness hint, но не глобальная гарантия.

### UAC / integrity / focus

- Application manifests задают `requestedExecutionLevel` через `asInvoker`, `highestAvailable`, `requireAdministrator`; `highestAvailable` и `requireAdministrator` могут требовать credentials/elevation prompt.
- `uiAccess=true` существует только как manifest-level capability для accessibility scenarios и отдельно ограничивается policy/security requirements; handler не может обещать elevated interaction path без target-specific manifest facts.
- `SetForegroundWindow` не гарантирует forced foreground success даже при выполнении documented conditions; Windows прямо может отказать и вместо этого только flash taskbar button.

### MCP tool contract

- blocked / confirmation / dry-run / business failures должны идти как tool execution result с `isError: true`, а не как protocol-level JSON-RPC error;
- `structuredContent` должен быть canonical source of truth, а serialized JSON должен дублироваться в `TextContentBlock`;
- clients SHOULD держать human-in-the-loop и confirmation prompt для sensitive operations, что совпадает с existing `needs_confirmation` path в shared gate.

### Official references

- [Process.Start](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start?view=net-10.0)
- [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0)
- [ProcessStartInfo.FileName](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.filename?view=net-10.0)
- [ProcessStartInfo.ArgumentList](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0)
- [ProcessStartInfo.WorkingDirectory](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.workingdirectory?view=net-10.0)
- [ProcessStartInfo.Environment](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment?view=net-10.0)
- [Process.MainWindowHandle](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.mainwindowhandle?view=net-10.0)
- [Process.WaitForInputIdle](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforinputidle?view=net-10.0)
- [Application manifests](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [MCP tools spec](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)

## 5. Public contract proposal

### Tool positioning

- public tool name: `windows.launch_process`;
- capability: `windows.launch`;
- MCP type: action tool with `UseStructuredContent = true`;
- execution policy: existing `launch` preset from `ToolContractManifest.FutureLaunchFamilyPolicyPresets`;
- safety stance: shared gate decides `allowed / blocked / needs_confirmation / dry_run_only`; handler materializes result payload and runtime evidence only after that decision.

### Success semantics

V1 success нельзя определять как “окно гарантированно появилось и получило foreground”. Это противоречит official docs и уже принятой capability policy.

Предлагаемая success model:

- базовый live success = `Process.Start(...)` succeeded, returned associated local `Process`, а runtime успел зафиксировать stable `processId`;
- optional stronger success = если caller запросил `waitForWindow=true`, runtime дополнительно observed non-zero `MainWindowHandle` в пределах timeout;
- foreground/focus/attach не входят в success definition и остаются отдельными явными шагами.

Отсюда предлагается разделять:

- top-level `status`:
  - `done`
  - `failed`
  - `blocked`
  - `needs_confirmation`
  - `dry_run_only`
- success `resultMode`:
  - `process_started`
  - `process_started_and_exited`
  - `window_observed`

Interpretation:

- `done + process_started` = process launch succeeded, PID есть, GUI window не требовался;
- `done + process_started_and_exited` = runtime честно зафиксировал successful start + quick exit; это не скрывается и не маскируется как window-ready success;
- `done + window_observed` = start succeeded и optional GUI post-check дал observed main window;
- `failed` используется для request/runtime failures tool semantics, а не для shared gate decisions.

### Minimal V1 request surface

V1 request предлагается держать минимальным и executable-centric:

- `executable: string` — обязательно; explicit executable path или executable name для PATH resolution;
- `args: string[] = []` — optional, только через `ArgumentList`;
- `workingDirectory: string? = null` — optional absolute path, без участия в executable resolution;
- `waitForWindow: bool = false` — optional GUI post-launch check;
- `timeoutMs: int? = null` — optional override только при `waitForWindow=true`; если не передан, runtime использует semantic default `5000`;
- `dryRun: bool = false` — routed into `ToolExecutionIntent.IsDryRunRequested`;
- `confirm: bool = false` — routed into `ToolExecutionIntent.ConfirmationGranted`.

Что намеренно не входит в V1 request surface:

- `environment`;
- `attachAfterLaunch`;
- shell-open/document/URI target fields;
- `verb`, `runAs`, alternate credentials, `createNoWindow`;
- process tree control или stdout/stderr capture options.

### Request validation rules

- `executable` обязателен, canonicalize-ится trim-ом и после canonicalization не может быть пустым;
- допустимы только fully qualified absolute path или bare executable name в direct executable semantics;
- relative subpath и drive-relative path = `invalid_request`;
- URI / URL / rooted directory / document-open or shell-open target = `unsupported_target_kind`;
- boundary использует positive executable policy: explicit extension должна соответствовать direct executable launch (`.exe`, `.com`), а неподдерживаемые file types fail-closed как `unsupported_target_kind`;
- `args` canonical default = empty collection; null-элементы внутри `args` запрещены как `invalid_request`;
- `workingDirectory` canonicalize-ится trim-ом и должен быть absolute path, если передан;
- `timeoutMs` допустим только при явном `waitForWindow=true`;
- `environment` в V1 не принимается вовсе; request boundary должен сохранить schema drift и вернуть `unsupported_environment_overrides`, не теряя поле на transport bind;
- любые прочие extra fields вне frozen request surface reject-ятся fail-closed как `invalid_request`, а не игнорируются молча;
- handler не пытается делать PE-parser или shell association inference: явные executable-vs-document hard rejections должны закрывать only obvious non-executable targets.

## 6. Request/Result DTO proposal

### New contracts

Предпочтительная посадка в `src/WinBridge.Runtime.Contracts/`:

- `LaunchProcessRequest.cs`
- `LaunchProcessRequestValidator.cs`
- `LaunchProcessDefaults.cs`
- `LaunchProcessResult.cs`
- `LaunchProcessPreview.cs`
- `LaunchProcessFailureCodeValues.cs`
- `LaunchProcessResultModeValues.cs`
- `LaunchMainWindowObservationStatusValues.cs`

### Proposed request DTO

```csharp
public sealed record LaunchProcessRequest
{
    public string Executable { get; init; } = string.Empty;
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public bool WaitForWindow { get; init; }
    public int? TimeoutMs { get; init; }
    public bool DryRun { get; init; }
    public bool Confirm { get; init; }
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
```

Design notes:

- `Executable` и `WorkingDirectory` canonicalize-ятся на request boundary, чтобы validator и runtime видели один и тот же normalized input;
- `Args` идут только как list-of-strings с canonical empty default, чтобы runtime всегда строил `ArgumentList`, а не raw `Arguments`;
- `TimeoutMs` семантически относится к optional window observation, а presence поля различим на contract boundary;
- `AdditionalProperties` нужны только для controlled schema drift capture; validator дальше fail-closed отклоняет любой unsupported extra key, а `environment` получает специальный failure code;
- `DryRun` и `Confirm` остаются transport-visible flags, но gate logic не переезжает в handler.

### Proposed result DTO

```csharp
public sealed record LaunchProcessResult(
    string Status,
    string Decision,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    string? ExecutableIdentity = null,
    int? ProcessId = null,
    DateTimeOffset? StartedAtUtc = null,
    bool? HasExited = null,
    int? ExitCode = null,
    bool MainWindowObserved = false,
    long? MainWindowHandle = null,
    string? MainWindowObservationStatus = null,
    string? ArtifactPath = null,
    LaunchProcessPreview? Preview = null,
    string? RiskLevel = null,
    string? GuardCapability = null,
    bool RequiresConfirmation = false,
    bool DryRunSupported = false,
    IReadOnlyList<GuardReason>? Reasons = null);
```

### Preview DTO

```csharp
public sealed record LaunchProcessPreview(
    string ExecutableIdentity,
    string ResolutionMode,
    int ArgumentCount,
    bool WorkingDirectoryProvided,
    bool WaitForWindow,
    int? TimeoutMs);
```

`PreviewAvailable=true` допускается только когда:

- request schema и V1 validation passed;
- runtime смог построить safe preview без `Process.Start(...)`;
- preview не требует raw args/env/working-directory disclosure.

Честный V1 preview показывает только:

- executable identity в безопасном виде, а не raw full path;
- path resolution mode (`absolute_path` / `path_lookup`);
- count аргументов;
- факт наличия working directory;
- будет ли requested GUI window observation.

### Failure code set

Tool-level failure codes нужны только для semantics этого slice и не заменяют shared guard reasons:

- `invalid_request`
- `unsupported_target_kind`
- `unsupported_environment_overrides`
- `executable_not_found`
- `working_directory_not_found`
- `start_failed`
- `process_object_unavailable`
- `process_exited_before_window`
- `main_window_timeout`
- `main_window_not_observed`
- `main_window_observation_not_supported`

Rule:

- `blocked / needs_confirmation / dry_run_only` используют existing `GuardReason` list и existing gate reason codes;
- `failed + failureCode` используется только после того, как invocation уже вошёл в allowed handler path.

## 7. Integration points by file

| Файл | Роль в slice | План |
| --- | --- | --- |
| `src/WinBridge.Runtime.Tooling/ToolNames.cs` | public tool name source of truth | В `Package A` добавить `WindowsLaunchProcess`; `windows.open_target` пока не публиковать. |
| `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs` | public wording | В `Package A` добавить canonical tool summary и parameter descriptions для `executable`, `args`, `workingDirectory`, `waitForWindow`, `timeoutMs`, `dryRun`, `confirm`. |
| `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` | manifest/export truth | В `Package A` зафиксировать internal-only `FutureLaunchProcessDescriptor` с final metadata и existing launch `ExecutionPolicy`, но не переводить `windows.launch_process` в `All`/`Implemented`/`Deferred`, пока нет public handler boundary. `windows.open_target` оставить только future policy preset. |
| `src/WinBridge.Runtime.Tooling/ToolExecutionPolicyDescriptor.cs` | shared policy types | Reuse as-is; новых policy enums для V1 не требуется. |
| `src/WinBridge.Runtime.Tooling/ToolDescriptor.cs` | tool metadata | Reuse existing shape; internal frozen descriptor для `windows.launch_process` должен нести final `SmokeRequired = true` и execution policy, не попадая в shipped export раньше времени. |
| `src/WinBridge.Runtime.Tooling/ContractToolDescriptorFactory.cs` | `okno.contract` export | Structural change не нужен; в `Package A` export path остаётся без publication change и tests фиксируют, что `windows.launch_process` ещё не попадает в agent-visible surface. |
| `src/WinBridge.Runtime.Contracts/ContractToolDescriptor.cs` | machine-readable contract | Structural change не нужен. |
| `src/WinBridge.Runtime.Guards/RuntimeGuardPolicy.cs` | shared launch readiness | Reuse `BuildLaunch(...)` as-is; no handler-specific branch, no new launch-only mini-policy. |
| `src/WinBridge.Runtime.Guards/ToolExecutionGate.cs` | decision matrix | Reuse as-is; handler только передаёт корректный `ToolExecutionIntent` и `PreviewAvailable`. |
| `src/WinBridge.Runtime.Guards/RuntimeGuardService.cs` | authoritative snapshot source | Reuse as-is. |
| `src/WinBridge.Runtime.Diagnostics/ToolExecution.cs` | mandatory gated boundary | Использовать только `RunGated(...)` / `RunGatedAsync(...)`; raw path запрещён. |
| `src/WinBridge.Runtime.Diagnostics/AuditLog.cs` | audit + decision markers | Добавить `launch.runtime.completed` runtime event-data shape и summary line; existing decision/redaction markers reuse-ятся. |
| `src/WinBridge.Runtime.Diagnostics/AuditToolContext.cs` | redaction class resolution | Existing policy resolution already covers `windows.launch_process`; ожидается reuse as-is. |
| `src/WinBridge.Runtime.Diagnostics/AuditPayloadRedactor.cs` | launch redaction | Existing `launch_payload` rules already подходят; возможны только узкие дополнения под новые event fields, без новой redaction class. |
| `src/WinBridge.Runtime.Windows.UIA/UiAutomationWorkerProcessRunner.cs` | existing launch precedent | Использовать как reference по `ProcessStartInfo`, timeout/kill/failure staging; не переносить stdout/stderr capture в public launch feature. |
| `src/WinBridge.Runtime/ServiceCollectionExtensions.cs` | DI root | Зарегистрировать новый runtime launch service. |
| `src/WinBridge.Server/Tools/WindowTools.cs` | public MCP boundary | `Package A` не трогает. `LaunchProcess(...)` handler и фактическая publication в `tools/list` идут только вместе со следующим boundary package. |
| `src/WinBridge.Server/Tools/AdminTools.cs` | health/contract projection | Direct code change не нужен; `Package A` добавляет только tests, которые фиксируют отсутствие premature publication в `okno.contract` / `okno.health`. |
| `src/WinBridge.Server/Program.cs` | tool registration | Если tool останется в `WindowTools`, отдельного host change не нужно. |
| `tests/WinBridge.Server.IntegrationTests/ToolExecutionGateBoundaryTests.cs` | synthetic gated boundary | Добавить launch-specific rejected/preview/confirmation coverage на final public payload shape. |
| `tests/WinBridge.Runtime.Tests/AuditPayloadRedactorTests.cs` | launch redaction proof | Расширить только под новые `preview` / `launch.runtime.completed` fields, если понадобится. |
| `tests/WinBridge.Runtime.Tests/ToolExecutionTests.cs` | mandatory gate + redacted failure path | Добавить launch handler-oriented coverage на allowed live path, dry-run path и sanitized exception/event data. |
| `tests/WinBridge.Server.IntegrationTests/AdminToolTests.cs` | `okno.contract` / `okno.health` surface | В `Package A` обновить ожидания наоборот: `windows.launch_process` остаётся вне implemented/deferred surface до handler boundary, при этом `launch` readiness semantics не меняется. |
| `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs` | real stdio smoke helpers | Использовать existing helper process/window wait patterns как reference для launch smoke assertions. |
| `tests/WinBridge.SmokeWindowHost/Program.cs` | deterministic launched GUI target | Reuse current helper; existing `--title` flag already подходит для launch smoke без focus dependency. |
| `scripts/smoke.ps1` | L3 proof | Добавить separate `windows.launch_process` smoke story и cleanup helper launched через сам tool, не через focus path. |
| `docs/architecture/observability.md` | launch evidence contract | Описать `launch.runtime.completed` и `artifacts/diagnostics/<run_id>/launch/<launch_id>.json`. |
| `docs/generated/project-interfaces.md` | agent-facing current surface | Обновить implemented tools table и artifact interface list. |
| `docs/generated/commands.md` | generated docs | Синхронизировать после `refresh-generated-docs.ps1`, если export surface меняется. |
| `docs/product/okno-roadmap.md` | delivery truth | Обновить progress row 13 по факту shipped contract/tests/smoke. |
| `docs/product/okno-spec.md` | source of truth for user-visible contract | Добавить final request/result semantics, если DTO surface stabilizes beyond this plan. |
| `docs/CHANGELOG.md` | decision log | В `Package A` добавить запись о contract freeze без public publication; shipped launch slice логировать только после handler/runtime rollout. |

### New runtime seam

Предпочтительная новая посадка:

- `src/WinBridge.Runtime.Windows.Launch/`
  - `IProcessLaunchService.cs`
  - `ProcessLaunchService.cs`
  - `LaunchProcessArtifactWriter.cs`
  - `LaunchWindowObservation.cs`

Это даёт отдельный runtime seam для `launch_process`, не смешанный с shell-open и без зашивания semantics в `WindowTools`.

## 8. Delivery packages

### Package A: Contract freeze

Scope:

- зафиксировать request/result DTO;
- выбрать final status/resultMode/failureCode literals;
- зафиксировать `ToolNames`/`ToolDescriptions`/`ToolContractManifest` plan surface как internal-only source of truth;
- решить, что входит в V1 request surface, а что остаётся явно out-of-scope.

Done when:

- другой инженер может открыть contracts + internal launch descriptor и реализовать runtime/service/handler без повторного design fork по `args`, `dryRun`, `confirm`, `waitForWindow`, `environment`;
- `windows.launch_process` ещё не появляется в `ToolContractManifest.All`, `okno.contract`, `okno.health` или `tools/list`, пока нет handler boundary и therefore нет риска соврать о shipped surface.

### Package B: Runtime launch service

Scope:

- ввести отдельный launch service seam;
- реализовать request validation;
- строить `ProcessStartInfo` только с `UseShellExecute = false`;
- использовать `ArgumentList`, а не raw `Arguments`;
- вернуть `processId`, `startedAtUtc`, `hasExited`, optional `exitCode`;
- optional `waitForWindow=true` реализовать через `WaitForInputIdle` + `Refresh` + `MainWindowHandle` polling.

Done when:

- runtime service честно различает `process_started`, `process_started_and_exited`, `window_observed` и tool failures без focus/attach side effects.

Текущее состояние на `2026-04-01`:

- отдельный runtime seam `WinBridge.Runtime.Windows.Launch` добавлен без handler/publication rollout;
- direct `ProcessStartInfo` path реализован только через `UseShellExecute = false` + `ArgumentList`;
- optional `waitForWindow` materialized как runtime-only observation step через `WaitForInputIdle` + `Refresh` + `MainWindowHandle`;
- follow-up hardening внутри того же `Package B` убрал lossy `File.Exists` / `Directory.Exists` preflight, перенёс классификацию ошибок на authoritative start-failure mapping, сделал `WaitForInputIdle` bounded/cancel-aware через короткие slices и provider-aware poll delay на одном `TimeProvider`, выровнял process snapshot (`HasExited` / `ExitCode`) между `process_started`, `process_started_and_exited` и `window_observed`, а `InvalidOperationException` от `WaitForInputIdle` перестал бездоказательно materialize-иться как `not_supported`: runtime теперь консервативно продолжает main-window observation и выбирает `process_exited_before_window`, `main_window_timeout` или `main_window_not_observed` по реально observed snapshot. Дополнительно `input-idle timeout` больше не считается terminal доказательством отсутствия main window: если окно уже materialized в пределах общего бюджета, `waitForWindow` честно возвращает `window_observed`, даже когда message loop не успел перейти в idle. Final deadline verdict теперь тоже берётся из fresh observation snapshot с обязательным `Refresh()` перед чтением `MainWindowHandle`, но stronger success допускается только если этот fresh snapshot снят не позже общего `timeoutMs`; stale cached handle и late-after-deadline window больше не дают ложный success. Для started-result веток runtime теперь тоже делает fresh result snapshot без legacy cached-state assumptions, включая cancel-path после старта, а observation verdict не может отдать `window_observed` поверх уже exited snapshot. Ambiguous `ERROR_PATH_NOT_FOUND` больше не материализуется как `working_directory_not_found` по одной только комбинации bare executable + `workingDirectory`: для таких path-ambiguity cases runtime теперь честно остаётся на `start_failed`, а `working_directory_not_found` сохраняется только для более сильных сигналов (`DirectoryNotFoundException`, `ERROR_DIRECTORY` / `267`);
- DI root в `WinBridge.Runtime` уже резолвит `IProcessLaunchService`, а shipped MCP surface по-прежнему не публикует `windows.launch_process`.

### Package C: Gated MCP boundary

Scope:

- добавить `windows.launch_process` handler в `WindowTools`;
- одновременно перевести `windows.launch_process` из internal contract freeze в agent-visible implemented surface через `ToolContractManifest.All` / `okno.contract` / `tools/list`;
- route request into `ToolExecutionIntent`;
- blocked/confirmation/dry-run payload materialize-ить без side effects;
- allowed path вызывать только через new runtime launch service;
- `attach` и `focus` не выполнять автоматически.

Done when:

- server boundary проходит synthetic gate tests, `tools/list`/`okno.contract`/manifest больше не расходятся между собой и не содержат собственного policy branching beyond request validation + result mapping.

### Package D: Evidence and redaction

Scope:

- materialize-ить `launch.runtime.completed`;
- писать `artifacts/diagnostics/<run_id>/launch/<launch_id>.json` для allowed dry-run/live execution paths;
- использовать existing `launch_payload` redaction class для request/event/error path;
- summary.md должен ссылаться только на safe executable identity, `processId`, `resultMode`, `artifactPath`.

Done when:

- raw args/full path/working directory/env values не попадают ни в `events.jsonl`, ни в `summary.md`, ни в artifact safe summary.

### Package E: Tests + smoke + docs sync

Scope:

- L1/L2 tests на validator, gate mapping, runtime launch service, redaction, contract export;
- L3 smoke c launched helper window без focus dependency;
- `refresh-generated-docs.ps1` и docs sync.

Done when:

- `windows.launch_process` появляется в shipped surface вместе с tests, smoke evidence и docs sync, а roadmap row 13 обновлён по факту.

### Implementation checklist

- [x] Добавить `WindowsLaunchProcess` в `ToolNames`.
- [x] Добавить canonical description и parameter descriptions в `ToolDescriptions`.
- [x] Зафиксировать internal-only `FutureLaunchProcessDescriptor` и policy freeze без публикации `windows.launch_process` в `ToolContractManifest.All` / export surface.
- [x] Добавить launch request/result contracts и validator в `WinBridge.Runtime.Contracts`.
- [x] Завести отдельный runtime launch service seam и DI registration.
- [x] Строить `ProcessStartInfo` только через `UseShellExecute = false`.
- [x] Использовать `ArgumentList`, а не raw `Arguments`.
- [x] Не принимать `environment`, `verb`, alternate credentials и shell-open targets в V1.
- [x] Реализовать optional GUI post-check через `WaitForInputIdle` + `Refresh` + `MainWindowHandle`.
- [x] Сделать `window_observed` optional success mode, а не universal success requirement.
- [ ] Добавить dedicated `launch.runtime.completed` event и `launch/<launch_id>.json` artifact.
- [ ] Не вводить новую redaction class; reuse `launch_payload`.
- [x] Не вводить auto-attach или auto-focus.
- [x] Добавить L1 tests для request validation и launch result modes.
- [ ] Добавить L1 tests для event-data redaction и fail-safe suppression.
- [ ] Добавить L2 synthetic boundary tests на `blocked / needs_confirmation / dry_run_only / allowed`.
- [x] Обновить `AdminToolTests` и contract export expectations.
- [x] Зафиксировать в active exec-plan и `CHANGELOG`, что public publication откладывается до handler boundary ради honesty current surface.
- [ ] Добавить L3 smoke на helper launch через сам tool.
- [ ] После implementation прогнать `scripts/build.ps1`, `scripts/test.ps1`, `scripts/smoke.ps1`, `scripts/refresh-generated-docs.ps1`, `scripts/codex/verify.ps1` строго последовательно.
- [ ] Синхронизировать roadmap/spec/observability/generated docs/changelog в том же цикле.

## 9. Test ladder L1/L2/L3

### L1

- `LaunchProcessRequestValidatorTests`
  - empty executable;
  - URI / URL target;
  - rooted directory path;
  - drive-relative path вроде `C:demo.exe`;
  - obvious document-open target вроде `.txt` / `.url`;
  - null element inside `args`;
  - `timeoutMs` without `waitForWindow`;
  - env override rejection if schema drift appears.
- `ProcessLaunchServiceTests`
  - `UseShellExecute = false`;
  - `ArgumentList` population and no `Arguments` fallback;
  - `WorkingDirectory` set independently from executable resolution;
  - no `Verb`, no alternate credentials;
  - `process_started` vs `process_started_and_exited`;
  - `waitForWindow=true` happy path / timeout / early exit / non-GUI invalid idle path.
- `AuditPayloadRedactorTests`
  - request preview redaction;
  - runtime event data redaction;
  - full path basename normalization;
  - fail-safe suppression.
- `ToolExecutionTests`
  - blocked/confirmation/dry-run marker propagation;
  - redacted exception path for `windows.launch_process`.

### L2

- synthetic gated boundary on final public `LaunchProcessResult` shape;
- integration test that `okno.contract` now lists `windows.launch_process` in implemented tools with `execution_policy`;
- integration test that `okno.health` launch readiness remains unchanged (`ready/degraded/blocked/unknown`) after publication of public tool;
- server-boundary test that rejected gate decision does not call runtime launch service;
- server-boundary test that allowed live path returns `structuredContent` + one text block and `isError=false`.

### L3

Strict sequential verification loop for actual implementation:

1. `scripts/build.ps1`
2. `scripts/test.ps1`
3. `scripts/smoke.ps1`
4. `scripts/refresh-generated-docs.ps1`
5. `scripts/codex/verify.ps1`

No parallel run in the same worktree, because these commands share build/generated artifacts.

## 10. Smoke strategy

### Smoke story

Use current helper `tests/WinBridge.SmokeWindowHost/Program.cs` as explicit launched target:

- executable: built `WinBridge.SmokeWindowHost.exe`;
- args: `["--title", "Okno Launch Smoke <run_id>"]`;
- request: `waitForWindow=true`, `timeoutMs=10000`, `confirm=true`, `dryRun=false`.

### Assertions

- `result.isError == false`;
- `structuredContent.status == "done"`;
- `structuredContent.decision == "allowed"`;
- `structuredContent.resultMode == "window_observed"`;
- `processId > 0`;
- `mainWindowObserved == true`;
- `mainWindowHandle != 0`;
- `artifactPath` exists.

### Cross-check without focus dependence

Smoke не должен зависеть от `activate_window`, `focus_window` или `focus_is`:

- либо нативно проверить launched helper process по `processId` и `MainWindowHandle`;
- либо через `windows.list_windows` дождаться matching `hwnd` / title, не требуя foreground;
- cleanup helper делать отдельным teardown path, а не частью tool semantics.

### Dry-run smoke

Отдельно добавить lightweight dry-run assertion:

- `dryRun=true`, `confirm=false`;
- expected `status == "done"` или separate preview-only success within `decision == "allowed"` in dry-run mode;
- preview содержит only safe executable identity, argument count and flags, без raw args/path disclosure.

## 11. Docs sync

При actual implementation этого slice синхронизировать в том же цикле:

- `docs/architecture/observability.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/commands.md`
- `docs/product/okno-roadmap.md`
- `docs/product/okno-spec.md`
- `docs/CHANGELOG.md`

Conditional docs:

- `docs/product/okno-vision.md` — только если хочется поднять status `windows.launch_process` из future list в shipped language;
- `docs/architecture/openai-computer-use-interop.md` — только если понадобятся clarifying words about split launch/open_target after publication;
- `docs/bootstrap/bootstrap-status.json` — только если `refresh-generated-docs.ps1` действительно поменяет generated bootstrap state.

Жёсткие rules:

- не отмечать row 13 как completed/implemented без tests + smoke + docs sync;
- не менять wording `windows.open_target` в том же PR beyond keeping the split explicit;
- observability doc должна явно фиксировать, что launch artifact/event добавлены, а health artifact policy остаётся прежней.

## 12. Rollback / risk notes

- `PATH`-resolved executable names делают preview менее детерминированным, чем absolute path; smoke и docs должны рекомендовать absolute path для доказуемых сценариев.
- positive executable policy для absolute path intentionally fail-closed: если file extension не выглядит как direct executable, request отклоняется на boundary, а не оставляется на будущее runtime guesswork.
- `waitForWindow=true` применим не ко всем launched processes; non-GUI/hidden/tray paths не должны автоматически считаться “broken launch”. Поэтому GUI observation остаётся optional mode, а не global success definition.
- `process_started_and_exited` нельзя silently downcast-ить в failure: quick exit иногда валиден. Failure начинается только там, где caller explicitly requested stronger post-launch contract (`waitForWindow=true`) или where `Process.Start(...)` itself failed.
- medium-integrity live launch остаётся confirmation-worthy даже после публикации tool: target-specific manifest facts, `requestedExecutionLevel` и `uiAccess` заранее неизвестны.
- `Verb`, `runas`, alternate credentials и env overrides являются сильным признаком scope creep в `windows.open_target` / separate elevation track и не должны попадать в V1 без нового exec-plan.
- `SetForegroundWindow` limitations означают, что auto-focus success нельзя обещать ни в result, ни в smoke acceptance; post-launch focus/activate остаются отдельными tools.
- rollback path должен быть additive: если launch runtime seam или artifact contract окажутся нестабильными, сначала откатывается public `windows.launch_process` publication и related docs surface, а shared gate/readiness/redaction foundation не трогается.
