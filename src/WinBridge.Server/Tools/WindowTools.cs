using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;
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
    private readonly IMonitorManager _monitorManager;
    private readonly ISessionManager _sessionManager;
    private readonly IUiAutomationService _uiAutomationService;
    private readonly IWindowActivationService _windowActivationService;
    private readonly IWindowManager _windowManager;
    private readonly IWindowTargetResolver _windowTargetResolver;

    public WindowTools(
        AuditLog auditLog,
        ISessionManager sessionManager,
        IWindowManager windowManager,
        ICaptureService captureService,
        IMonitorManager monitorManager,
        IWindowActivationService windowActivationService,
        IWindowTargetResolver windowTargetResolver,
        IUiAutomationService uiAutomationService)
    {
        _auditLog = auditLog;
        _captureService = captureService;
        _monitorManager = monitorManager;
        _sessionManager = sessionManager;
        _uiAutomationService = uiAutomationService;
        _windowActivationService = windowActivationService;
        _windowManager = windowManager;
        _windowTargetResolver = windowTargetResolver;
    }

    [Description(ToolDescriptions.WindowsListMonitorsTool)]
    [McpServerTool(
        Name = ToolNames.WindowsListMonitors,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true)]
    public ListMonitorsResult ListMonitors()
        => RuntimeToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsListMonitors,
            new { },
            invocation =>
            {
                DisplayTopologySnapshot topology = _monitorManager.GetTopologySnapshot();
                MonitorDescriptor[] monitors = topology.Monitors
                    .Select(item => item.Descriptor)
                    .ToArray();
                ListMonitorsResult result = new(monitors, monitors.Length, topology.Diagnostics, _sessionManager.GetSnapshot());

                invocation.Complete(
                    "done",
                    $"Найдено {monitors.Length} активных monitor targets.",
                    data: new Dictionary<string, string?>
                    {
                        ["count"] = monitors.Length.ToString(CultureInfo.InvariantCulture),
                        ["identity_mode"] = topology.Diagnostics.IdentityMode,
                    });

                return result;
            });

    [Description(ToolDescriptions.WindowsListWindowsTool)]
    [McpServerTool(Name = ToolNames.WindowsListWindows)]
    public ListWindowsResult ListWindows(
        [Description(ToolDescriptions.IncludeInvisibleParameter)]
        bool includeInvisible = false)
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

    [Description(ToolDescriptions.WindowsAttachWindowTool)]
    [McpServerTool(Name = ToolNames.WindowsAttachWindow)]
    public AttachWindowResult AttachWindow(
        [Description(ToolDescriptions.HwndParameter)]
        long? hwnd = null,
        [Description(ToolDescriptions.TitlePatternParameter)]
        string? titlePattern = null,
        [Description(ToolDescriptions.ProcessNameParameter)]
        string? processName = null)
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

                    if (!WindowIdentityValidator.TryValidateStableIdentity(window, out string? reason))
                    {
                        AttachWindowResult weakIdentity = new(
                            Status: "failed",
                            Reason: reason,
                            AttachedWindow: null,
                            Session: _sessionManager.GetSnapshot());

                        invocation.Complete("failed", reason!, window.Hwnd);
                        return weakIdentity;
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
                catch (ArgumentException exception)
                {
                    AttachWindowResult invalidSelector = new(
                        Status: "failed",
                        Reason: exception.Message,
                        AttachedWindow: null,
                        Session: _sessionManager.GetSnapshot());

                    invocation.Complete("failed", exception.Message);
                    return invalidSelector;
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

    [Description(ToolDescriptions.WindowsActivateWindowTool)]
    [McpServerTool(
        Name = ToolNames.WindowsActivateWindow,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    public Task<CallToolResult> ActivateWindow(CancellationToken cancellationToken = default) =>
        RuntimeToolExecution.RunAsync(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsActivateWindow,
            new { },
            async invocation =>
            {
                WindowDescriptor? attachedWindow = _sessionManager.GetAttachedWindow()?.Window;
                if (attachedWindow is null)
                {
                    ActivateWindowResult missingTarget = new(
                        Status: "failed",
                        Reason: "Для активации сначала прикрепи окно через windows.attach_window.",
                        Window: null,
                        WasMinimized: false,
                        IsForeground: false);

                    invocation.Complete("failed", missingTarget.Reason!);
                    return CreateToolResult(missingTarget, isError: true);
                }

                WindowDescriptor? targetWindow = _windowTargetResolver.ResolveExplicitOrAttachedWindow(null, attachedWindow);
                if (targetWindow is null)
                {
                    ActivateWindowResult missingWindow = new(
                        Status: "failed",
                        Reason: "Прикрепленное окно больше не найдено или больше не совпадает с live target.",
                        Window: null,
                        WasMinimized: false,
                        IsForeground: false);

                    invocation.Complete("failed", missingWindow.Reason!, attachedWindow?.Hwnd);
                    return CreateToolResult(missingWindow, isError: true);
                }

                ActivateWindowResult result = await _windowActivationService
                    .ActivateAsync(targetWindow, cancellationToken)
                    .ConfigureAwait(false);

                invocation.Complete(
                    result.Status,
                    result.Status == "done" ? "Окно активировано и готово к работе." : result.Reason!,
                    targetWindow.Hwnd,
                    new Dictionary<string, string?>
                    {
                        ["was_minimized"] = result.WasMinimized.ToString(CultureInfo.InvariantCulture),
                        ["is_foreground"] = result.IsForeground.ToString(CultureInfo.InvariantCulture),
                    });

                return CreateToolResult(result, isError: ActivateStatusIsToolError(result.Status));
            });

    [Description(ToolDescriptions.WindowsFocusWindowTool)]
    [McpServerTool(Name = ToolNames.WindowsFocusWindow)]
    public FocusWindowResult FocusWindow(
        [Description(ToolDescriptions.FocusHwndParameter)]
        long? hwnd = null)
        => RuntimeToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsFocusWindow,
            new { hwnd },
            invocation =>
            {
                WindowDescriptor? attachedWindow = _sessionManager.GetAttachedWindow()?.Window;
                if (hwnd is null && attachedWindow is null)
                {
                    FocusWindowResult missingTarget = new(
                        Status: "failed",
                        Reason: "Для фокуса нужно передать hwnd или сначала прикрепить окно.",
                        Window: null);

                    invocation.Complete("failed", missingTarget.Reason!);
                    return missingTarget;
                }

                WindowDescriptor? window = _windowTargetResolver.ResolveExplicitOrAttachedWindow(hwnd, attachedWindow);
                if (window is null)
                {
                    FocusWindowResult missingWindow = new(
                        Status: "failed",
                        Reason: hwnd is not null
                            ? "Окно для фокуса больше не найдено."
                            : "Прикрепленное окно больше не найдено или больше не совпадает с live target.",
                        Window: null);

                    invocation.Complete("failed", missingWindow.Reason!, hwnd ?? attachedWindow?.Hwnd);
                    return missingWindow;
                }

                bool focused = _windowManager.TryFocus(window.Hwnd);
                FocusWindowResult result = new(
                    Status: focused ? "done" : "failed",
                    Reason: focused ? null : "Windows отказалась перевести окно в foreground.",
                    Window: window);

                invocation.Complete(
                    result.Status,
                    focused ? "Запрошен foreground focus для окна." : result.Reason!,
                    window.Hwnd);

                return result;
            });

    [Description(ToolDescriptions.WindowsCaptureTool)]
    [McpServerTool(
        Name = ToolNames.WindowsCapture,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    public Task<CallToolResult> Capture(
        [Description(ToolDescriptions.CaptureScopeParameter)]
        string scope = "window",
        [Description(ToolDescriptions.CaptureHwndParameter)]
        long? hwnd = null,
        [Description(ToolDescriptions.CaptureMonitorIdParameter)]
        string? monitorId = null,
        CancellationToken cancellationToken = default) =>
        RuntimeToolExecution.RunAsync(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsCapture,
            new { scope, hwnd, monitorId },
            async invocation =>
            {
                if (!CaptureScopeExtensions.TryParse(scope, out CaptureScope captureScope))
                {
                    string reason = $"Unsupported capture scope '{scope}'. Допустимые значения: window, desktop.";
                    invocation.Complete("failed", reason);
                    return CreateErrorResult(reason, scope, hwnd, monitorId);
                }

                if (captureScope == CaptureScope.Window && !string.IsNullOrWhiteSpace(monitorId))
                {
                    string reason = "Аргумент monitorId поддерживается только для desktop capture.";
                    invocation.Complete("failed", reason);
                    return CreateErrorResult(reason, scope, hwnd, monitorId);
                }

                if (captureScope == CaptureScope.Desktop && hwnd is not null && !string.IsNullOrWhiteSpace(monitorId))
                {
                    string reason = "Для desktop capture нельзя одновременно передавать hwnd и monitorId.";
                    invocation.Complete("failed", reason);
                    return CreateErrorResult(reason, scope, hwnd, monitorId);
                }

                WindowDescriptor? window = ResolveCaptureWindow(captureScope, hwnd, monitorId);
                if (hwnd is not null && window is null)
                {
                    string reason = "Окно для capture по указанному hwnd больше не найдено.";
                    invocation.Complete("failed", reason, hwnd);
                    return CreateErrorResult(reason, scope, hwnd, monitorId);
                }

                if (captureScope == CaptureScope.Window && window is null)
                {
                    string reason = "Для window capture нужно передать hwnd или сначала прикрепить окно.";
                    invocation.Complete("failed", reason);
                    return CreateErrorResult(reason, scope, hwnd, monitorId);
                }

                CaptureTarget target = new(captureScope, window, monitorId);

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
                            ["coordinate_space"] = metadata.CoordinateSpace,
                            ["effective_dpi"] = metadata.EffectiveDpi?.ToString(CultureInfo.InvariantCulture),
                            ["dpi_scale"] = metadata.DpiScale?.ToString(CultureInfo.InvariantCulture),
                            ["monitor_id"] = metadata.MonitorId,
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
                    return CreateErrorResult(exception.Message, scope, hwnd ?? window?.Hwnd, monitorId);
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

    [Description(ToolDescriptions.WindowsUiaSnapshotTool)]
    [McpServerTool(
        Name = ToolNames.WindowsUiaSnapshot,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    public Task<CallToolResult> UiaSnapshot(
        [Description(ToolDescriptions.UiaSnapshotHwndParameter)]
        long? hwnd = null,
        [Description(ToolDescriptions.UiaSnapshotDepthParameter)]
        int depth = UiaSnapshotDefaults.Depth,
        [Description(ToolDescriptions.UiaSnapshotMaxNodesParameter)]
        int maxNodes = UiaSnapshotDefaults.MaxNodes,
        CancellationToken cancellationToken = default) =>
        RuntimeToolExecution.RunAsync(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsUiaSnapshot,
            new { hwnd, depth, maxNodes },
            async invocation =>
            {
                UiaSnapshotRequest request = CreateUiaSnapshotRequest(depth, maxNodes);
                if (!UiaSnapshotRequestValidator.TryValidate(request, out string? validationReason))
                {
                    return CreateInvalidRequestToolResult(invocation, hwnd, depth, maxNodes, validationReason!);
                }

                WindowDescriptor? attachedWindow = _sessionManager.GetAttachedWindow()?.Window;
                UiaSnapshotTargetResolution resolution = _windowTargetResolver.ResolveUiaSnapshotTarget(hwnd, attachedWindow);
                if (resolution.Window is null)
                {
                    return CreateTargetFailureToolResult(invocation, hwnd, depth, maxNodes, attachedWindow, resolution.FailureCode);
                }

                WindowDescriptor targetWindow = resolution.Window;

                try
                {
                    UiaSnapshotResult runtimeResult = await _uiAutomationService
                        .SnapshotAsync(
                            targetWindow,
                            request,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return CreateCompletedUiaSnapshotToolResult(
                        invocation,
                        runtimeResult,
                        hwnd,
                        depth,
                        maxNodes,
                        resolution.Source,
                        targetWindow.Hwnd);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return CreateUnexpectedServerFailureToolResult(
                        invocation,
                        exception,
                        hwnd,
                        depth,
                        maxNodes,
                        resolution.Source,
                        targetWindow.Hwnd);
                }
            });

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

    private WindowDescriptor? ResolveCaptureWindow(CaptureScope scope, long? hwnd, string? monitorId)
    {
        if (scope == CaptureScope.Desktop && !string.IsNullOrWhiteSpace(monitorId))
        {
            return null;
        }

        return _windowTargetResolver.ResolveExplicitOrAttachedWindow(hwnd, _sessionManager.GetAttachedWindow()?.Window);
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

    private static CallToolResult CreateToolResult<T>(T payload, bool isError)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, PayloadJsonOptions),
                },
            ],
        };
    }

    private static bool ActivateStatusIsToolError(string status) =>
        status is "failed" or "ambiguous";

    private static bool UiaSnapshotStatusIsToolError(string status) =>
        !string.Equals(status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal);

    private static UiaSnapshotRequest CreateUiaSnapshotRequest(int depth, int maxNodes) =>
        new()
        {
            Depth = depth,
            MaxNodes = maxNodes,
        };

    private static CallToolResult CreateInvalidRequestToolResult(
        AuditInvocationScope invocation,
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string reason)
    {
        UiaSnapshotToolResult payload = CreateUiaSnapshotFailurePayload(
            reason,
            requestedHwnd,
            requestedDepth,
            requestedMaxNodes);
        CompleteUiaSnapshotInvocation(
            invocation,
            outcome: "failed",
            message: reason,
            windowHwnd: requestedHwnd,
            data: CreateUiaSnapshotValidationAuditData(requestedHwnd, requestedDepth, requestedMaxNodes));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateTargetFailureToolResult(
        AuditInvocationScope invocation,
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        WindowDescriptor? attachedWindow,
        string? targetFailureCode)
    {
        string reason = CreateUiaSnapshotTargetFailureReason(targetFailureCode);
        UiaSnapshotToolResult payload = CreateUiaSnapshotFailurePayload(
            reason,
            requestedHwnd,
            requestedDepth,
            requestedMaxNodes,
            targetFailureCode: targetFailureCode);
        CompleteUiaSnapshotInvocation(
            invocation,
            outcome: "failed",
            message: reason,
            windowHwnd: requestedHwnd ?? attachedWindow?.Hwnd,
            data: CreateUiaSnapshotTargetFailureAuditData(requestedHwnd, requestedDepth, requestedMaxNodes, targetFailureCode));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateCompletedUiaSnapshotToolResult(
        AuditInvocationScope invocation,
        UiaSnapshotResult runtimeResult,
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string? targetSource,
        long fallbackWindowHwnd)
    {
        UiaSnapshotToolResult payload = CreateUiaSnapshotToolResult(runtimeResult, targetSource, requestedHwnd);
        CompleteUiaSnapshotInvocation(
            invocation,
            outcome: runtimeResult.Status,
            message: runtimeResult.Status == UiaSnapshotStatusValues.Done
                ? "UIA snapshot построен."
                : runtimeResult.Reason ?? "UIA snapshot завершился с ошибкой.",
            windowHwnd: runtimeResult.Window?.Hwnd ?? fallbackWindowHwnd,
            data: CreateUiaSnapshotRuntimeAuditData(requestedHwnd, requestedDepth, requestedMaxNodes, targetSource, runtimeResult));
        return CreateToolResult(payload, isError: UiaSnapshotStatusIsToolError(runtimeResult.Status));
    }

    private static CallToolResult CreateUnexpectedServerFailureToolResult(
        AuditInvocationScope invocation,
        Exception exception,
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string? targetSource,
        long targetHwnd)
    {
        const string reason = "Server не смог завершить UIA snapshot request.";
        UiaSnapshotToolResult payload = CreateUiaSnapshotFailurePayload(
            reason,
            requestedHwnd,
            requestedDepth,
            requestedMaxNodes,
            targetSource: targetSource);
        invocation.CompleteSanitizedFailure(
            reason,
            exception,
            targetHwnd,
            CreateUiaSnapshotUnexpectedFailureAuditData(requestedHwnd, requestedDepth, requestedMaxNodes, targetSource));
        return CreateToolResult(payload, isError: true);
    }

    private static UiaSnapshotToolResult CreateUiaSnapshotFailurePayload(
        string reason,
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string? targetSource = null,
        string? targetFailureCode = null) =>
        new(
            Status: UiaSnapshotStatusValues.Failed,
            Reason: reason,
            Window: null,
            RequestedHwnd: requestedHwnd,
            RequestedDepth: requestedDepth,
            RequestedMaxNodes: requestedMaxNodes,
            TargetSource: targetSource,
            TargetFailureCode: targetFailureCode);

    private static Dictionary<string, string?> CreateUiaSnapshotValidationAuditData(
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes)
    {
        Dictionary<string, string?> data = CreateUiaSnapshotBaseAuditData(requestedHwnd, requestedDepth, requestedMaxNodes);
        data["request_validation"] = bool.TrueString;
        return data;
    }

    private static Dictionary<string, string?> CreateUiaSnapshotTargetFailureAuditData(
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string? targetFailureCode)
    {
        Dictionary<string, string?> data = CreateUiaSnapshotBaseAuditData(requestedHwnd, requestedDepth, requestedMaxNodes);
        data["target_failure_code"] = targetFailureCode;
        return data;
    }

    private static Dictionary<string, string?> CreateUiaSnapshotRuntimeAuditData(
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string? targetSource,
        UiaSnapshotResult runtimeResult)
    {
        Dictionary<string, string?> data = CreateUiaSnapshotBaseAuditData(requestedHwnd, requestedDepth, requestedMaxNodes);
        data["target_source"] = targetSource;
        data["node_count"] = runtimeResult.NodeCount.ToString(CultureInfo.InvariantCulture);
        data["artifact_path"] = runtimeResult.ArtifactPath;
        return data;
    }

    private static Dictionary<string, string?> CreateUiaSnapshotUnexpectedFailureAuditData(
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes,
        string? targetSource)
    {
        Dictionary<string, string?> data = CreateUiaSnapshotBaseAuditData(requestedHwnd, requestedDepth, requestedMaxNodes);
        data["target_source"] = targetSource;
        data["unexpected_server_failure"] = bool.TrueString;
        return data;
    }

    private static Dictionary<string, string?> CreateUiaSnapshotBaseAuditData(
        long? requestedHwnd,
        int requestedDepth,
        int requestedMaxNodes) =>
        new()
        {
            ["requested_hwnd"] = requestedHwnd?.ToString(CultureInfo.InvariantCulture),
            ["requested_depth"] = requestedDepth.ToString(CultureInfo.InvariantCulture),
            ["requested_max_nodes"] = requestedMaxNodes.ToString(CultureInfo.InvariantCulture),
        };

    private static void CompleteUiaSnapshotInvocation(
        AuditInvocationScope invocation,
        string outcome,
        string message,
        long? windowHwnd,
        IReadOnlyDictionary<string, string?> data) =>
        invocation.Complete(outcome, message, windowHwnd, data);

    private static UiaSnapshotToolResult CreateUiaSnapshotToolResult(
        UiaSnapshotResult runtimeResult,
        string? targetSource,
        long? requestedHwnd) =>
        new(
            Status: runtimeResult.Status,
            Reason: runtimeResult.Reason,
            Window: runtimeResult.Window,
            RequestedHwnd: requestedHwnd,
            RequestedDepth: runtimeResult.RequestedDepth,
            RequestedMaxNodes: runtimeResult.RequestedMaxNodes,
            TargetSource: targetSource,
            TargetFailureCode: null,
            View: runtimeResult.View,
            RealizedDepth: runtimeResult.RealizedDepth,
            NodeCount: runtimeResult.NodeCount,
            Truncated: runtimeResult.Truncated,
            DepthBoundaryReached: runtimeResult.DepthBoundaryReached,
            NodeBudgetBoundaryReached: runtimeResult.NodeBudgetBoundaryReached,
            AcquisitionMode: runtimeResult.AcquisitionMode,
            ArtifactPath: runtimeResult.ArtifactPath,
            CapturedAtUtc: runtimeResult.CapturedAtUtc,
            Root: runtimeResult.Root);

    private static string CreateUiaSnapshotTargetFailureReason(string? failureCode) =>
        failureCode switch
        {
            UiaSnapshotTargetFailureValues.MissingTarget => "Для UIA snapshot нужно передать hwnd, прикрепить окно или иметь единственный foreground top-level window.",
            UiaSnapshotTargetFailureValues.StaleExplicitTarget => "Окно для UIA snapshot по указанному hwnd больше не найдено или explicit hwnd недействителен.",
            UiaSnapshotTargetFailureValues.StaleAttachedTarget => "Прикрепленное окно больше не найдено или больше не совпадает с live target.",
            UiaSnapshotTargetFailureValues.AmbiguousActiveTarget => "Foreground window для UIA snapshot неоднозначен: найдено несколько live top-level candidates.",
            _ => "Не удалось разрешить target для UIA snapshot.",
        };

    private static CallToolResult CreateErrorResult(string reason, string scope, long? hwnd, string? monitorId)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            new
            {
                status = "failed",
                reason,
                scope,
                hwnd,
                monitorId,
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
