using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using RuntimeToolExecution = WinBridge.Runtime.Diagnostics.ToolExecution;

namespace WinBridge.Server.Tools;

[McpServerToolType]
public sealed class WindowTools
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AuditLog _auditLog;
    private readonly ICaptureService _captureService;
    private readonly ISessionManager _sessionManager;
    private readonly IWindowManager _windowManager;

    public WindowTools(
        AuditLog auditLog,
        ISessionManager sessionManager,
        IWindowManager windowManager,
        ICaptureService captureService)
    {
        _auditLog = auditLog;
        _captureService = captureService;
        _sessionManager = sessionManager;
        _windowManager = windowManager;
    }

    [McpServerTool(Name = ToolNames.WindowsListWindows)]
    public ListWindowsResult ListWindows(bool includeInvisible = false)
        => RuntimeToolExecution.Run(
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
        => RuntimeToolExecution.Run(
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
        => RuntimeToolExecution.Run(
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

    [McpServerTool(
        Name = ToolNames.WindowsCapture,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    public Task<CallToolResult> Capture(string scope = "window", long? hwnd = null, CancellationToken cancellationToken = default) =>
        RuntimeToolExecution.RunAsync(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsCapture,
            new { scope, hwnd },
            async invocation =>
            {
                if (!CaptureScopeExtensions.TryParse(scope, out CaptureScope captureScope))
                {
                    string reason = $"Unsupported capture scope '{scope}'. Допустимые значения: window, desktop.";
                    invocation.Complete("failed", reason);
                    return CreateErrorResult(reason, scope, hwnd);
                }

                WindowDescriptor? window = ResolveCaptureWindow(captureScope, hwnd);
                if (hwnd is not null && window is null)
                {
                    string reason = "Окно для capture по указанному hwnd больше не найдено.";
                    invocation.Complete("failed", reason, hwnd);
                    return CreateErrorResult(reason, scope, hwnd);
                }

                if (captureScope == CaptureScope.Window && window is null)
                {
                    string reason = "Для window capture нужно передать hwnd или сначала прикрепить окно.";
                    invocation.Complete("failed", reason);
                    return CreateErrorResult(reason, scope, hwnd);
                }

                CaptureTarget target = new(captureScope, window);

                try
                {
                    CaptureResult result = await _captureService.CaptureAsync(target, cancellationToken).ConfigureAwait(false);
                    CaptureMetadata metadata = result.Metadata;

                    invocation.Complete(
                        "done",
                        metadata.TargetKind == "window" ? "Снимок окна получен." : "Снимок monitor получен.",
                        metadata.Hwnd,
                        new Dictionary<string, string?>
                        {
                            ["scope"] = metadata.Scope,
                            ["target_kind"] = metadata.TargetKind,
                            ["artifact_path"] = metadata.ArtifactPath,
                            ["mime_type"] = metadata.MimeType,
                            ["pixel_width"] = metadata.PixelWidth.ToString(CultureInfo.InvariantCulture),
                            ["pixel_height"] = metadata.PixelHeight.ToString(CultureInfo.InvariantCulture),
                            ["byte_size"] = metadata.ByteSize.ToString(CultureInfo.InvariantCulture),
                        });

                    return CreateSuccessResult(result);
                }
                catch (CaptureOperationException exception)
                {
                    invocation.Complete("failed", exception.Message, window?.Hwnd);
                    return CreateErrorResult(exception.Message, scope, hwnd ?? window?.Hwnd);
                }
            });

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
        => RuntimeToolExecution.Run(
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

    private WindowDescriptor? ResolveCaptureWindow(CaptureScope scope, long? hwnd)
    {
        if (hwnd is long explicitHwnd)
        {
            return _windowManager.ListWindows(includeInvisible: true)
                .FirstOrDefault(candidate => candidate.Hwnd == explicitHwnd);
        }

        WindowDescriptor? attachedWindow = _sessionManager.GetAttachedWindow()?.Window;
        if (attachedWindow is null)
        {
            return null;
        }

        IReadOnlyList<WindowDescriptor> liveWindows = _windowManager.ListWindows(includeInvisible: true);
        return TryResolveLiveAttachedWindow(attachedWindow, liveWindows);
    }

    private static WindowDescriptor? TryResolveLiveAttachedWindow(
        WindowDescriptor attachedWindow,
        IReadOnlyList<WindowDescriptor> liveWindows)
    {
        WindowDescriptor? liveCandidate = liveWindows
            .FirstOrDefault(candidate => candidate.Hwnd == attachedWindow.Hwnd);
        if (liveCandidate is null)
        {
            return null;
        }

        return MatchesAttachedWindowIdentity(liveCandidate, attachedWindow) ? liveCandidate : null;
    }

    private static bool MatchesAttachedWindowIdentity(
        WindowDescriptor liveCandidate,
        WindowDescriptor attachedWindow)
    {
        bool processIdCompatible = liveCandidate.ProcessId is null
            || attachedWindow.ProcessId is null
            || liveCandidate.ProcessId == attachedWindow.ProcessId;
        bool threadIdCompatible = liveCandidate.ThreadId is null
            || attachedWindow.ThreadId is null
            || liveCandidate.ThreadId == attachedWindow.ThreadId;
        bool classNameCompatible = string.IsNullOrWhiteSpace(liveCandidate.ClassName)
            || string.IsNullOrWhiteSpace(attachedWindow.ClassName)
            || string.Equals(liveCandidate.ClassName, attachedWindow.ClassName, StringComparison.Ordinal);

        return processIdCompatible && threadIdCompatible && classNameCompatible;
    }

    private static CallToolResult CreateSuccessResult(CaptureResult result)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(result.Metadata, PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = false,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(result.Metadata, PayloadJsonOptions),
                },
                new ImageContentBlock
                {
                    Data = Encoding.ASCII.GetBytes(Convert.ToBase64String(result.PngBytes)),
                    MimeType = result.Metadata.MimeType,
                },
            ],
        };
    }

    private static CallToolResult CreateErrorResult(string reason, string scope, long? hwnd)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            new
            {
                status = "failed",
                reason,
                scope,
                hwnd,
            },
            PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = true,
            StructuredContent = payload,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, PayloadJsonOptions),
                },
            ],
        };
    }
}
