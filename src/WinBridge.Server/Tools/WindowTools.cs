using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.Tools;

[McpServerToolType]
public sealed class WindowTools
{
    private readonly AuditLog _auditLog;
    private readonly ISessionManager _sessionManager;
    private readonly IWindowManager _windowManager;

    public WindowTools(AuditLog auditLog, ISessionManager sessionManager, IWindowManager windowManager)
    {
        _auditLog = auditLog;
        _sessionManager = sessionManager;
        _windowManager = windowManager;
    }

    [McpServerTool(Name = ToolNames.WindowsListWindows)]
    public ListWindowsResult ListWindows(bool includeInvisible = false)
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsListWindows,
            new { includeInvisible },
            invocation =>
            {
                IReadOnlyList<WindowDescriptor> windows = _windowManager.ListWindows(includeInvisible);
                ListWindowsResult result = new(windows, windows.Count, _sessionManager.GetSnapshot());

                invocation.Complete(
                    "done",
                    $"Найдено {windows.Count} top-level окон.",
                    data: new Dictionary<string, string?>
                    {
                        ["count"] = windows.Count.ToString(CultureInfo.InvariantCulture),
                        ["include_invisible"] = includeInvisible.ToString(CultureInfo.InvariantCulture),
                    });

                return result;
            });

    [McpServerTool(Name = ToolNames.WindowsAttachWindow)]
    public AttachWindowResult AttachWindow(long? hwnd = null, string? titlePattern = null, string? processName = null)
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsAttachWindow,
            new { hwnd, titlePattern, processName },
            invocation =>
            {
                WindowSelector selector = new(hwnd, titlePattern, processName);
                try
                {
                    WindowDescriptor? window = _windowManager.FindWindow(selector);
                    if (window is null)
                    {
                        AttachWindowResult notFound = new(
                            Status: "failed",
                            Reason: "Окно по заданному селектору не найдено.",
                            AttachedWindow: null,
                            Session: _sessionManager.GetSnapshot());

                        invocation.Complete("failed", notFound.Reason!);
                        return notFound;
                    }

                    SessionMutation mutation = _sessionManager.Attach(window, selector.MatchStrategy);
                    _auditLog.RecordSessionAttached(mutation.Before, mutation.After);

                    string status = mutation.Changed ? "done" : "already_attached";
                    string message = mutation.Changed
                        ? "Окно прикреплено к текущей сессии."
                        : "Указанное окно уже было прикреплено к текущей сессии.";

                    AttachWindowResult result = new(
                        Status: status,
                        Reason: mutation.Changed ? null : message,
                        AttachedWindow: mutation.After.AttachedWindow,
                        Session: mutation.After);

                    invocation.Complete(status, message, window.Hwnd);
                    return result;
                }
                catch (RegexMatchTimeoutException)
                {
                    AttachWindowResult timedOut = new(
                        Status: "failed",
                        Reason: "Селектор titlePattern превысил допустимое время сопоставления.",
                        AttachedWindow: null,
                        Session: _sessionManager.GetSnapshot());

                    invocation.Complete("failed", timedOut.Reason!);
                    return timedOut;
                }
                catch (InvalidOperationException exception)
                {
                    AttachWindowResult ambiguous = new(
                        Status: "ambiguous",
                        Reason: exception.Message,
                        AttachedWindow: null,
                        Session: _sessionManager.GetSnapshot());

                    invocation.Complete("ambiguous", exception.Message);
                    return ambiguous;
                }
            });

    [McpServerTool(Name = ToolNames.WindowsFocusWindow)]
    public FocusWindowResult FocusWindow(long? hwnd = null)
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsFocusWindow,
            new { hwnd },
            invocation =>
            {
                long? targetHwnd = hwnd ?? _sessionManager.GetAttachedWindow()?.Window.Hwnd;
                if (targetHwnd is null)
                {
                    FocusWindowResult missingTarget = new(
                        Status: "failed",
                        Reason: "Для фокуса нужно передать hwnd или сначала прикрепить окно.",
                        Window: null);

                    invocation.Complete("failed", missingTarget.Reason!);
                    return missingTarget;
                }

                IReadOnlyList<WindowDescriptor> windows = _windowManager.ListWindows(includeInvisible: true);
                WindowDescriptor? window = windows.FirstOrDefault(candidate => candidate.Hwnd == targetHwnd.Value);
                if (window is null)
                {
                    FocusWindowResult missingWindow = new(
                        Status: "failed",
                        Reason: "Окно для фокуса больше не найдено.",
                        Window: null);

                    invocation.Complete("failed", missingWindow.Reason!, targetHwnd.Value);
                    return missingWindow;
                }

                bool focused = _windowManager.TryFocus(targetHwnd.Value);
                FocusWindowResult result = new(
                    Status: focused ? "done" : "failed",
                    Reason: focused ? null : "Windows отказалась перевести окно в foreground.",
                    Window: window);

                invocation.Complete(
                    result.Status,
                    focused ? "Запрошен foreground focus для окна." : result.Reason!,
                    targetHwnd.Value);

                return result;
            });

    [McpServerTool(Name = ToolNames.WindowsCapture)]
    public DeferredToolResult Capture(string scope = "window", bool includeUiaSummary = false) =>
        Deferred(ToolNames.WindowsCapture);

    [McpServerTool(Name = ToolNames.WindowsClipboardGet)]
    public DeferredToolResult ClipboardGet() =>
        Deferred(ToolNames.WindowsClipboardGet);

    [McpServerTool(Name = ToolNames.WindowsClipboardSet)]
    public DeferredToolResult ClipboardSet(string value) =>
        Deferred(ToolNames.WindowsClipboardSet);

    [McpServerTool(Name = ToolNames.WindowsInput)]
    public DeferredToolResult Input(string actionsJson = "[]") =>
        Deferred(ToolNames.WindowsInput);

    [McpServerTool(Name = ToolNames.WindowsUiaSnapshot)]
    public DeferredToolResult UiaSnapshot(int depth = 3, string? filtersJson = null) =>
        Deferred(ToolNames.WindowsUiaSnapshot);

    [McpServerTool(Name = ToolNames.WindowsUiaAction)]
    public DeferredToolResult UiaAction(string elementId, string action, string? value = null) =>
        Deferred(ToolNames.WindowsUiaAction);

    [McpServerTool(Name = ToolNames.WindowsWait)]
    public DeferredToolResult Wait(string until, string? selector = null, int timeoutMs = 3000) =>
        Deferred(ToolNames.WindowsWait);

    private DeferredToolResult Deferred(string toolName)
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            toolName,
            new { deferred = true },
            invocation =>
            {
                ToolDescriptor descriptor = ToolContractManifest.Deferred.Single(item => item.Name == toolName);
                DeferredToolResult result = new(
                    ToolName: descriptor.Name,
                    Status: "unsupported",
                    Reason: "Инструмент задекларирован в контракте, но ещё не реализован в bootstrap vertical slice.",
                    PlannedPhase: descriptor.PlannedPhase!,
                    SuggestedAlternative: descriptor.SuggestedAlternative!);

                invocation.Complete("unsupported", result.Reason);
                return result;
            });
}
