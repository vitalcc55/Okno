namespace WinBridge.Runtime.Tooling;

public static class ToolContractManifest
{
    public static string ContractNotes { get; } =
        "Okno bootstrap runtime экспортирует безопасный window/session slice и честные deferred tools.";

    public static IReadOnlyList<ToolDescriptor> All { get; } =
        new[]
        {
            new ToolDescriptor(ToolNames.OknoHealth, "okno.admin", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, "Возвращает сводку состояния runtime и артефактов.", null, null, true),
            new ToolDescriptor(ToolNames.OknoContract, "okno.admin", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, "Возвращает текущий tool contract runtime.", null, null, false),
            new ToolDescriptor(ToolNames.OknoSessionState, "okno.session", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, "Возвращает текущий session snapshot.", null, null, true),
            new ToolDescriptor(ToolNames.WindowsListWindows, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.ReadOnly, "Перечисляет top-level окна Windows.", null, null, true),
            new ToolDescriptor(ToolNames.WindowsAttachWindow, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.SessionMutation, "Прикрепляет текущую сессию к выбранному окну.", null, null, true),
            new ToolDescriptor(ToolNames.WindowsFocusWindow, "windows.shell", ToolLifecycle.Implemented, ToolSafetyClass.OsSideEffect, "Пытается перевести окно в foreground.", null, null, false),
            new ToolDescriptor(ToolNames.WindowsCapture, "windows.capture", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Снимает desktop или window capture.", "roadmap stage 3", "В bootstrap используй list/attach/focus вместо capture.", false),
            new ToolDescriptor(ToolNames.WindowsClipboardGet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Читает текущее содержимое clipboard.", "roadmap stage 4", "Clipboard path будет добавлен после skeleton runtime.", false),
            new ToolDescriptor(ToolNames.WindowsClipboardSet, "windows.clipboard", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Записывает новое содержимое в clipboard.", "roadmap stage 4", "До clipboard-сервиса используй безопасные stub calls.", false),
            new ToolDescriptor(ToolNames.WindowsInput, "windows.input", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет низкоуровневую последовательность input-действий.", "roadmap stage 5", "Low-level input вводится только после capture/text path.", false),
            new ToolDescriptor(ToolNames.WindowsUiaSnapshot, "windows.uia", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Возвращает UIA snapshot выбранного окна.", "roadmap stage 6", "UIA snapshot ещё не подключён в bootstrap slice.", false),
            new ToolDescriptor(ToolNames.WindowsUiaAction, "windows.uia", ToolLifecycle.Deferred, ToolSafetyClass.OsSideEffect, "Выполняет semantic UIA action по element id.", "roadmap stage 7", "Semantic UIA actions запланированы после snapshot layer.", false),
            new ToolDescriptor(ToolNames.WindowsWait, "windows.wait", ToolLifecycle.Deferred, ToolSafetyClass.ReadOnly, "Ждёт выполнения условия и верифицирует изменение состояния.", "roadmap stage 8", "Wait/verify contract будет реализован отдельным сервисом.", false),
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
