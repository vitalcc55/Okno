using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Tooling;

public static class ToolContractManifest
{
    private static readonly Dictionary<string, ToolDescriptor> AllByName;

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

    public static string ContractNotes { get; } =
        "Okno bootstrap runtime экспортирует observe/window slice, public okno.health readiness summary, public windows.uia_snapshot, public windows.wait, public windows.launch_process и честные deferred action tools без hidden enforcement; okno.contract публикует execution_policy metadata для policy-bearing public tools и declared deferred tools.";

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
            new ToolDescriptor(ToolNames.WindowsUiaSnapshot, "windows.uia", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.WindowsUiaSnapshotTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsWait, "windows.wait", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.WindowsWaitTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsClipboardGet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Читает текущее содержимое clipboard.", "roadmap stage 4", "Clipboard path будет добавлен после skeleton runtime.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.Clipboard, ToolExecutionRiskLevel.Medium, CapabilitySummaryValues.Clipboard, supportsDryRun: false, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.ClipboardPayload)),
            new ToolDescriptor(ToolNames.WindowsClipboardSet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Записывает новое содержимое в clipboard.", "roadmap stage 4", "До clipboard-сервиса используй безопасные stub calls.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.Clipboard, ToolExecutionRiskLevel.High, CapabilitySummaryValues.Clipboard, supportsDryRun: true, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.ClipboardPayload)),
            new ToolDescriptor(ToolNames.WindowsInput, "windows.input", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет низкоуровневую последовательность input-действий.", "roadmap stage 5", "Low-level input вводится только после capture/text path.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.Input, ToolExecutionRiskLevel.Destructive, CapabilitySummaryValues.Input, supportsDryRun: false, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.TextPayload)),
            new ToolDescriptor(ToolNames.WindowsUiaAction, "windows.uia", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет semantic UIA action по element id.", "roadmap stage 7", "Semantic UIA actions запланированы после snapshot layer.", false, CreateExecutionPolicy(ToolExecutionPolicyGroup.UiaAction, ToolExecutionRiskLevel.High, CapabilitySummaryValues.Uia, supportsDryRun: false, ToolExecutionConfirmationMode.Required, ToolExecutionRedactionClass.TargetMetadata)),
        };

    static ToolContractManifest()
    {
        AllByName = All.ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);
    }

    internal static IReadOnlyDictionary<string, ToolExecutionPolicyDescriptor> FutureLaunchFamilyPolicyPresets { get; } =
        new Dictionary<string, ToolExecutionPolicyDescriptor>(StringComparer.Ordinal)
        {
            [ToolNames.WindowsOpenTarget] = FutureOpenTargetDescriptor.ExecutionPolicy
                ?? throw new InvalidOperationException("Execution policy for FutureOpenTargetDescriptor must be configured."),
        };

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
