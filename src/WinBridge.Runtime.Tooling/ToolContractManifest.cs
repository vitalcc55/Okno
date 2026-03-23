namespace WinBridge.Runtime.Tooling;

public static class ToolContractManifest
{
    public static string ContractNotes { get; } =
        "Okno bootstrap runtime экспортирует observe/window slice, public windows.uia_snapshot, public windows.wait и честные deferred action tools.";

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
            new ToolDescriptor(ToolNames.WindowsUiaSnapshot, "windows.uia", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, ToolDescriptions.WindowsUiaSnapshotTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsWait, "windows.wait", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, ToolDescriptions.WindowsWaitTool, null, null, true),
            new ToolDescriptor(ToolNames.WindowsClipboardGet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Читает текущее содержимое clipboard.", "roadmap stage 4", "Clipboard path будет добавлен после skeleton runtime.", false),
            new ToolDescriptor(ToolNames.WindowsClipboardSet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Записывает новое содержимое в clipboard.", "roadmap stage 4", "До clipboard-сервиса используй безопасные stub calls.", false),
            new ToolDescriptor(ToolNames.WindowsInput, "windows.input", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет низкоуровневую последовательность input-действий.", "roadmap stage 5", "Low-level input вводится только после capture/text path.", false),
            new ToolDescriptor(ToolNames.WindowsUiaAction, "windows.uia", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет semantic UIA action по element id.", "roadmap stage 7", "Semantic UIA actions запланированы после snapshot layer.", false),
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
}
