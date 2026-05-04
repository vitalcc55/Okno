// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Tooling;

public static class ToolContractManifest
{
    private static readonly Dictionary<string, ToolDescriptor> AllByName;
    private static readonly ToolContractProfile WindowsEngineProfile;
    private static readonly ToolContractProfile ComputerUseWinProfile;

    internal static ToolDescriptor FutureLaunchProcessDescriptor { get; } =
        new(
            ToolNames.WindowsLaunchProcess,
            "windows.launch",
            ToolLifecycle.Implemented,
            ToolSafetyClass.OsSideEffect,
            ToolDescriptions.WindowsLaunchProcessTool,
            null,
            null,
            true,
            CreateExecutionPolicy(
                ToolExecutionPolicyGroup.Launch,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Launch,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required,
                ToolExecutionRedactionClass.LaunchPayload));

    internal static ToolDescriptor FutureOpenTargetDescriptor { get; } =
        new(
            ToolNames.WindowsOpenTarget,
            "windows.launch",
            ToolLifecycle.Implemented,
            ToolSafetyClass.OsSideEffect,
            ToolDescriptions.WindowsOpenTargetTool,
            null,
            null,
            true,
            CreateExecutionPolicy(
                ToolExecutionPolicyGroup.Launch,
                ToolExecutionRiskLevel.Medium,
                CapabilitySummaryValues.Launch,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required,
                ToolExecutionRedactionClass.LaunchPayload));

    internal static ToolDescriptor FutureInputDescriptor { get; } =
        new(
            ToolNames.WindowsInput,
            "windows.input",
            ToolLifecycle.Implemented,
            ToolSafetyClass.OsSideEffect,
            ToolDescriptions.WindowsInputTool,
            null,
            null,
            true,
            CreateExecutionPolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required,
                ToolExecutionRedactionClass.TextPayload));

    public static string ContractNotes { get; } =
        "Okno bootstrap runtime экспортирует observe/window slice, public okno.health readiness summary, public windows.uia_snapshot, public windows.wait, public windows.launch_process, public windows.open_target и public click-first `windows.input` boundary без hidden enforcement. Для `windows.input` сейчас опубликован только implemented subset `move`, `click`, `double_click`, `click(button=right)`; artifacts/events/materializer уже закрыты Package D через `input.runtime.completed` и `artifacts/diagnostics/<run_id>/input/input-*.json`, а smoke/fresh-host acceptance закрыты Package E через real helper click proof и fresh staged host binding proof.";

    public static string ComputerUseWinContractNotes { get; } =
        "Public product profile `computer-use-win` публикует vendor-like operator surface поверх внутреннего Okno engine. В текущем shipped subset в public profile входят `list_apps`, `get_app_state`, `click`, `press_key`, `set_value`, `type_text`, `scroll`, `perform_secondary_action` и `drag`; `list_apps` группирует visible window instances по app-level approval identity, публикует nested `windows[]` и выдаёт runtime-owned selectable `windowId`, а `get_app_state` таргетирует конкретный instance через `windowId` или explicit `hwnd`. `type_text` по умолчанию остаётся focused-editable input path без clipboard-default и без hidden focus guessing; explicit `allowFocusedFallback=true` требует `confirm=true`, допускает target-local focused poor-UIA fallback с fresh focus proof или coordinate-confirmed fallback через explicit `point` в `coordinateSpace=capture_pixels` из последнего app state, dispatch-ит coordinate click + text в одном SendInput batch и сохраняет dispatch-only success как `verify_needed`. `click`, `press_key`, `type_text`, `scroll` и `drag` поддерживают explicit `observeAfter=true`: после committed `done`/`verify_needed` action runtime best-effort materialize-ит nested `successorState` с новым short-lived `stateToken` и image content block; top-level action status остаётся factual, successful successor state делает `refreshStateRecommended=false`, а failed successor observe публикуется как advisory `successorStateFailure` без переписывания action outcome. `set_value` сохраняется как preferred semantic path для settable controls, `scroll` предпочитает semantic UIA `ScrollPattern` path и оставляет coordinate wheel только как explicit confirmed fallback, `perform_secondary_action` v1 остаётся semantic-only path для strong UIA secondary affordance `toggle` без context-menu/right-click fallback, а `drag` требует отдельный source/destination proof и по умолчанию завершает generic path как `verify_needed`, а не optimistic `done`. Низкоуровневые `windows.*` tools остаются внутренним execution substrate и не являются главным product-facing Codex UX.";

    public static IReadOnlyList<ToolDescriptor> All { get; } =
        new[]
        {
            new ToolDescriptor(ToolNames.OknoHealth, "okno.admin", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.OknoHealthTool, null, null, true),
            new ToolDescriptor(ToolNames.OknoContract, "okno.admin", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.OknoContractTool, null, null, false),
            new ToolDescriptor(ToolNames.OknoSessionState, "okno.session", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.OknoSessionStateTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsListMonitors, "windows.display", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.WindowsListMonitorsTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsListWindows, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.WindowsListWindowsTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsAttachWindow, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.SessionMutation, ToolDescriptions.WindowsAttachWindowTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsActivateWindow, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.WindowsActivateWindowTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsFocusWindow, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.WindowsFocusWindowTool, null, null, false),
            new ToolDescriptor(ToolNames.WindowsCapture, "windows.capture", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.WindowsCaptureTool, null, null, true),
            FutureLaunchProcessDescriptor,
            FutureOpenTargetDescriptor,
            FutureInputDescriptor,
            new ToolDescriptor(ToolNames.WindowsUiaSnapshot, "windows.uia", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.WindowsUiaSnapshotTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsWait, "windows.wait", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.WindowsWaitTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsClipboardGet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Читает текущее содержимое clipboard.", "roadmap stage 4", "Clipboard path будет добавлен после skeleton runtime.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.Clipboard, ToolExecutionRiskLevel.Medium, CapabilitySummaryValues.Clipboard, supportsDryRun: false, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.ClipboardPayload)),
            new ToolDescriptor(ToolNames.WindowsClipboardSet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Записывает новое содержимое в clipboard.", "roadmap stage 4", "До clipboard-сервиса используй безопасные stub calls.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.Clipboard, ToolExecutionRiskLevel.High, CapabilitySummaryValues.Clipboard, supportsDryRun: true, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.ClipboardPayload)),
            new ToolDescriptor(ToolNames.WindowsUiaAction, "windows.uia", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет semantic UIA action по element id.", "roadmap stage 7", "Semantic UIA actions запланированы после snapshot layer.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.UiaAction, ToolExecutionRiskLevel.High, CapabilitySummaryValues.Uia, supportsDryRun: false, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.TargetMetadata)),
        };

    static ToolContractManifest()
    {
        AllByName = All.ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);
        WindowsEngineProfile = new(
            ToolSurfaceProfileValues.WindowsEngine,
            Implemented,
            Deferred,
            ImplementedNames,
            SmokeRequiredToolNames,
            DeferredPhaseMap,
            ContractNotes);
        ComputerUseWinProfile = CreateComputerUseWinProfile();
    }

    internal static IReadOnlyDictionary<string, ToolExecutionPolicyDescriptor> FutureLaunchFamilyPolicyPresets { get; } =
        new Dictionary<string, ToolExecutionPolicyDescriptor>(StringComparer.Ordinal);

    public static IReadOnlyList<ToolDescriptor> Implemented { get; } =
        All.Where(descriptor => descriptor.Lifecycle == ToolLifecycle.Implemented).ToArray();

    public static IReadOnlyList<ToolDescriptor> Deferred { get; } =
        All.Where(descriptor => descriptor.Lifecycle == ToolLifecycle.Deferred).ToArray();

    public static IReadOnlyList<string> ImplementedNames { get; } =
        Implemented.Select(descriptor => descriptor.Name).ToArray();

    public static IReadOnlyList<string> SmokeRequiredToolNames { get; } =
        Implemented.Where(descriptor => descriptor.SmokeRequired).Select(descriptor => descriptor.Name).ToArray();

    public static IReadOnlyDictionary<string, string> DeferredPhaseMap { get; } =
        Deferred.ToDictionary(descriptor => descriptor.Name, descriptor => descriptor.PlannedPhase!, StringComparer.Ordinal);

    public static ToolExecutionPolicyDescriptor? ResolveExecutionPolicy(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (AllByName.TryGetValue(toolName, out ToolDescriptor? descriptor))
        {
            return descriptor.ExecutionPolicy;
        }

        return FutureLaunchFamilyPolicyPresets.TryGetValue(toolName, out ToolExecutionPolicyDescriptor? preset)
            ? preset
            : null;
    }

    public static ToolContractProfile GetProfile(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)
            || string.Equals(profileName, ToolSurfaceProfileValues.WindowsEngine, StringComparison.Ordinal))
        {
            return WindowsEngineProfile;
        }

        if (string.Equals(profileName, ToolSurfaceProfileValues.ComputerUseWin, StringComparison.Ordinal))
        {
            return ComputerUseWinProfile;
        }

        throw new ArgumentOutOfRangeException(nameof(profileName), profileName, "Неизвестный tool surface profile.");
    }

    private static ToolContractProfile CreateComputerUseWinProfile()
    {
        ToolDescriptor[] implemented =
        [
            new(ToolNames.ComputerUseWinListApps, "computer_use_win.apps", ToolLifecycle.Implemented, ToolSafetyClass.SessionMutation, ToolDescriptions.ComputerUseWinListAppsTool, null, null, true),
            new(ToolNames.ComputerUseWinGetAppState, "computer_use_win.state", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinGetAppStateTool, null, null, true),
            new(ToolNames.ComputerUseWinClick, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinClickTool, null, null, true),
            new(ToolNames.ComputerUseWinPressKey, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinPressKeyTool, null, null, true),
            new(ToolNames.ComputerUseWinSetValue, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinSetValueTool, null, null, true),
            new(ToolNames.ComputerUseWinTypeText, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinTypeTextTool, null, null, true),
            new(ToolNames.ComputerUseWinScroll, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinScrollTool, null, null, true),
            new(ToolNames.ComputerUseWinPerformSecondaryAction, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinPerformSecondaryActionTool, null, null, true),
            new(ToolNames.ComputerUseWinDrag, "computer_use_win.action", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.ComputerUseWinDragTool, null, null, true),
        ];
        ToolDescriptor[] deferred = [];

        return new(
            ToolSurfaceProfileValues.ComputerUseWin,
            implemented,
            deferred,
            implemented.Select(static descriptor => descriptor.Name).ToArray(),
            implemented.Where(static descriptor => descriptor.SmokeRequired).Select(static descriptor => descriptor.Name).ToArray(),
            deferred.ToDictionary(static descriptor => descriptor.Name, static descriptor => descriptor.PlannedPhase!, StringComparer.Ordinal),
            ComputerUseWinContractNotes);
    }

    private static ToolExecutionPolicyDescriptor CreateExecutionPolicy(
        ToolExecutionPolicyGroup policyGroup,
        ToolExecutionRiskLevel riskLevel,
        string guardCapability,
        bool supportsDryRun,
        ToolExecutionConfirmationMode confirmationMode,
        ToolExecutionRedactionClass redactionClass) =>
        new(
            PolicyGroup: policyGroup,
            RiskLevel: riskLevel,
            GuardCapability: guardCapability,
            SupportsDryRun: supportsDryRun,
            ConfirmationMode: confirmationMode,
            RedactionClass: redactionClass);
}
