# ExecPlan: windows.open_target

Статус: completed
Архивирован: 2026-04-09
Создан: 2026-04-08
Актуально на: 2026-04-09

## 1. Goal

Спроектировать и подготовить shipped public slice `windows.open_target` как shell-open tool для файла / URL / document target поверх уже shipped launch/safety foundation.

Жёсткая формулировка цели:

- опубликовать отдельный tool именно для shell-open semantics, а не для direct process launch;
- не смешивать его с уже shipped [`windows.launch_process`](../completed/completed-2026-04-08-windows-launch-process.md);
- переиспользовать уже существующие shared gate, launch readiness и `launch_payload` redaction;
- сохранить ближайший delivery order `windows.open_target -> windows.input(click first)`;
- оставить attach/focus/window-activation отдельными явно проверяемыми шагами, а не скрытыми post-open эффектами.

## 2. Non-goals

- не реализовывать `windows.input`;
- не менять semantics `windows.launch_process`, кроме общего reuse launch-domain foundation;
- не вводить новую mini-policy или отдельную reason taxonomy внутри handler-а;
- не делать `runas`, alternate credentials, elevation choreography или target-specific UAC automation;
- не тащить OpenAI-specific adapter logic в `WinBridge.Runtime` или `WinBridge.Server`;
- не обещать PID/process identity там, где shell-open по платформе этого не гарантирует;
- не добавлять public `verb`, `workingDirectory`, `environment`, `timeoutMs`, `waitForWindow` в V1 request surface;
- не строить smoke вокруг assumptions вида “откроется именно Notepad/Chrome/Outlook”.

## 3. Current repo state

По состоянию на `2026-04-08` slice нельзя планировать как greenfield:

- roadmap уже ставит `windows.open_target` следующим planned slice после shipped `windows.launch_process` и до `windows.input`;
- product spec уже закрепил split `windows.launch_process` / `windows.open_target`;
- completed safety baseline уже ответил `да` на вопрос, можно ли строить следующий launch-family tool без новой safety-логики внутри handler-а;
- `RuntimeGuardPolicy.BuildLaunch(...)` уже materialize-ит reusable launch-readiness summary и различает `ready / degraded / blocked / unknown`;
- medium-integrity live path уже выражается не hard block-ом, а `launch=degraded` + warning `launch_elevation_boundary_unconfirmed`;
- raw execution для policy-bearing tools fail-fast запрещён: будущий handler обязан входить только через `ToolExecution.RunGated(...)` / `RunGatedAsync(...)`;
- `ToolContractManifest` уже хранит internal-only future policy preset для `windows.open_target` с `policy_group=launch`, `risk_level=medium`, `supports_dry_run=true`, `confirmation_mode=required`, `redaction_class=launch_payload`;
- `launch_payload` redaction class уже покрывает request/event-data path для executable/path/url-like payloads и является правильной базой для open-target redaction;
- shipped `windows.launch_process` уже задал sibling-pattern по contract freeze, runtime seam, manual MCP registration, runtime artifact/event materialization, smoke и docs sync;
- observability contract уже умеет жить с launch-family evidence в `artifacts/diagnostics/<run_id>/launch/` и best-effort runtime events без semantic downcast при audit/artifact failures;
- [`docs/architecture/openai-computer-use-interop.md`](../architecture/openai-computer-use-interop.md) уже фиксирует, что split `windows.launch_process` / `windows.open_target` сохраняется и не должен ломаться ради будущего adapter-слоя.

Следствие:

- новый exec-plan должен описывать не новый guardrail stack, а узкий shell-open contract, runtime seam, evidence contract, smoke story и docs rollout;
- foundation слои `launch readiness -> ToolExecutionGate -> RunGated -> AuditLog redaction` считаются готовыми и не перепридумываются.

## 4. Official constraints

### 4.1. Граница между direct launch и shell-open

- В .NET `ProcessStartInfo.FileName` при `UseShellExecute = true` может быть executable, document file или URL, тогда как при `UseShellExecute = false` `FileName` должен быть executable. Это и есть formal boundary между `windows.launch_process` и будущим `windows.open_target`.
- `Process.Start(ProcessStartInfo)` может вернуть `null`, если никакой process resource не стартовал. Для shell-open это принципиально важно: успешный open request не обязан давать новый `Process`.
- Win32 `ShellExecuteW` прямо работает через verb + shell association model: `lpFile` может быть executable, document file, folder или URL; `lpOperation = NULL` означает default verb, а при отсутствии default shell пытается `open`.

### 4.2. Shell-open не обязан давать process handle

- `SHELLEXECUTEINFOW` с `SEE_MASK_NOCLOSEPROCESS` даёт `hProcess` только при некоторых сценариях; официальный Win32 текст прямо говорит, что в ряде случаев `hProcess` может быть `NULL`, например при URL, который открылся в уже запущенном browser process.
- Из этого следует узкое design-решение: V1 не должен строить success semantics вокруг обязательного `processId`, `startedAtUtc`, `hasExited` или window handle.
- Честный factual success для V1 = shell принял request и не вернул association/path-level failure. Optional process observation допустим только как enrichment.

### 4.3. `WorkingDirectory` и `Verb`

- Официальный .NET текст для `ProcessStartInfo.WorkingDirectory` при `UseShellExecute = true` описывает directory прежде всего как location executable-а; Win32 `lpDirectory` описывается как default/current directory для operation. Для document/URL shell-open semantics это не даёт достаточно честного и полезного public contract.
- Поэтому `workingDirectory` в `windows.open_target` V1 нужно исключить, а не переносить из `launch_process`.
- `ProcessStartInfo.Verb` и Win32 `lpOperation` существуют, но добавление `print`, `edit`, `openas`, `runas` сразу превращает slice в более широкий verb-policy/elevation problem. Поэтому V1 фиксирует только default open action и не публикует public `verb`.

### 4.4. Error/result semantics в MCP

- MCP tools spec допускает business/tool failures через normal tool result с `isError: true`, а не через transport-level JSON-RPC error.
- Это совпадает с уже shipped boundary-паттерном `blocked / needs_confirmation / dry_run_only / failed` для `windows.launch_process` и должно быть сохранено для `windows.open_target`.

### 4.5. Official references

- [Process.Start](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start?view=net-10.0)
- [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0)
- [ProcessStartInfo.FileName](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.filename?view=net-10.0)
- [ProcessStartInfo.WorkingDirectory](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.workingdirectory?view=net-10.0)
- [ProcessStartInfo.Verb](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.verb?view=net-10.0)
- [ShellExecuteW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecutew)
- [SHELLEXECUTEINFOW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow)
- [MCP tools spec](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)

## 5. Public contract proposal

### 5.1. Tool positioning

- public tool name: `windows.open_target`;
- capability: `windows.launch`;
- MCP type: action tool with `UseStructuredContent = true`;
- safety class: `os_side_effect`;
- execution policy: existing launch-family preset from `ToolContractManifest.FutureLaunchFamilyPolicyPresets`;
- safety stance: shared gate materialize-ит `allowed / blocked / needs_confirmation / dry_run_only`; handler строит preview или live shell-open semantics только после этого решения.

### 5.2. V1 target surface

V1 intentionally narrow:

- `document` — absolute DOS local или UNC document path, который не выглядит как direct executable / script / launcher target;
- `folder` — absolute DOS local или UNC directory path;
- `url` — absolute `http` / `https` URL.

V1 intentionally out:

- `mailto:`;
- `file://` URI;
- device-style path forms (`\\?\`, `\\.\`, `\\?\UNC\...`);
- custom URI schemes (`ms-settings:`, `slack:`, `zoommtg:` и т.д.);
- direct executable targets (`.exe`, `.com`) и launcher/script-like file types (`.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.msi`, `.lnk`, `.url`, `.scr`, `.reg`, `.msc`, `.hta` и аналогичные);
- public `verb` variants `print`, `edit`, `openas`, `runas`.

### 5.3. Success semantics

V1 success не должен означать “новый process создан” или “окно наблюдено”.

Предлагаемая success model:

- базовый live success = shell принял open request и runtime не получил association/path-level failure;
- stronger optional success = runtime дополнительно observed new handler process id через `ShellExecuteExW + SEE_MASK_NOCLOSEPROCESS`;
- отсутствие new process observation не downcast-ится в failure и не делает result менее честным.

Отсюда frozen result modes:

- `target_open_requested`
- `handler_process_observed`

Interpretation:

- `done + target_open_requested` = shell-open request принят, но новый handler process не наблюдался или платформа не дала handle;
- `done + handler_process_observed` = shell-open request принят и Win32 path дал новый handler process id;
- neither mode ничего не говорит про foreground, attach, active window или tab selection.

### 5.4. Verb, working directory и post-open verification

- V1 поддерживает только default open action и не публикует `verb`;
- `workingDirectory` не входит в public request surface;
- V1 не вводит requested `waitForHandler` / `waitForWindow` path, потому что для shell-open это создаёт ложный implied contract о PID/window identity;
- optional process observation допускается только как passive enrichment результата;
- attach/focus/window verification остаются следующими slices (`windows.list_windows`, `windows.attach_window`, `windows.wait`, будущий `windows.input`).

## 6. Request/Result DTO proposal

### 6.1. New contracts

Предпочтительная посадка в `src/WinBridge.Runtime.Contracts/`:

- `OpenTargetRequest.cs`
- `OpenTargetRequestValidator.cs`
- `OpenTargetKindValues.cs`
- `OpenTargetResult.cs`
- `OpenTargetPreview.cs`
- `OpenTargetResultModeValues.cs`
- `OpenTargetFailureCodeValues.cs`
- `OpenTargetClassifier.cs`

### 6.2. Proposed request DTO

```csharp
public sealed record OpenTargetRequest
{
    public string TargetKind { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public bool Confirm { get; init; }
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
```

Design notes:

- `TargetKind` обязателен и равен одному из literal-set `document | folder | url`;
- `Target` canonicalize-ится trim-ом на boundary;
- explicit `TargetKind` лучше для shell-open, чем неявная эвристика по строке: он убирает неоднозначность extensionless path vs folder и делает smoke/docs contract стабильнее;
- `AdditionalProperties` нужны только для controlled schema drift capture; unsupported fields fail-closed отклоняются validator-ом.

### 6.3. Proposed preview DTO

```csharp
public sealed record OpenTargetPreview(
    string TargetKind,
    string? TargetIdentity = null,
    string? UriScheme = null);
```

Preview intentionally safe:

- для `document` / `folder` `TargetIdentity` = basename only;
- для `url` `TargetIdentity` отсутствует, а `UriScheme` = `http` или `https`;
- raw full path, raw URL, query string, fragment и mail-recipient-like payload не попадают в preview.

### 6.4. Proposed result DTO

```csharp
public sealed record OpenTargetResult(
    string Status,
    string Decision,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    string? TargetKind = null,
    string? TargetIdentity = null,
    string? UriScheme = null,
    DateTimeOffset? AcceptedAtUtc = null,
    int? HandlerProcessId = null,
    string? ArtifactPath = null,
    OpenTargetPreview? Preview = null,
    string? RiskLevel = null,
    string? GuardCapability = null,
    bool RequiresConfirmation = false,
    bool DryRunSupported = false,
    IReadOnlyList<GuardReason>? Reasons = null);
```

### 6.5. Request validation rules

- `targetKind` обязателен и должен быть `document`, `folder` или `url`;
- `target` обязателен и не должен быть пустым после trim;
- `document` / `folder` принимают только absolute DOS local или UNC path; relative path, drive-relative path и device-style path forms = `invalid_request`;
- `document` explicitly reject-ит direct executable / launcher / script-like extensions;
- `url` принимает только absolute `http` / `https`;
- `mailto`, `file`, `ms-settings` и любой другой scheme = `unsupported_uri_scheme`;
- `verb`, `workingDirectory`, `environment`, `waitForWindow`, `timeoutMs`, `attachAfterOpen`, `runAs` и любые иные extra fields fail-closed reject-ятся;
- validator не делает lossy `File.Exists` / `Directory.Exists` preflight как источник истины для live result; authoritative failure mapping остаётся на runtime/platform path.

### 6.6. Failure code set

Public failure codes нужны только для semantics этого slice и не заменяют shared gate reasons:

- `invalid_request`
- `unsupported_target_kind`
- `unsupported_uri_scheme`
- `target_not_found`
- `target_access_denied`
- `no_association`
- `shell_rejected_target`

Rule:

- `blocked / needs_confirmation / dry_run_only` используют existing `GuardReason` list и existing gate reason codes;
- `failed + failureCode` используется только после того, как invocation уже вошёл в allowed handler/runtime path;
- timeout-based handler/window observation codes в V1 не freeze-ятся, потому что requested post-open observation path в этом slice intentionally отсутствует.

## 7. Integration points by file

| Файл | Роль в slice | План |
| --- | --- | --- |
| `src/WinBridge.Runtime.Tooling/ToolNames.cs` | public tool name source of truth | В `Package A` добавить `WindowsOpenTarget`; `windows.open_target` должен стать literal source of truth наравне с `windows.launch_process`. |
| `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs` | public wording | Добавить canonical summary и parameter descriptions только для `targetKind`, `target`, `dryRun`, `confirm`; не переносить `workingDirectory`/`verb` wording из sibling tool. |
| `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs` | manifest/export truth | На `Package A` зафиксировать internal-only `FutureOpenTargetDescriptor`, который reuse-ит existing launch preset и не публикуется в `All`/`Implemented` до handler boundary. После `Package B` перевести descriptor в implemented surface, не ломая sibling `windows.launch_process`. |
| `src/WinBridge.Runtime.Tooling/ToolExecutionPolicyDescriptor.cs` | shared policy types | Reuse as-is; новых policy enums или confirmation modes не требуется. |
| `src/WinBridge.Runtime.Guards/RuntimeGuardPolicy.cs` | shared launch readiness | Reuse `BuildLaunch(...)` as-is; open-target handler не должен вводить собственный mini-gate или другую reason taxonomy. |
| `src/WinBridge.Runtime.Guards/ToolExecutionGate.cs` | decision matrix | Reuse as-is; handler только передаёт `PreviewAvailable=true` после request validation и deterministic preview build. |
| `src/WinBridge.Runtime.Guards/RuntimeGuardService.cs` | authoritative snapshot source | Reuse as-is; новый shell-open slice не добавляет отдельный probe stack. |
| `src/WinBridge.Runtime.Diagnostics/ToolExecution.cs` | mandatory gated boundary | Использовать только `RunGatedAsync(...)`; raw execution path для `windows.open_target` недопустим. |
| `src/WinBridge.Runtime.Diagnostics/AuditLog.cs` | audit + summary suffix | Добавить `open_target.runtime.completed` и `open_target.preview.completed` handling, safe summary suffix для `target_kind`, `target_identity`/`uri_scheme`, `handler_process_id`, `artifact_path`; не писать raw path/URL. |
| `tests/WinBridge.Runtime.Tests/AuditPayloadRedactorTests.cs` | redaction proof | Расширить launch-family redaction coverage под `target`, `target_identity`, `uri_scheme`, query/fragment suppression и safe runtime event-data для `open_target`. |
| `src/WinBridge.Runtime.Windows.Launch/ProcessLaunchService.cs` | shipped sibling reference | Использовать как sibling-pattern для request validation, runtime/service split и materializer lifecycle, но не копировать DTO/result semantics и не тащить `UseShellExecute=false` assumptions. |
| `src/WinBridge.Runtime.Windows.Launch/SystemProcessLaunchPlatform.cs` | direct-launch precedent | Reference only: для `open_target` этого platform seam недостаточно, потому что он теряет ShellExecute-specific error/result surface; нужен отдельный shell-open platform. |
| `src/WinBridge.Runtime.Windows.Launch/LaunchResultMaterializer.cs` | artifact/event pattern | Reuse architectural pattern, но ввести отдельный `OpenTargetResultMaterializer` и не смешивать `OpenTargetResult` c `LaunchProcessResult`. |
| `src/WinBridge.Runtime.Windows.Launch/LaunchArtifactWriter.cs` | evidence family precedent | Reuse naming/location strategy для launch-family artifacts, но с отдельным payload type и filename prefix `open-target-...json`. |
| `src/WinBridge.Server/Tools/WindowsLaunchProcessToolRegistration.cs` | manual MCP registration pattern | Использовать как template для `WindowsOpenTargetToolRegistration.cs`: explicit flat `inputSchema`, programmatic registration, `UseStructuredContent=true`. |
| `src/WinBridge.Server/Tools/WindowTools.cs` | public MCP boundary | Добавить `OpenTarget(...)` handler с raw arguments binding, validator, shared gate, dry-run preview, live call в новый service и canonical payload mapping. |
| `tests/WinBridge.Server.IntegrationTests/WindowLaunchProcessToolTests.cs` | sibling boundary proof | Использовать как template для `WindowOpenTargetToolTests.cs`: blocked/needs_confirmation/dry_run/live-path coverage, audit/event best-effort behavior, invalid transport binding. |
| `tests/WinBridge.Runtime.Tests/LaunchProcessContractFreezeTests.cs` | contract freeze pattern | Использовать как template для `OpenTargetContractFreezeTests.cs`: literals, defaults, validator, ToolDescriptions freeze и schema drift rejection. |
| `tests/WinBridge.Runtime.Tests/ToolContractManifestTests.cs` | manifest/export sync | Обновить future launch preset expectations на `Package A`, затем implemented/publication expectations на `Package B`. |
| `tests/WinBridge.Server.IntegrationTests/AdminToolTests.cs` | `okno.contract` / `okno.health` sync | На `Package A` зафиксировать отсутствие premature publication; на `Package B` перевести `windows.open_target` в implemented tool list с expected execution policy, не меняя `launch` readiness semantics. |
| `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs` | real stdio smoke helpers | Добавить final `tools/list` / `okno.contract` parity expectations для `windows.open_target`; smoke acceptance не должен требовать browser/editor-specific handler. |

### New runtime seam

Предпочтительная новая посадка в том же проекте `src/WinBridge.Runtime.Windows.Launch/`:

- `IOpenTargetService.cs`
- `OpenTargetService.cs`
- `IOpenTargetPlatform.cs`
- `ShellExecuteOpenTargetPlatform.cs`
- `OpenTargetResultMaterializer.cs`
- `OpenTargetArtifactWriter.cs`
- `OpenTargetArtifactNameBuilder.cs`

Почему именно так:

- `windows.open_target` остаётся в том же launch-domain project, что и `windows.launch_process`;
- DTO/result/runtime semantics остаются отдельными и не смешиваются с `LaunchProcessResult`;
- `ShellExecuteExW`-oriented platform seam нужен отдельно, потому что `.NET Process.Start(ProcessStartInfo { UseShellExecute = true })` не даёт достаточно честного ShellExecute-specific failure/result surface для V1 contract.

## 8. Delivery packages

### Package A: Contract and runtime foundation

Scope:

- зафиксировать request/result/preview DTO и final literal sets для `targetKind`, `status`, `resultMode`, `failureCode`;
- добавить `ToolNames` / `ToolDescriptions` / internal `FutureOpenTargetDescriptor`;
- решить V1 scope по target kinds, `verb`, `workingDirectory`, URI schemes и redaction surface;
- ввести новый `IOpenTargetService` и `IOpenTargetPlatform`;
- реализовать validator-aware live path поверх `ShellExecuteExW` с `SEE_MASK_FLAG_NO_UI | SEE_MASK_NOCLOSEPROCESS`;
- map-ить ShellExecute failure space в public failure codes `target_not_found`, `target_access_denied`, `no_association`, `shell_rejected_target`;
- materialize-ить factual `AcceptedAtUtc` и optional `HandlerProcessId`.

Done when:

- другой инженер может открыть contracts + runtime seam и реализовать MCP boundary без повторного design fork по `mailto`, `custom scheme`, `workingDirectory`, `verb`, success semantics и smoke target choice;
- runtime service честно различает `target_open_requested`, `handler_process_observed` и tool failures без attach/focus/window assumptions;
- live success больше не зависит от обязательного process object;
- `windows.open_target` ещё не появляется в `ToolContractManifest.All`, `okno.contract`, `okno.health` или `tools/list`.

Текущее состояние на `2026-04-08`:

- `Package A` закрыт: internal-only descriptor, contracts, shell-open runtime seam, DI registration и узкие tests уже добавлены без premature publication в public surface.

### Package B: Public boundary and observability rollout

Scope:

- добавить `WindowsOpenTargetToolRegistration`;
- добавить `OpenTarget(...)` handler в `WindowTools`;
- bind-ить request из raw MCP `arguments`, а не через attribute/scalar auto-binding;
- blocked/confirmation/dry-run payload materialize-ить без side effects;
- allowed live path вызывать только через новый service;
- одновременно перевести `windows.open_target` из internal contract freeze в agent-visible implemented surface через `ToolContractManifest.All` / `okno.contract` / `tools/list`;
- materialize-ить `open_target.runtime.completed` только для factual live path;
- использовать `artifacts/diagnostics/<run_id>/launch/open-target-<id>.json` как launch-family evidence directory, но с отдельным payload type;
- добавить internal dry-run proof marker `open_target.preview.completed`;
- reuse existing `launch_payload` redaction class и расширить его под full path / URL / query / fragment / mail-like payload suppression.

Done when:

- server boundary проходит synthetic gate tests;
- invalid request materialize-ится как tool-level `failed`, а не как transport error;
- allowed live failure path публикует `Decision=failed`, даже если shared gate до этого дал `allowed`;
- raw path, raw URL, query string, fragment, custom-scheme payload и future verb-like fields не попадают в `events.jsonl`, `summary.md` или runtime artifact;
- observability write остаётся best-effort и не downcast-ит factual result при `artifact_write` / audit sink failures.

Текущее состояние на `2026-04-08`:

- `Package B` закрыт: public MCP boundary, publication и observability rollout для `windows.open_target` собраны без захода в broad verification contour, smoke и docs sync из `Package C`.

### Package C: Verification, smoke and docs rollout

Scope:

- L1/L2 tests на validator, platform mapping, runtime service, boundary, manifest/export, observability и redaction;
- L3 smoke без browser/editor dependency;
- `refresh-generated-docs.ps1` и product/generated/architecture docs sync.

Done when:

- `windows.open_target` появляется в shipped surface вместе с tests, smoke evidence и docs sync;
- roadmap row 14 переводится из `запланировано` только по факту зелёного sequential contour;
- sequential contour `build -> test -> smoke -> refresh-generated-docs -> verify` пройден без дополнительных blocker follow-up.

Текущее состояние на `2026-04-09`:

- `Package C` закрыт: missing boundary/publication checks добраны, `scripts/smoke.ps1` теперь доказывает `windows.open_target` через deterministic `folder` dry-run/live story без browser/editor assumptions, `windows.open_target` переведён в `SmokeRequired`, а sequential contour `scripts/build.ps1 -> scripts/test.ps1 -> scripts/smoke.ps1 -> scripts/refresh-generated-docs.ps1 -> scripts/codex/verify.ps1` пройден зелёным вместе с docs/generated sync.
- Post-completion review hardening без смены public V1 contract дополнительно зафиксировал шесть внутренних инвариантов branch state: unexpected exceptions после входа в allowed live runtime path теперь materialize-ятся через тот же `OpenTargetResultMaterializer` с `failure_stage` и canonical open-target artifact/event trail; document safety policy вынесена в explicit artifact и fail-closed блокирует не только Python script family `.py/.pyw/.pyc`, но и packaged executable archives `.pyz/.pyzw` и Java `jar`; shell failure normalizer учитывает не только legacy `SE_ERR_NOASSOC`, но и `GetLastError(ERROR_NO_ASSOCIATION)`; dedicated STA executor стал phase-aware и различает `pre-dispatch cancellation` от `cancellation after dispatch`, поэтому отмена больше не маскирует возможный factual shell side effect как чистый `OperationCanceledException`, а после dispatch current single-result model intentionally ждёт factual completion вместо ложного caller-side cancel; live path inspection для `document`/`folder` теперь intentionally остаётся fast local-only refinement и деградирует в `Unresolved` только для UNC/remote network-sensitive paths, сохраняя truthful refinement для local fixed/removable/CD-ROM/ramdisk media; verification control plane запускает CI-like top-level steps в fresh `powershell -NoProfile -NonInteractive` child processes, чтобы process isolation не зависела от user profile contamination.

### Implementation checklist

`Package A`

- [x] Добавить `WindowsOpenTarget` в `ToolNames`.
- [x] Добавить canonical summary и parameter descriptions в `ToolDescriptions`.
- [x] Зафиксировать internal-only `FutureOpenTargetDescriptor` и final execution policy metadata.
- [x] Добавить open-target request/result/preview contracts и validator в `WinBridge.Runtime.Contracts`.
- [x] Завести отдельный runtime shell-open service seam в `WinBridge.Runtime.Windows.Launch`.
- [x] Реализовать Win32 shell-open platform поверх `ShellExecuteExW`, а не поверх прямого `Process.Start(...)`.
- [x] Не публиковать `workingDirectory`, `verb`, `waitForWindow`, `timeoutMs`, `environment`.
- [x] Зафиксировать V1 kinds: `document`, `folder`, `url(http/https)`.
- [x] Явно оставить `mailto` и custom URI schemes вне V1.
- [x] Сделать success factual вокруг shell acceptance, а не обязательного PID/window.
- [x] Делать optional `handlerProcessId` только как enrichment.

`Package B`

- [x] Добавить `WindowsOpenTargetToolRegistration` и explicit flat `inputSchema`.
- [x] Добавить raw-arguments binding и gated `OpenTarget(...)` handler в `WindowTools`.
- [x] Перевести `windows.open_target` в implemented/publication surface без drift между `ToolContractManifest`, `okno.contract` и `tools/list`.
- [x] Добавить dedicated `open_target.runtime.completed` event и `launch/open-target-*.json` artifact.
- [x] Добавить internal preview marker `open_target.preview.completed`.
- [x] Не вводить новую redaction class; reuse `launch_payload`.
- [x] Не вводить auto-attach или auto-focus.

`Package C`

- [x] Подтвердить L1 coverage для request validation, shell failure mapping, runtime service и redaction и сохранить её зелёной в полном contour.
- [x] Добавить недостающие L2 boundary tests на `blocked / needs_confirmation / dry_run_only` и publication parity.
- [x] Добавить L3 smoke на folder target без browser/editor assumptions.
- [x] Синхронизировать observability/generated/product docs в том же цикле.
- [x] После implementation прогнать `scripts/build.ps1`, `scripts/test.ps1`, `scripts/smoke.ps1`, `scripts/refresh-generated-docs.ps1`, `scripts/codex/verify.ps1` строго последовательно.

## 9. L1/L2/L3 test ladder

### L1

- `OpenTargetRequestValidatorTests`
  - empty `targetKind` / empty `target`;
  - unsupported `targetKind`;
  - relative / drive-relative / device-style path for `document` and `folder`;
  - executable / script / launcher extension under `document`, включая Python launcher-associated `.py`, `.pyw`, `.pyc`, packaged `.pyz` / `.pyzw` и Java executable `jar`;
  - `mailto`, `file`, `ms-settings`, custom scheme under `url`;
  - extra fields `verb`, `workingDirectory`, `environment`, `waitForWindow`, `timeoutMs`;
  - safe preview identity for folder/document vs URL.
- `ShellExecuteOpenTargetPlatformTests`
  - success without process handle;
  - success with process id;
  - `SE_ERR_FNF` / `SE_ERR_PNF` -> `target_not_found`;
  - `SE_ERR_NOASSOC` / `SE_ERR_ASSOCINCOMPLETE` / `GetLastError(ERROR_NO_ASSOCIATION)` -> `no_association`;
  - access denied mapping;
  - generic reject -> `shell_rejected_target`;
  - no unexpected UI flags drift (`SEE_MASK_FLAG_NO_UI` present).
- `OpenTargetServiceTests`
  - `target_open_requested` vs `handler_process_observed`;
  - no `HandlerProcessId` on accepted-without-process path;
  - no mandatory PID/window semantics in success definition;
  - unexpected exceptions after allowed live-path entry still materialize terminal runtime artifact/event with `failure_stage`;
  - artifact/event materialization remains best-effort.
- `OpenTargetPathInspectorTests`
  - UNC path and remote-drive path degrade to `Unresolved` without touching filesystem attributes;
  - local fixed-drive path keeps existing file-vs-directory refinement;
  - remote/device-style path no longer adds token-blind network latency before shell dispatch.
- `AuditPayloadRedactorTests`
  - document/folder path redaction to basename only;
  - URL redaction suppresses full URL, query, fragment and keeps only `uri_scheme`;
  - fail-safe suppression path.
- `ToolExecutionTests`
  - policy-bearing `windows.open_target` rejects raw execution path;
  - gated decision markers remain namespaced under `gate_*`.

### L2

- `WindowOpenTargetToolTests`
  - blocked / needs_confirmation / dry_run_only do not invoke runtime service;
  - allowed dry-run returns safe preview and no factual runtime event;
  - allowed live returns `done` with `target_open_requested` or `handler_process_observed`;
  - invalid transport binding returns tool-level `failed`;
  - audit sink failure after factual result does not rewrite payload to generic failure.
- `ToolContractManifestTests`
  - `Package A`: preset/descriptor freeze without public publication;
  - `Package B`: implemented surface contains `windows.open_target` and keeps `windows.launch_process` unchanged.
- `AdminToolTests`
  - `okno.contract` publishes `execution_policy` for `windows.open_target`;
  - `okno.health` launch readiness remains the same because gate foundation не меняется.
- `McpProtocolSmokeTests`
  - `tools/list` parity after publication;
  - `structuredContent` + one text content block;
  - `isError=true` for blocked/needs_confirmation/dry_run_only/failed.

### L3

Strict sequential verification loop:

1. `scripts/build.ps1`
2. `scripts/test.ps1`
3. `scripts/smoke.ps1`
4. `scripts/refresh-generated-docs.ps1`
5. `scripts/codex/verify.ps1`

No parallel run in the same worktree, because these commands share build/generated artifacts and repo guardrails already запрещают такой overlap.

## 10. Smoke strategy

### 10.1. Deterministic smoke target

На обычной Windows машине самым детерминированным V1 smoke target является `folder`, а не `document` и не `url`.

Почему:

- folder open идёт через built-in shell/Explorer, а не через user-specific default browser/editor/mail client;
- smoke не зависит от конкретного приложения вроде Notepad или Chrome;
- shell-open folder path хорошо соответствует V1 success semantics “request accepted”, даже если Explorer reuse-ит уже running process/window/tab.

### 10.2. Live smoke acceptance

L3 pass criterion для `windows.open_target` должен быть таким:

- `isError == false`;
- `structuredContent.status == "done"`;
- `structuredContent.decision == "done"`;
- `structuredContent.resultMode` равен `target_open_requested` или `handler_process_observed`;
- `acceptedAtUtc` присутствует;
- `artifactPath` указывает на `artifacts/diagnostics/<run_id>/launch/open-target-*.json`;
- `events.jsonl` содержит ровно один `open_target.runtime.completed` с safe parity set к public payload.

Не требуется для pass:

- новый PID;
- новое окно;
- foreground/focus;
- подтверждение, что Explorer открыл новый top-level window, а не reused existing window/tab/process.

### 10.3. Optional enrichment

Если live path вернул `handlerProcessId`, smoke может сделать best-effort enrichment:

- проверить, что такой PID существует сразу после tool call;
- при желании зафиксировать image name в report.

Но это не должно быть pass/fail criterion, потому что official shell-open path не гарантирует new process handle.

### 10.4. Dry-run smoke

Отдельный dry-run scenario:

- `targetKind = folder`;
- `target = <throwaway folder under external disposable smoke probe root>`;
- `dryRun = true`;
- expected `status == "done"` и `decision == "done"` with preview-only payload;
- expected internal marker `open_target.preview.completed`;
- expected отсутствие `open_target.runtime.completed` и runtime artifact;
- expected no need to close or clean external handler UI, потому что live side effect не совершался.

### 10.5. Cleanup policy

Smoke не должен пытаться закрывать Explorer или другой reused handler process.

Правильный cleanup:

- target folder создаётся во внешнем disposable probe-root вне repo-owned `artifacts/smoke/<run_id>`;
- probe-folder не удаляется в том же run, если ownership shell handler не доказан;
- retention/cleanup evidence tree `artifacts/smoke/<run_id>` не должен зависеть от того, удерживает ли Explorer probe-target open;
- cleanup не должен зависеть от того, открылся ли target в already-running app.

## 11. Docs sync

При actual implementation этого slice синхронизировать в том же цикле:

- `docs/architecture/observability.md`
- `docs/generated/project-interfaces.md`
- `docs/generated/commands.md`
- `docs/generated/test-matrix.md` если `refresh-generated-docs.ps1` его меняет
- `docs/product/okno-roadmap.md`
- `docs/product/okno-spec.md`
- `docs/CHANGELOG.md`

Conditional docs:

- `docs/architecture/openai-computer-use-interop.md` — только если понадобится уточнить wording про shipped split `windows.launch_process` / `windows.open_target`;
- `docs/product/okno-vision.md` — только если future list формулировка должна перейти в shipped language;
- `docs/bootstrap/bootstrap-status.json` — только если generated refresh реально дал diff.

Жёсткие rules:

- не отмечать roadmap row 14 как реализованный без tests + smoke + docs sync;
- не менять wording shipped `windows.launch_process` beyond keeping the sibling split explicit;
- observability doc должна явно фиксировать, что `open_target` reuse-ит launch-family evidence directory, но не reuse-ит `LaunchProcessResult`.

## 12. Rollback / risk notes

- Shell-open acceptance не равна new process creation; именно поэтому V1 success строится вокруг `target_open_requested`, а не вокруг PID.
- `workingDirectory` и `verb` intentionally исключены: official docs не дают достаточно честной и полезной semantics для document/URL V1, а verb surface быстро превращается в separate exec-plan.
- `mailto` и custom URI schemes intentionally оставлены вне V1: они слишком зависят от machine-local associations, UI state и чувствительных payloads.
- Conservative document denylist должен оставаться fail-closed и покрывать executable/script/launcher-like extensions; лучше extra rejection, чем скрытый process-launch surrogate через shell-open.
- Admission boundary для `document` / `folder` intentionally уже, чем “любой fully qualified Windows path”: V1 принимает только DOS local и UNC forms, а device-style `\\?\` / `\\.\` paths считаются вне публичного request surface и reject-ятся на validator boundary.
- Live kind refinement должен избегать только network-sensitive preflight: UNC и `DRIVE_REMOTE` деградируют в `Unresolved`, но supported local media (`DRIVE_FIXED`, `DRIVE_REMOVABLE`, `DRIVE_CDROM`, `DRIVE_RAMDISK`) сохраняют truthful file-vs-directory refinement.
- `launch_payload` redaction для `open_target` должен скрывать raw full path, raw URL, query, fragment и любые будущие verb-like fields; для URL safe identity в audit/result = only `uri_scheme`.
- Evidence contract должен быть additive: при нестабильности open-target runtime seam сначала откатывается public publication и related docs surface, а shared gate/readiness/redaction foundation не трогается.
- Current single-result V1 intentionally не обещает caller-visible cancellation после необратимого shell dispatch: после перехода в dispatched runtime ждёт factual completion, а bounded request-lifecycle split потребовал бы отдельной task/pending модели и выходит за границы этого slice.
- Если implementation team захочет уточнить optional `handler_process_observed` expectations для конкретных target kinds, допустим один узкий локальный эксперимент до final freeze: маленький repo-local probe поверх `ShellExecuteExW` для `folder`, `document`, `https://example.com`, который записывает только `success/failure`, наличие `hProcess` и `GetProcessId(...)`. Этот probe нужен только для smoke tuning и не должен менять public contract сам по себе.
