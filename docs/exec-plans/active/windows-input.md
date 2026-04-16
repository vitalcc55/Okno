# ExecPlan: windows.input

Статус: planned  
Создан: 2026-04-10

## 1. Goal

Спроектировать и подготовить shipped public slice `windows.input` как quiet, Codex-friendly, OpenAI-compatible action tool для Windows без обхода уже shipped `health/readiness -> execution policy -> gated boundary -> runtime service -> evidence -> smoke/docs` контура.

Жёсткая формулировка цели:

- оставить один public tool `windows.input`, а не распиливать surface на `click_tool` / `type_tool` / `scroll_tool`;
- immediate delivery target = `click` first + structural action schema freeze;
- держать vocabulary совместимым с GUI action loop уровня `move / click / double_click / drag / scroll / type / keypress`, с later extension для `hotkey` и `paste`;
- не смешивать `windows.input` c `windows.capture`, `windows.wait`, `windows.launch_process`, `windows.open_target` и будущим `windows.uia_action`;
- сохранить shared gate, confirmation, redaction, readiness и audit как обязательный boundary, а не как optional wrapper.

## 2. Non-goals

- не делать весь input zoo в одном runtime пакете без доказуемой product-выгоды;
- не смешивать coordinate input с semantic click по element id внутри того же V1 contract;
- не тащить clipboard внутрь `type` скрытым fallback-ом;
- не делать hidden attach/focus/activate/restore side effects ради “удобства”;
- не считать `SendInput`-dispatch эквивалентом verified user outcome;
- не перестраивать core runtime вокруг built-in OpenAI `computer use`;
- не публиковать public schema, которая обещает больше action literals, чем реально поддерживает shipped runtime wave.

## 3. Current repo state

- `Okno` уже shipped как observe/verify/guardrails runtime: `list_monitors`, `list_windows`, `attach_window`, `focus_window`, `activate_window`, `capture`, `windows.uia_snapshot`, `windows.wait`, `okno.health`, `windows.launch_process`, `windows.open_target`.
- `windows.input` уже объявлен в [src/WinBridge.Runtime.Tooling/ToolNames.cs](../../../src/WinBridge.Runtime.Tooling/ToolNames.cs) и [src/WinBridge.Runtime.Tooling/ToolContractManifest.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractManifest.cs) как deferred tool с frozen execution policy:
  - `policy_group=input`
  - `risk_level=destructive`
  - `guard_capability=input`
  - `supports_dry_run=false`
  - `confirmation_mode=required`
  - `redaction_class=text_payload`
- deferred public registration уже использует schema-preserving `actions[]` envelope и structured `unsupported` result, но implemented lifecycle, live gated handler и runtime dispatch по-прежнему не опубликованы.
- shared gate уже обязателен для policy-bearing tools: raw `ToolExecution.Run(...)`/`RunAsync(...)` для `windows.input` fail-fast запрещён, нужен только `RunGated(...)` / `RunGatedAsync(...)`.
- shared readiness для `input` больше не placeholder blocked: `RuntimeGuardPolicy.BuildInput(...)` уже выражает reusable `ready/degraded/blocked/unknown` baseline, а target-specific integrity/focus checks ещё остаются на future runtime boundary.
- runtime seam для input больше не пустой: [src/WinBridge.Runtime.Windows.Input/IInputService.cs](../../../src/WinBridge.Runtime.Windows.Input/IInputService.cs) уже фиксирует contract-level `ExecuteAsync(...)`, но concrete dispatch implementation ещё отсутствует.
- sibling rollout patterns уже materialized:
  - `windows.wait` показал canonical target resolution, artifact/event model и `structuredContent + TextContentBlock` boundary.
  - `windows.launch_process` показал gated public action tool с preview-freeze/result modes/materializer lifecycle.
  - `windows.open_target` показал, как делать sibling service + runtime artifact/event без ложных process/window promises.
- [tests/WinBridge.SmokeWindowHost/Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs) уже даёт пригодный deterministic helper surface:
  - кнопка `Run semantic smoke`
  - checkbox `Remember semantic selection`
  - textbox `Smoke query input`
  - visual heartbeat panel/label
  - tree view
  - canonical focus target behavior

Следствие:

- `windows.input` нельзя строить как isolated helper вокруг `SendInput`;
- сначала нужно разморозить honest contract, input-readiness и result/evidence model, и только потом публиковать handler.

## 4. Official constraints

### 4.1. MCP tool result contract

Source: [MCP Tools spec](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)

Binding facts:

- tool execution errors должны идти в normal `result` c `isError: true`, а не как protocol-level JSON-RPC error;
- `structuredContent` является canonical structured payload;
- tool, который возвращает structured result, должен также вернуть serialized JSON в `TextContent` block;
- clients SHOULD держать human-in-the-loop и confirmation prompt для sensitive operations.

Decision for `windows.input`:

- gate outcomes `blocked` / `needs_confirmation` публикуются как canonical tool result;
- live runtime outcome не маскируется под plain text;
- final public result shape изначально должен вмещать `done`, `verify_needed`, `failed`, gate metadata и per-action factual details.

### 4.2. OpenAI / Codex compatibility target

Source: [OpenAI computer use guide](https://developers.openai.com/api/docs/guides/tools-computer-use), [OpenAI MCP/connectors guide](https://developers.openai.com/api/docs/guides/tools-connectors-mcp), [OpenAI skills guide](https://developers.openai.com/api/docs/guides/tools-skills), [Shell + Skills + Compaction](https://developers.openai.com/blog/skills-shell-tips)

Binding facts:

- built-in Computer use loop already normalizes action family `click`, `double_click`, `scroll`, `type`, `wait`, `keypress`, `drag`, `move`, `screenshot`;
- `keypress` в OpenAI guidance предназначен для standalone keyboard input, а held modifiers для mouse interactions рекомендуется держать в mouse action `keys[]`, а не раскладывать на отдельные keyboard steps;
- после action batch guide требует вернуть updated screenshot и рекомендует `detail: "original"`; если screenshot downscaled, координаты нужно remap-ить обратно в original coordinate space;
- OpenAI прямо допускает custom MCP/tool harness вместо перестройки продукта вокруг built-in `computer`;
- OpenAI для existing harnesses рекомендует сохранять mature action execution, observability, retries и guardrails, а не ломать их ради compatibility.

Decision for `windows.input`:

- сохранить `Okno`-native contract и построить quiet one-tool action surface;
- vocabulary выровнять по `move/click/double_click/drag/scroll/type/keypress`;
- `right_click` не делать отдельным public action literal: это `click` c `button=right`;
- screenshot/capture остаются отдельным explicit step, а `windows.input` использует capture-derived coordinates, но не поглощает capture внутрь себя.

### 4.3. Win32 pointer and coordinate constraints

Source: [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput), [SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos), [GetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos), [ClientToScreen](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clienttoscreen), [ScreenToClient](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-screentoclient), [GetSystemMetrics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmetrics), [GetDoubleClickTime](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdoubleclicktime)

Binding facts:

- `SendInput` mouse absolute path использует normalized range `0..65535`; без `MOUSEEVENTF_VIRTUALDESK` эти координаты маппятся на primary monitor, с `MOUSEEVENTF_VIRTUALDESK` — на весь virtual desktop;
- `SetCursorPos` / `GetCursorPos` работают в screen coordinates;
- вызывающий процесс должен иметь доступ к current input desktop;
- `ClientToScreen` и `ScreenToClient` переводят между client и screen device coordinates;
- `GetSystemMetrics(SM_XVIRTUALSCREEN/Y/CX/CY)` задаёт virtual desktop rectangle;
- `GetDoubleClickTime()` возвращает maximum interval between the first and second click of a double-click;
- `GetSystemMetrics(SM_CXDOUBLECLK/SM_CYDOUBLECLK)` задают pixel rectangle, внутри которого второй click должен остаться для system double-click recognition.

Decision for V1 backend:

- primary V1 pointer backend = `SetCursorPos(screenX, screenY)` + `SendInput` для button down/up, а не full absolute mouse path через normalized `SendInput`;
- этот путь проще для `capture_pixels -> screen` remap, avoids primary-monitor-only footgun и даёт прямую post-move verification через `GetCursorPos`;
- `MOUSEEVENTF_ABSOLUTE` + `MOUSEEVENTF_VIRTUALDESK` остаётся fallback/diagnostic path, а не default.

### 4.4. Focus, foreground, integrity and protected UI

Source: [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow), [Application manifests](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests), [Mandatory Integrity Control](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control), [UI Automation Security Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-security-overview)

Binding facts:

- `SendInput` is subject to UIPI; input can be injected only into applications at an equal or lower integrity level;
- neither return value nor `GetLastError` reliably indicate UIPI blocking;
- `SetForegroundWindow` has documented restrictions and Windows may deny the request even when some conditions look satisfied;
- `uiAccess` is `false` by default; protected UI access requires manifest + signing + special deployment conditions;
- MIC means low cannot write to medium, standard users are medium, elevated users are high, and new processes inherit min(user,file) integrity.

Decision for `windows.input`:

- shared gate must remain authoritative for environment readiness, but `windows.input` also needs target-specific integrity preflight before dispatch;
- medium-integrity without `uiAccess` нельзя трактовать как universal hard block навсегда, иначе slice будет useless on normal desktops; instead shared readiness should move from always-blocked placeholder to `degraded` baseline, а live runtime обязан fail-closed на higher-integrity target;
- `needs_confirmation` не может bypass UIPI / protected UI hard block;
- success of prior `focus_window` / `activate_window` cannot be treated as global allow signal for live input.

### 4.5. Keyboard normalization constraints

Source: [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput), [MapVirtualKeyA](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeya), [VkKeyScanExA](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-vkkeyscanexa), [GetKeyboardLayout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout)

Binding facts:

- `GetKeyboardLayout(0)` returns active input locale for the current thread and may change dynamically via `WM_INPUTLANGCHANGE`;
- `VkKeyScanEx` depends on keyboard layout and returns shift-state bits together with virtual-key mapping, or `-1` when translation does not exist;
- `MapVirtualKey(VK -> VSC)` returns left-hand scan code for non-side-specific keys unless the caller provides extended scan data;
- `KEYEVENTF_UNICODE` / `VK_PACKET` deliver Unicode text to the foreground thread queue instead of hardware-like key semantics.

Decision for planning:

- `keypress` stays schema-frozen in Package A, but live shipping waits until layout-aware normalization and redaction for keyboard sequences are explicitly covered;
- `type` and `keypress` do not ship in click-first wave;
- later `type` must choose one honest path per action (`unicode_text` vs `virtual_key_sequence`) and expose which path was used in runtime result.

### 4.6. Reference repo comparison

| Reference | Что взять | Что не копировать |
| --- | --- | --- |
| `Windows-MCP` | action family already split into `Click / Type / Scroll / Move / Shortcut / Wait`; coordinates-or-label ergonomics; breadth-first proof that agents like direct action vocabulary | separate noisy tool zoo, string-only results, no shared gate/readiness/evidence/redaction, no honest verify model |
| `Windows-MCP.Net` | breadth map of what users eventually ask from desktop runtimes | слишком широкий surface: app/browser/filesystem/OCR/system control рядом с click tools; для `Okno` это noise before core action contract is proven |
| `Peekaboo` | quiet interaction vocabulary; explicit split between coordinate target and element/snapshot target; concrete `verifyFocusForCoordinateClick()` fail-closed pattern вместо silent wrong-window click | отдельные CLI commands вместо single MCP tool; platform-specific focus helpers and snapshot model cannot be transplanted 1:1 |
| `pywinauto-mcp` | idea that compact surface can help models stay focused | portmanteau tools смешивают state, action, OCR и operator workflow; convenience wins over contract strictness and would blur `Okno` boundaries |

Net takeaways:

- для `Okno` правильная форма не breadth-first zoo, но и не giant “do everything” tool;
- лучший compromise = один quiet `windows.input` c typed `actions[]`, explicit target model, existing gate/evidence, and no hidden semantic click path;
- из references strongest concrete pattern для V1 = Peekaboo-style focus verification for coordinate click plus Windows-MCP-style action vocabulary breadth, но поверх `Okno`-native result/evidence model.

## 5. Public contract proposal

### 5.1. Decision: one tool, not zoo

`windows.input` остаётся одним public tool с ordered `actions[]`.

Почему:

- это совпадает с roadmap и product spec;
- это ближе к OpenAI `computer use` loop и keeps surface quiet;
- это не раздувает `tools/list` отдельными `click`, `scroll`, `type`, `press`;
- это даёт один shared gate/redaction/evidence boundary для всех low-level actions.

### 5.2. Tool positioning

- public name: `windows.input`
- capability: `windows.input`
- MCP type: action tool, `UseStructuredContent = true`
- safety class: `os_side_effect`
- execution policy: existing input preset from manifest
- boundary rule: only `RunGatedAsync(...)`
- no public `dryRun` flag in V1 because `supports_dry_run=false`

### 5.3. Top-level request shape

Предлагаемый V1 request envelope:

```csharp
public sealed record InputRequest
{
    public long? Hwnd { get; init; }
    public IReadOnlyList<InputAction> Actions { get; init; } = [];
    public bool Confirm { get; init; }
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
```

Contract rules:

- `actions` обязателен и non-empty;
- `confirm` остаётся только transport-visible gate signal;
- `dryRun` в public schema отсутствует и extra-field `dryRun` reject-ится fail-closed;
- `hwnd` = default explicit target for window-scoped actions; if absent, runtime may use attached window only for window-scoped actions;
- active-window fallback для live input запрещён.

### 5.4. Top-level result shape

Предлагаемый top-level result:

```csharp
public sealed record InputResult(
    string Status,
    string Decision,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    long? TargetHwnd = null,
    string? TargetSource = null,
    int CompletedActionCount = 0,
    int? FailedActionIndex = null,
    IReadOnlyList<InputActionResult>? Actions = null,
    string? ArtifactPath = null,
    string? RiskLevel = null,
    string? GuardCapability = null,
    bool RequiresConfirmation = false,
    bool DryRunSupported = false,
    IReadOnlyList<GuardReason>? Reasons = null);
```

Frozen status set:

- `done`
- `verify_needed`
- `failed`
- `blocked`
- `needs_confirmation`

Interpretation:

- `blocked` / `needs_confirmation` = gate outcomes before live dispatch;
- `failed` = invalid request, stale target, focus/integrity precondition failure, mapping failure, dispatch failure or unexpected runtime failure after allowed path;
- `verify_needed` = dispatch completed factually, but tool intentionally does not claim end-to-end UI outcome without explicit follow-up proof;
- `done` is reserved for future inline verify hook or for narrow V1 cases where the tool itself can prove the requested postcondition without absorbing `windows.wait`.

V1 live expectation:

- most successful click-first outcomes will be `verify_needed`, not `done`;
- `isError = false` for `verify_needed`;
- downstream proof step is explicit `windows.wait` and/or `windows.uia_snapshot`, not hidden auto-wait.

### 5.5. Batch semantics

Batch rules are frozen up front:

- execution is strictly ordered;
- runtime stops on first non-success action;
- rollback does not exist;
- side effects of already completed actions are treated as committed and remain caller-visible;
- top-level result always reports `CompletedActionCount`;
- top-level result reports `FailedActionIndex` when the first non-success action exists;
- `Actions[]` preserves per-action factual outcome up to and including the first failed action;
- actions after `FailedActionIndex` are not attempted and must not be synthesized as pseudo-results.

This wording is intentional:

- it prevents future implementation drift toward fake transactional semantics;
- it keeps multi-action batches usable for `move -> click` and later `move -> drag`, while staying honest about irreversible cursor/input effects.

### 5.6. Failure code set

Initial V1 failure codes:

- `invalid_request`
- `unsupported_action_type`
- `unsupported_coordinate_space`
- `missing_target`
- `stale_explicit_target`
- `stale_attached_target`
- `target_not_foreground`
- `target_minimized`
- `target_integrity_blocked`
- `capture_reference_required`
- `capture_reference_stale`
- `point_out_of_bounds`
- `cursor_move_failed`
- `input_dispatch_failed`

Rule:

- gate outcomes keep shared `GuardReason` codes, not tool-specific failure codes;
- failure codes express only tool semantics after request bind or after allowed live path.

## 6. Action schema proposal

### 6.1. Structural freeze now

Package A freezes the union shape, even if public Package C initially advertises only the shipped subset.

Frozen structural family:

- `move`
- `click`
- `double_click`
- `drag`
- `scroll`
- `type`
- `keypress`

Later additive extensions:

- `hotkey`
- `paste`

Important decision:

- `right_click` is not a separate action literal;
- right-click = `click` with `button=right`;
- this keeps vocabulary quieter and closer to OpenAI action family.

### 6.2. Pointer action DTO shape

```csharp
public sealed record InputPoint
{
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed record InputBounds
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
}

public sealed record InputCaptureReference
{
    public InputBounds? Bounds { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public int? EffectiveDpi { get; init; }
    public DateTimeOffset? CapturedAtUtc { get; init; }
}

public sealed record InputAction
{
    public string Type { get; init; } = string.Empty;
    public InputPoint? Point { get; init; }
    public IReadOnlyList<InputPoint>? Path { get; init; }
    public string? CoordinateSpace { get; init; }
    public string? Button { get; init; }
    public IReadOnlyList<string>? Keys { get; init; }
    public string? Text { get; init; }
    public string? Key { get; init; }
    public int? Repeat { get; init; }
    public int? Delta { get; init; }
    public string? Direction { get; init; }
    public InputCaptureReference? CaptureReference { get; init; }
}
```

Validation policy:

- public Package C schema advertises only `move`, `click`, `double_click` literals;
- `drag` / `scroll` / `type` / `keypress` live shipping is deferred, but their structural slots stay reserved in DTO/validator architecture;
- request binding preserves `missing` / `explicit null` / `invalid token` / `non-object token` distinction across the frozen `InputRequest` / `InputAction` / nested input DTO hierarchy before validator runs, so malformed `actions`, action objects, token-like fields, `keys`, `point`, `path`, `captureReference`, `bounds` and capture geometry reject as typed `invalid_request` instead of surfacing serializer exceptions;
- DTO layer preserves field presence for action-specific fields and coordinate objects, so validator can reject `provided-but-empty/null` forbidden fields and partial coordinate objects without banning explicit zero coordinates;
- extra properties reject-ятся fail-closed;
- batch execution is ordered and fail-fast on first non-success action;
- max batch size V1 = `16` actions to avoid giant unverified side-effect bursts.

### 6.3. First shipped wave

Live V1 action subset:

- `move`
- `click`
- `double_click`
- `click(button=right)` as the right-click path

Explicitly not in first live wave:

- `drag`
- `scroll`
- `type`
- `keypress`
- `hotkey`
- `paste`

Why:

- immediate product value is coordinate click on known target;
- drag/scroll/type/keypress each introduce extra verify and keyboard-layout forks;
- this keeps first rollout narrow while freezing the final action envelope.

### 6.4. `double_click` semantics

`double_click` is frozen as a dedicated action literal, not as “`click` twice if convenient”.

V1 contract:

- target point is resolved once;
- runtime performs one cursor move to the resolved point;
- runtime dispatches two left-button click sequences at the exact same resolved screen point;
- runtime does not insert any intermediate pointer motion between the two clicks;
- runtime uses a fixed internal inter-click delay of `50ms`, which is intentionally well below the system maximum returned by `GetDoubleClickTime()`;
- because the point is identical, distance stays within the system double-click rectangle defined by `SM_CXDOUBLECLK` / `SM_CYDOUBLECLK`.

Implications:

- V1 relies on Windows system double-click recognition semantics, not on an Okno-specific custom gesture detector;
- no public knob for inter-click timing or distance is exposed;
- if the OS or target application still does not interpret the gesture as a semantic double-click, the result remains `verify_needed` until an explicit post-action proof confirms the effect.

## 7. Targeting / coordinate model

### 7.1. Targeting decision

First shipped `windows.input` is coordinate-based only.

Out of scope for this tool:

- element id
- selector
- semantic click by UIA node
- hidden “find then click” behavior

That territory belongs to future `windows.uia_action`.

### 7.2. Window target policy

Window-scoped actions use this precedence:

1. explicit `hwnd` from request
2. attached window from session

Not allowed:

- active-window fallback
- auto-attach
- stale explicit target falling back to attached target
- stale attached target falling back to current foreground

Rationale:

- live input side effects are too expensive for heuristic target guessing;
- `wait` and `uia_snapshot` can afford `explicit -> attached -> active`, but `input` must be stricter.

### 7.3. Coordinate spaces

Public canonical space for V1 = `capture_pixels`.

Supported V1 spaces:

- `capture_pixels`
- `screen`

Not public in V1:

- `window_client`

Why:

- `capture_pixels` is the natural planning space after `windows.capture` and the cleanest bridge to screenshot-first loops;
- `screen` is needed as a low-level escape hatch and for future external adapters that already remap coordinates themselves;
- `window_client` adds a second window-local public coordinate system without giving a clear product gain in click-first rollout.

### 7.4. `capture_pixels` semantics

For `capture_pixels`:

- `hwnd` or attached window is mandatory;
- `captureReference` is mandatory;
- coordinates are expressed relative to the authoritative raster returned by `windows.capture(scope=window)`;
- runtime remaps to screen coordinates using `captureReference.Bounds` and current live window bounds;
- `capture_reference_stale` is raised when any of the following becomes true before dispatch:
  - current authoritative window width or height differs from `captureReference.Bounds` by any physical pixel;
  - current authoritative window `Left` or `Top` differs from `captureReference.Bounds` by more than `1` physical pixel;
  - `captureReference.EffectiveDpi` is present and current authoritative `EffectiveDpi` differs from it;
  - `captureReference.PixelWidth` or `PixelHeight` no longer match the authoritative capture geometry implied by the current live window/capture metadata;
- the `1`-pixel tolerance exists only for origin rounding drift between capture/runtime paths; resize is never tolerated in V1.

### 7.5. `screen` semantics

For `screen`:

- `point` is in physical screen pixels;
- `captureReference` must be absent;
- if `hwnd` is also provided, runtime enforces that the target window is frontmost and the resolved point is still meaningful for that target;
- if no `hwnd` is provided, runtime does not promise app identity and result is expected to stay `verify_needed` unless a later explicit proof step confirms the effect.

## 8. Focus / verify / gate model

### 8.1. Focus / activation policy

Fixed V1 rule:

- `windows.input` never performs hidden `attach`, `focus`, `activate` or `restore`;
- caller must explicitly use `windows.activate_window` / `windows.focus_window` before window-scoped input if needed;
- live click fails closed when resolved target window is not current foreground top-level window;
- minimized or non-usable target fails before pointer dispatch.

Concrete reuse:

- use existing shell inventory and `WindowTargetResolver` identity matching;
- use existing activation/focus slice only as a previous explicit step, not as hidden helper.

### 8.2. Verify policy

`SendInput` return value is not enough.

V1 effect model:

- tool proves preconditions and factual dispatch;
- tool does not claim the business/UI outcome unless another authoritative source proves it;
- therefore normal live success returns `verify_needed`.

Expected loop:

1. `windows.capture` or `windows.uia_snapshot`
2. `windows.input`
3. `windows.wait` and/or `windows.uia_snapshot`

Optional future extension:

- later add a narrow inline `expectedAfter` hook only if it can reuse `windows.wait` semantics without embedding a second wait DSL into `windows.input`.

### 8.3. Gate and readiness model

Shared gate stays mandatory, but Package A must upgrade `BuildInput(...)` from placeholder to real baseline.

Required guard refactor:

- remove unconditional `capability_not_implemented` once the runtime slice exists;
- classify environment readiness separately from target-specific integrity/focus checks.

Planned shared readiness shape:

- `blocked`: unusable desktop/session, integrity below medium, hard session mismatch
- `unknown`: prerequisites not resolved
- `degraded`: medium integrity without `uiAccess`, because higher-integrity target interaction is unconfirmed
- `ready`: high/system integrity or medium + `uiAccess`

Target-specific hard block stays in runtime:

- higher-integrity target than current process
- cross-session target and protected-UI paths that the runtime can already prove through non-default desktop or current-token `uiAccess` / integrity evidence; Package B does not add a separate target-side protected-UI probe without a primary-source-backed signal
- target not foreground / not usable

### 8.4. Dry-run semantics

Decision:

- keep `supports_dry_run=false`;
- do not add public `dryRun` to request;
- rejected and confirmation payloads may still include safe normalized request metadata, but that is not a dry-run preview branch.

Why:

- click-first input has no honest side-effect-free preview that is stronger than validation;
- existing gate already models no-dry-run tools correctly;
- fake preview would create false confidence around real cursor/focus side effects.

### 8.5. Redaction

Decision:

- keep manifest-level `redaction_class=text_payload`;
- click-only V1 avoids over-redaction by not writing raw text-like fields into audit/event data at all;
- safe click metadata stays visible: `action_count`, `action_types`, `button`, `coordinate_space`, `target_hwnd`, `resolved_screen_point`, `artifact_path`, `failure_code`.

Follow-up requirement before keyboard wave:

- extend `AuditPayloadRedactor` and tests for `key`, `keys`, `repeat`, `text`, `pressEnter`, `clear`;
- do not ship `type` / `keypress` until that redaction branch is proven.

## 9. Integration points by file

| Файл | Роль | План для `windows.input` |
| --- | --- | --- |
| [src/WinBridge.Runtime.Tooling/ToolNames.cs](../../../src/WinBridge.Runtime.Tooling/ToolNames.cs) | source of truth for tool literal | Reuse existing `WindowsInput`; new literal sets live in contracts, not here. |
| [src/WinBridge.Runtime.Tooling/ToolDescriptions.cs](../../../src/WinBridge.Runtime.Tooling/ToolDescriptions.cs) | public wording | Добавить canonical description для `windows.input` и parameter descriptions для `actions`, `hwnd`, `confirm`, coordinate-space notes. |
| [src/WinBridge.Runtime.Tooling/ToolContractManifest.cs](../../../src/WinBridge.Runtime.Tooling/ToolContractManifest.cs) | lifecycle / export truth | `Package A`: keep deferred descriptor but sync wording. `Package C/E`: flip `windows.input` to implemented and smoke-required only после runtime + observability + smoke proof. |
| [src/WinBridge.Runtime.Tooling/ToolExecutionPolicyDescriptor.cs](../../../src/WinBridge.Runtime.Tooling/ToolExecutionPolicyDescriptor.cs) | policy types | Reuse existing `Input` group / `Destructive` risk / `TextPayload`; no new gate enums. |
| [src/WinBridge.Runtime.Contracts/GuardReason.cs](../../../src/WinBridge.Runtime.Contracts/GuardReason.cs) | shared reason vocabulary | Пересобрать input-specific reasons под real readiness и target/runtime failures where needed; не плодить дубли reason taxonomy в handler-е. |
| [src/WinBridge.Runtime.Contracts/CapabilityGuardSummary.cs](../../../src/WinBridge.Runtime.Contracts/CapabilityGuardSummary.cs) | capability projection | Reuse `input` capability; switch from deferred placeholder to real readiness status. |
| [src/WinBridge.Runtime.Guards/RuntimeGuardPolicy.cs](../../../src/WinBridge.Runtime.Guards/RuntimeGuardPolicy.cs) | shared readiness baseline | Главная точка refactor: `BuildInput(...)` должен перестать быть always-blocked placeholder и стать reusable `ready/degraded/blocked/unknown` baseline. |
| [src/WinBridge.Runtime.Guards/RuntimeGuardService.cs](../../../src/WinBridge.Runtime.Guards/RuntimeGuardService.cs) | snapshot source | Reuse as-is after `BuildInput(...)` refactor; no separate guard service for input. |
| [src/WinBridge.Runtime.Guards/ToolExecutionGate.cs](../../../src/WinBridge.Runtime.Guards/ToolExecutionGate.cs) | decision matrix | Reuse as-is; `windows.input` intentionally keeps `supports_dry_run=false` and `confirmation_mode=required`. |
| [src/WinBridge.Runtime.Diagnostics/ToolExecution.cs](../../../src/WinBridge.Runtime.Diagnostics/ToolExecution.cs) | mandatory gated boundary | `windows.input` handler only through `RunGatedAsync(...)`. |
| `src/WinBridge.Runtime.Contracts/Input*.cs` | new DTO/value-set layer | Add `InputRequest`, `InputAction`, `InputPoint`, `InputCaptureReference`, `InputResult`, `InputActionResult`, status/result/failure literals, validators. |
| [src/WinBridge.Runtime.Windows.Input/IInputService.cs](../../../src/WinBridge.Runtime.Windows.Input/IInputService.cs) | runtime seam | Replace empty seam with `Task<InputResult> ExecuteAsync(InputRequest request, InputExecutionContext context, CancellationToken cancellationToken);`. |
| `src/WinBridge.Runtime.Windows.Input/Win32InputService.cs` | concrete V1 execution path | Single V1 runtime service: target resolution, geometry remap, preflight, cursor move, button dispatch, factual per-action result assembly. |
| `src/WinBridge.Runtime.Windows.Input/IInputPlatform.cs` + `Win32InputPlatform.cs` | OS abstraction | Wrap `SetCursorPos`, `GetCursorPos`, `SendInput`, `GetWindowThreadProcessId`, target integrity/session probes. |
| `src/WinBridge.Runtime.Windows.Input/InputCoordinateMapper.cs` | coordinate remap | Convert `capture_pixels` or `screen` point into factual screen point; reject stale geometry and out-of-bounds coordinates. |
| `src/WinBridge.Runtime.Windows.Input/InputResultMaterializer.cs` | evidence/event lifecycle | Write `input-*.json`, emit `input.runtime.completed`, keep best-effort artifact/event behavior aligned with launch/open/wait. |
| [src/WinBridge.Runtime.Windows.Shell/IWindowTargetResolver.cs](../../../src/WinBridge.Runtime.Windows.Shell/IWindowTargetResolver.cs) + [WindowTargetResolver.cs](../../../src/WinBridge.Runtime.Windows.Shell/WindowTargetResolver.cs) | target resolution | Add dedicated `ResolveInputTarget(...)` with precedence `explicit -> attached`, no active fallback, explicit failure codes. |
| [src/WinBridge.Runtime.Windows.Capture/GraphicsCaptureService.cs](../../../src/WinBridge.Runtime.Windows.Capture/GraphicsCaptureService.cs) + capture contracts | capture metadata source | Reuse existing `Bounds`, `PixelWidth`, `PixelHeight`, `CoordinateSpace=physical_pixels` for `capture_pixels` remap. No hidden capture call from input. |
| [src/WinBridge.Runtime.Waiting/IWaitService.cs](../../../src/WinBridge.Runtime.Waiting/IWaitService.cs) | follow-up verification | No runtime dependency for dispatch itself, but `windows.wait` remains canonical post-action proof path. |
| [src/WinBridge.Runtime/ServiceCollectionExtensions.cs](../../../src/WinBridge.Runtime/ServiceCollectionExtensions.cs) | DI wiring | Register input platform, service, result materializer and any target-integrity helper. |
| `src/WinBridge.Server/Tools/WindowsInputToolRegistration.cs` | programmatic MCP registration | Add manual input schema for shipped subset only; mirror launch/open registration pattern. |
| [src/WinBridge.Server/Tools/WindowTools.cs](../../../src/WinBridge.Server/Tools/WindowTools.cs) | public handler boundary | Replace deferred stub with bind -> validate -> RunGatedAsync -> runtime service -> `CallToolResult`; rejected payloads follow launch/open pattern. |
| [tests/WinBridge.Runtime.Tests/RuntimeGuardPolicyTests.cs](../../../tests/WinBridge.Runtime.Tests/RuntimeGuardPolicyTests.cs) | L1 guard proof | Update input capability expectations from placeholder blocked to real readiness matrix. |
| [tests/WinBridge.Runtime.Tests/ToolExecutionGateTests.cs](../../../tests/WinBridge.Runtime.Tests/ToolExecutionGateTests.cs) | L1 gate proof | Keep blocked/degraded/no-dry-run coverage for input policy. |
| [tests/WinBridge.Runtime.Tests/ToolExecutionTests.cs](../../../tests/WinBridge.Runtime.Tests/ToolExecutionTests.cs) | raw-vs-gated boundary proof | Add `windows.input` public handler path once implemented; keep raw execution rejection invariant. |
| [tests/WinBridge.Runtime.Tests/AuditPayloadRedactorTests.cs](../../../tests/WinBridge.Runtime.Tests/AuditPayloadRedactorTests.cs) | redaction proof | Add coordinate-only input event tests and later keyboard/text redaction tests before non-click wave. |
| [tests/WinBridge.Server.IntegrationTests/ToolExecutionGateBoundaryTests.cs](../../../tests/WinBridge.Server.IntegrationTests/ToolExecutionGateBoundaryTests.cs) | rejected payload semantics | Reuse synthetic gated boundary pattern for `blocked` / `needs_confirmation` input payloads. |
| [tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs](../../../tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs) | public MCP parity | Add `tools/list`, `okno.contract`, invalid input and live boundary checks for `windows.input`. |
| [tests/WinBridge.SmokeWindowHost/Program.cs](../../../tests/WinBridge.SmokeWindowHost/Program.cs) | L3 helper | Reuse existing controls; new smoke app should not be introduced unless this helper proves insufficient. |
| [scripts/smoke.ps1](../../../scripts/smoke.ps1) | real proof path | Extend owned helper scenario with click-first proof and artifact/event assertions. |

## 10. Delivery packages

### Package A — Contract freeze

Статус: `done`

Scope:

- freeze one-tool `InputRequest` / `InputAction` / `InputResult` structure;
- decide shipped subset vs reserved literals;
- define coordinate-space, button, modifier, status, result-mode and failure-code value sets;
- refactor shared `BuildInput(...)` readiness from always-blocked placeholder to reusable baseline;
- keep live runtime dispatch and implemented publication off while runtime and tests are still incomplete; schema-preserving deferred registration is allowed if it stays honest `unsupported`.

Done when:

- another engineer can implement runtime without reopening design forks on `single tool vs zoo`, `click(button=right)`, `capture_pixels`, `no active fallback`, `verify_needed`, `dry-run=false`;
- `RuntimeGuardPolicyTests` express real input-readiness matrix instead of placeholder blocked status.

#### Package A Checklist

- [x] Frozen `InputRequest`, `InputAction`, `InputResult`, value sets and validator architecture in `src/WinBridge.Runtime.Contracts/Input*.cs`.
- [x] Structural freeze kept separate from future shipped subset via validator policy; Package A code accepts `move` / `click` / `double_click` / `drag` / `scroll` / `type` / `keypress`, while click-first subset remains a later allowlist.
- [x] Action-specific allowed/required fields are centralized in `InputActionContractCatalog` and reused by validator/schema materialization, so keyboard, pointer, scroll and drag action shapes cannot drift independently.
- [x] Scalar constraints are centralized in `InputActionScalarConstraints` and mirrored by validator/schema materialization where JSON Schema can express them: non-whitespace `key` / `direction`, positive `repeat`, non-zero `delta`, and positive capture pixel dimensions.
- [x] Contract boundary preserves field presence and validity for forbidden/required fields across `InputRequest`, `InputAction` and nested input DTOs, so whitespace placeholders, explicit `null`, non-object values, invalid scalar/list tokens and partial `actions` / `point` / `path` / `captureReference` / `bounds` / capture-geometry shapes reject as typed `invalid_request` without banning explicit zero coordinates.
- [x] Deferred public `windows.input` publication uses manual schema-preserving registration with action `oneOf` branches and nested required fields while still returning structured `unsupported`, so Package A can keep the frozen `actions[]` MCP envelope without relying on reflection binding for malformed action elements.
- [x] Capture-reference bounds validation uses direct edge ordering instead of overflowing `int` derived dimensions.
- [x] Shared `BuildInput(...)` no longer uses deferred placeholder semantics and now exposes `ready` / `degraded` / `blocked` / `unknown` based on environment readiness.
- [x] `CapabilityNotImplemented` no longer appears in healthy `input` readiness summaries; medium integrity without `uiAccess` is now `degraded`, not permanently blocked.
- [x] Added dedicated `ResolveInputTarget(...)` seam with `explicit -> attached` policy and no active fallback.
- [x] Kept `windows.input` deferred, but aligned the public deferred MCP schema with the frozen `actions[]` envelope instead of leaving legacy `actionsJson`.
- [x] Synced product/interop/tooling wording with Package A decisions: one tool, `click(button=right)`, `capture_pixels` + `screen`, `verify_needed`, `supports_dry_run=false`.
- [ ] Live MCP handler, request binding into runtime execution path and `RunGatedAsync(...)` rollout remain deferred to Package C.
- [x] Internal Package B runtime slice now exists without public rollout: `Win32InputService` + `Win32InputPlatform` execute `move`, `click`, `double_click` and `click(button=right)` through `ResolveInputTarget(...)`, per-action target revalidation, foreground/minimized/session/integrity preflight, `capture_pixels -> screen` translation-only remap, factual cursor verification and fail-fast batch results.
- [x] Live pointer batches are now serialized through an internal input execution gate, and every irreversible click dispatch revalidates the live target immediately before dispatch through a refreshed coordinate-space-aware dispatch plan; `capture_pixels` can update the authoritative screen point on admissible origin drift for single-click gestures, while `double_click` now splits into pre-gesture plan refresh before the first move and a stable-point execution phase where both taps fail closed if boundary revalidation would retarget the gesture. Final click dispatch proves the factual cursor position, that the live foreground window still matches the same admitted target `HWND` and stable identity, and only then derives ambient async-state readability from that live foreground owner immediately before `SendInput`.
- [ ] Runtime artifacts/events and result materialization remain deferred to Package D.
- [ ] Smoke, fresh-host acceptance and promotion from deferred registration to implemented contract remain deferred to Package E.

### Package B — Runtime/input service

Статус: `done`

Scope:

- land concrete `Win32InputService` + `Win32InputPlatform`;
- implement `capture_pixels -> screen` remap and direct `screen` path;
- implement `move`, `click`, `double_click`, `click(button=right)` only;
- add target usability checks: resolved window, foreground, not minimized, target integrity/session compatibility;
- add per-action factual result model and fail-fast batch semantics;
- no silent success if cursor move or button dispatch cannot be proved.

Done when:

- runtime can execute click-first actions end-to-end without public publication;
- `verify_needed` vs `failed` is determined honestly;
- higher-integrity or wrong-foreground target is rejected before dispatch.

Package B checklist:

- [x] Landed `Win32InputService`, `IInputPlatform` and `Win32InputPlatform` as runtime-only slice.
- [x] Added translation-only `capture_pixels -> screen` mapping plus direct `screen` path with overflow-safe stale-geometry and out-of-bounds rejection.
- [x] Implemented click-first runtime subset only: `move`, `click`, `double_click`, `click(button=right)`.
- [x] Static subset exclusions that are already known from the request (`keys[]`, `click(button=middle)` and future click-first policy exclusions) are rejected in one pre-execution pass before any cursor move or click side effect can occur.
- [x] Reuse `ResolveInputTarget(...)` once at request start and `ResolveLiveWindowByIdentity(...)` before each action; no active fallback and no hidden focus/restore/attach. Stable-identity admission stays input-specific for paths that actually revalidate by identity; shared explicit `HWND` lookup used by focus/activate/capture remains a weaker live-window lookup and is not implicitly tightened by Package B.
- [x] Added target-specific preflight for foreground, minimized state, session match and equal-or-lower integrity unless current token has `uiAccess`; minimized now has explicit precedence over generic foreground loss.
- [x] Enforced fail-fast per-action factual results with cursor post-move verification, refreshed dispatch-plan revalidation (`screen` vs `capture_pixels`) and two explicit pointer boundaries: one before any `SetCursorPos` and one before button dispatch. Both boundaries prove the admitted target still owns the live foreground window in the same identity-resolution used at admission time; the move boundary blocks held ambient input before any cursor side effect, while the final click boundary additionally proves that the cursor still sits at the resolved point. The ambient-input proof is tri-state (`neutral` / `non-neutral` / `unknown`): Package B blocks pointer side effects when keyboard modifiers or mouse buttons are already held, and also fail-closes when the runtime cannot honestly prove that it is reading async input state from the active input desktop. Async-state mode selection is now derived from the same live foreground snapshot that passed the target-level proof, so same-process vs cross-process readability no longer rides on stale orchestration metadata. The same active-desktop async-state readability probe is now shared with guard/readiness logic, so `input=ready` no longer over-promises more than the live dispatch boundary can prove. Logical `button=left/right` semantics are preserved end-to-end even on swapped-button systems through one internal mapper reused by both ambient proof and Win32 dispatch. Partial `SendInput` insertion is treated as partial side effect, not a clean no-op: Package B performs best-effort button-up compensation and returns an honest failure classification if only part of the click sequence was inserted, and cancellation/reporting now follow the same factual model through one committed side-effect context plus explicit cancellation observation context: before the first side effect runtime still cancels by exception, but after any committed cursor move or tap Package B re-observes cancellation before the next irreversible step, including refreshed helper-path retarget moves, and before final success-return, materializing a partial `InputResult` from the last committed action owner and committed `resolved_screen_point` instead of mutable loop state. The loop now also has an explicit `enter action side-effect phase` boundary immediately before the first move of every shipped action, so `BetweenActions` cancellation cannot drift through setup and still start a new pointer side effect. The cancellation policy distinguishes in-flight action cancellation, between-actions cancellation and after-batch-completed-before-success-return cancellation, and the remaining exception-based observation at loop start is aligned with that same lifecycle model instead of forcing every late cancel into `InFlightAction`. `double_click` reporting comes from explicit irreversible phase state rather than boolean flags, so cancellation after the first tap and after the second tap materialize different factual reasons. `double_click` now refreshes plan only in a pre-gesture phase and then fails closed on both taps if boundary revalidation would require retargeting to a different screen point.
- [x] Registered internal-only `IInputService` / `IInputPlatform` in runtime DI; `WindowTools.cs`, deferred public registration, smoke and docs publication remain untouched.
- [x] L1 proof completed with targeted input-runtime tests, existing contract/guard regression floor and full `WinBridge.Runtime.Tests`.
- [x] Package B intentionally stops at runtime-only execution semantics; public handler, observability rollout and publication remain untouched.
- [ ] Public handler / `RunGatedAsync(...)` boundary remains Package C.
- [ ] Runtime event / artifact / materializer rollout remains Package D.
- [ ] Smoke / fresh-host acceptance / public publication remains Package E.

### Package C — Server/public tool

Scope:

- replace schema-preserving deferred registration with live public handler and implemented tool semantics;
- implement raw JSON bind, `AdditionalProperties` drift rejection and shipped-subset schema;
- route only through `RunGatedAsync(...)`;
- materialize `blocked` / `needs_confirmation` payloads in the same style as launch/open boundary;
- publish only the implemented subset in `tools/list` and `okno.contract`.

Done when:

- public schema is quiet and honest;
- no unsupported literal is advertised as shipped;
- `windows.input` becomes callable as MCP tool without bypassing shared gate.

### Package D — Observability

Scope:

- add `input.runtime.completed` runtime event;
- add JSON artifact family `artifacts/diagnostics/<run_id>/input/input-*.json`;
- capture safe fields only: action types, count, target source, coordinate space, resolved screen points, verification status, failure stage, artifact path;
- extend redaction tests for click metadata and reserve keyboard redaction work for later wave.

Done when:

- investigation path matches existing launch/open/wait conventions;
- artifact/event write failures remain best-effort and do not downcast factual runtime result.

Residual risk to carry from Package B:

- partial dispatch / dispatch evidence taxonomy should be reviewed explicitly before Package D materializes artifacts/events;
- current Package B already distinguishes clean failure vs partial side effect for click dispatch, but observability rollout must preserve that distinction instead of collapsing it into one generic `failed` evidence shape;
- if Package D introduces retries, compensation metadata or richer failure stages, it must do so on top of Package B committed-side-effect model rather than rebuilding side-effect ownership from top-level result only.

### Package E — Smoke/docs finalization

Scope:

- extend `scripts/smoke.ps1` with click-first proof on `SmokeWindowHost`;
- run sequential verify contour;
- add post-publication fresh-host acceptance: restart the MCP host and prove `windows.input` materializes in a new thread/session, not only in the current warm process;
- sync product/architecture/generated docs and changelog with actual shipped subset;
- only here lift roadmap row and final manifest publication flags.

Done when:

- `windows.input` passes L1/L2/L3;
- `tools/list`, `okno.contract`, roadmap/spec and generated docs all describe the same shipped subset;
- fresh-host acceptance proves the tool appears and binds correctly after restart, not only before process recycle;
- smoke proves no false success and no hidden focus side effects.

## 11. L1/L2/L3 test ladder

### L1. Runtime / contract tests

- request validation: non-empty actions, shipped subset literals, no `dryRun`, no extra fields, correct `confirm` routing
- target resolution: explicit vs attached, no active fallback
- coordinate mapping: `capture_pixels` happy path, stale geometry rejection, out-of-bounds rejection
- cursor dispatch: post-move `GetCursorPos` verification and failure mapping
- target preconditions: minimized, non-foreground, higher-integrity target, session mismatch
- batch semantics: ordered execution, stop on first failure, per-action result content
- result/status mapping: `verify_needed` vs `failed`
- guard readiness: `BuildInput(...)` ready/degraded/blocked/unknown matrix
- redaction: coordinate-only audit data stays visible; text-like fields remain suppressed

### L2. Server-side integration tests

- `tools/list` publishes `windows.input` with only shipped action subset in schema
- `okno.contract` exports implemented `windows.input` execution policy and no stale deferred metadata
- invalid transport payloads become tool-level `failed` results, not protocol errors
- gate boundary returns `blocked` and `needs_confirmation` payloads without calling allowed path
- live handler returns one `structuredContent` + one `TextContentBlock`
- `isError` mapping:
  - `blocked` / `needs_confirmation` / `failed` => `true`
  - `verify_needed` / `done` => `false`

### L3. Smoke

- launch or reuse existing helper from current smoke flow
- attach helper window
- explicit activation step via existing `windows.activate_window`
- `windows.capture(scope=window)` to get fresh `capture_pixels` basis
- `windows.uia_snapshot` to locate `Smoke query input` bounds for proof planning
- `windows.input` click center of the textbox in `capture_pixels`
- `windows.wait(condition=focus_is)` proves focus moved from button to edit
- artifact/event checks:
  - input artifact exists
  - `input.runtime.completed` exists when audit sink is healthy
  - no hidden activation/focus side effect is required inside `windows.input`
- post-publication host acceptance:
  - restart server / start fresh MCP session
  - verify `tools/list` advertises `windows.input`
  - verify `okno.contract` shows implemented `windows.input` with input execution policy
  - execute one narrow negative-path call on the fresh session to prove binding/result materialization without depending on warm in-process state

## 12. Smoke story

Canonical first smoke story should reuse current helper and stay as close as possible to real agent loop:

1. `windows.launch_process` or existing smoke helper launch creates `SmokeWindowHost`.
2. `windows.attach_window` claims the helper.
3. `windows.activate_window` makes it usable.
4. `windows.capture(scope=window)` returns fresh raster + bounds in `physical_pixels`.
5. `windows.uia_snapshot` confirms the helper tree and provides textbox bounds for `Smoke query input`.
6. `windows.input` receives:
   - `hwnd = helper`
   - `actions = [{ type = "click", coordinateSpace = "capture_pixels", point = center_of_textbox, captureReference = {...} }]`
7. Tool returns `verify_needed` with resolved screen point and artifact path.
8. `windows.wait(condition=focus_is, selector={ name="Smoke query input", controlType="edit" })` returns `done`.
9. Smoke asserts audit/event/artifact parity and that `windows.input` itself never tried hidden focus recovery.

Why this story is preferred:

- it proves real click value without inventing a new helper;
- it uses already shipped `capture + uia_snapshot + wait` floor;
- it avoids fake success based only on pointer dispatch.

## 13. Docs sync

When the slice is actually shipped, sync in the same cycle:

- this exec-plan
- [docs/architecture/observability.md](../../architecture/observability.md)
- [docs/architecture/openai-computer-use-interop.md](../../architecture/openai-computer-use-interop.md)
- [docs/product/okno-roadmap.md](../../product/okno-roadmap.md)
- [docs/product/okno-spec.md](../../product/okno-spec.md)
- [docs/product/okno-vision.md](../../product/okno-vision.md) if `right_click` wording is collapsed into `click(button=right)`
- [docs/generated/project-interfaces.md](../../generated/project-interfaces.md)
- [docs/generated/commands.md](../../generated/commands.md)
- [docs/generated/test-matrix.md](../../generated/test-matrix.md)
- `docs/generated/project-interfaces.json`
- `docs/bootstrap/bootstrap-status.json`
- [docs/CHANGELOG.md](../../CHANGELOG.md)

Rule:

- generated docs change only after factual build/test/smoke pass;
- no roadmap/spec claim may describe `drag`/`scroll`/`type`/`keypress` as shipped until their own runtime wave is proven.

## 14. Rollback / risk notes

- if shared input readiness cannot be refined honestly, keep `windows.input` deferred; do not ship a handler behind a permanently blocked capability summary.
- if `capture_pixels` remap proves unstable across resize/move/minimize drift, fail closed and require fresh capture rather than adding heuristics.
- if frontmost verification is flaky, keep `windows.input` strict and make `activate_window` the only explicit recovery step.
- if target-specific integrity probing cannot reliably distinguish equal/lower target, keep medium-integrity path at `needs_confirmation` + `failed` on ambiguity; do not silently attempt cross-IL click.
- if input artifact/event schema starts leaking too much geometry or future keyboard payload, prefer suppressing fields over inventing a second hidden redaction class.
- `window_client` is intentionally deferred. If a real consumer appears, the only acceptable follow-up is a narrow local experiment:
  - compute the same helper textbox click once from `capture_pixels`, once from candidate `window_client`;
  - resize/move the helper between capture and click;
  - compare drift and false-success behavior before exposing a third public coordinate space.
