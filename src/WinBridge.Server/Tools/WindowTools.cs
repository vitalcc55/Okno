using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Launch;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;
using RuntimeToolExecution = WinBridge.Runtime.Diagnostics.ToolExecution;

namespace WinBridge.Server.Tools;

[McpServerToolType]
public sealed class WindowTools
{
    private const string LaunchPreviewCompletedEventName = "launch.preview.completed";
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AuditLog _auditLog;
    private readonly ICaptureService _captureService;
    private readonly IMonitorManager _monitorManager;
    private readonly IProcessLaunchService _processLaunchService;
    private readonly ISessionManager _sessionManager;
    private readonly IToolExecutionGate _toolExecutionGate;
    private readonly IUiAutomationService _uiAutomationService;
    private readonly IWaitService _waitService;
    private readonly WaitResultMaterializer _waitResultMaterializer;
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
        IUiAutomationService uiAutomationService,
        IWaitService waitService,
        WaitResultMaterializer waitResultMaterializer,
        IToolExecutionGate toolExecutionGate,
        IProcessLaunchService processLaunchService)
    {
        _auditLog = auditLog;
        _captureService = captureService;
        _monitorManager = monitorManager;
        _processLaunchService = processLaunchService;
        _sessionManager = sessionManager;
        _toolExecutionGate = toolExecutionGate;
        _uiAutomationService = uiAutomationService;
        _waitService = waitService;
        _waitResultMaterializer = waitResultMaterializer;
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

    public Task<CallToolResult> LaunchProcess(
        LaunchProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteLaunchProcessAsync(request, new(true, request, null, null), cancellationToken);
    }

    public Task<CallToolResult> LaunchProcess(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        LaunchProcessTransportBinding binding = BindLaunchProcessRequest(requestContext);
        return ExecuteLaunchProcessAsync(binding.Request, binding, cancellationToken);
    }

    private Task<CallToolResult> ExecuteLaunchProcessAsync(
        LaunchProcessRequest request,
        LaunchProcessTransportBinding binding,
        CancellationToken cancellationToken)
    {
        LaunchProcessBoundaryValidation validation = ValidateLaunchProcessRequest(request, binding);
        ToolExecutionPolicyDescriptor policy = ToolContractManifest.ResolveExecutionPolicy(ToolNames.WindowsLaunchProcess)
            ?? throw new InvalidOperationException("Execution policy for windows.launch_process is not configured.");
        ToolExecutionIntent intent = new(
            IsDryRunRequested: request.DryRun,
            ConfirmationGranted: request.Confirm,
            PreviewAvailable: validation.IsValid);

        return RuntimeToolExecution.RunGatedAsync(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsLaunchProcess,
            request,
            policy,
            intent,
            _toolExecutionGate,
            (invocation, decision) => ExecuteAllowedLaunchProcessAsync(invocation, request, validation, decision, cancellationToken),
            (invocation, decision) => Task.FromResult(CreateRejectedLaunchProcessToolResult(invocation, request, validation, decision)));
    }

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

    [Description(ToolDescriptions.WindowsWaitTool)]
    [McpServerTool(
        Name = ToolNames.WindowsWait,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    public Task<CallToolResult> Wait(
        [Description(ToolDescriptions.WaitConditionParameter)]
        string condition,
        [Description(ToolDescriptions.WaitSelectorParameter)]
        WaitElementSelector? selector = null,
        [Description(ToolDescriptions.WaitExpectedTextParameter)]
        string? expectedText = null,
        [Description(ToolDescriptions.WaitHwndParameter)]
        long? hwnd = null,
        [Description(ToolDescriptions.WaitTimeoutMsParameter)]
        int timeoutMs = WaitDefaults.TimeoutMs,
        CancellationToken cancellationToken = default) =>
        RuntimeToolExecution.RunAsync(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.WindowsWait,
            new { condition, selector, expectedText, hwnd, timeoutMs },
            async invocation =>
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
                WaitRequest request = new(condition, selector, expectedText, timeoutMs);
                WindowDescriptor? attachedWindow = _sessionManager.GetAttachedWindow()?.Window;
                WaitTargetResolution resolution = new();

                try
                {
                    resolution = _windowTargetResolver.ResolveWaitTarget(hwnd, attachedWindow);
                    WaitResult runtimeResult = await _waitService
                        .WaitAsync(resolution, request, cancellationToken)
                        .ConfigureAwait(false);

                    invocation.Complete(
                        runtimeResult.Status,
                        runtimeResult.Status == WaitStatusValues.Done
                            ? "Wait condition подтверждён."
                            : runtimeResult.Reason ?? "Wait condition завершился без success.",
                        runtimeResult.Window?.Hwnd ?? resolution.Window?.Hwnd ?? hwnd ?? attachedWindow?.Hwnd,
                        CreateWaitAuditData(condition, hwnd, resolution, runtimeResult));
                    return CreateToolResult(runtimeResult, isError: WaitStatusIsToolError(runtimeResult.Status));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    const string reason = "Server не смог завершить wait request.";
                    WaitResult failedResult = _waitResultMaterializer.MaterializeTerminalFailure(
                        request: request,
                        target: resolution,
                        startedAtUtc: startedAtUtc,
                        result: new WaitResult(
                            Status: WaitStatusValues.Failed,
                            Condition: condition,
                            TargetSource: resolution.Source,
                            TargetFailureCode: resolution.FailureCode,
                            Reason: reason,
                            TimeoutMs: timeoutMs,
                            ElapsedMs: (int)Math.Round(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, MidpointRounding.AwayFromZero)),
                        failureStage: "tool_boundary_unhandled",
                        failureException: exception);
                    invocation.CompleteSanitizedFailure(
                        reason,
                        exception,
                        hwnd ?? resolution.Window?.Hwnd ?? attachedWindow?.Hwnd,
                        CreateUnexpectedWaitFailureAuditData(condition, hwnd, timeoutMs, resolution));
                    return CreateToolResult(failedResult, isError: true);
                }
            });

    private async Task<CallToolResult> ExecuteAllowedLaunchProcessAsync(
        AuditInvocationScope invocation,
        LaunchProcessRequest request,
        LaunchProcessBoundaryValidation validation,
        ToolExecutionDecision decision,
        CancellationToken cancellationToken)
    {
        if (!validation.IsValid)
        {
            return CreateInvalidLaunchProcessToolResult(invocation, request, validation);
        }

        if (decision.Mode == ToolExecutionMode.DryRun)
        {
            LaunchProcessResult dryRunResult = CreateAllowedDryRunLaunchProcessResult(validation.Preview!, decision);
            _auditLog.TryRecordRuntimeEvent(
                eventName: LaunchPreviewCompletedEventName,
                severity: "info",
                messageHuman: "Dry-run preview подготовлен без factual запуска процесса.",
                toolName: ToolNames.WindowsLaunchProcess,
                outcome: "preview_only",
                windowHwnd: null,
                data: CreateLaunchProcessAuditData(dryRunResult));
            invocation.CompleteBestEffort(
                dryRunResult.Status,
                "Подготовлен dry-run preview запуска процесса.",
                data: CreateLaunchProcessAuditData(dryRunResult));
            return CreateToolResult(dryRunResult, isError: false);
        }

        try
        {
            LaunchProcessResult runtimeResult = await _processLaunchService
                .LaunchAsync(request, cancellationToken)
                .ConfigureAwait(false);
            invocation.CompleteBestEffort(
                runtimeResult.Status,
                CreateLaunchProcessCompletionMessage(runtimeResult),
                data: CreateLaunchProcessAuditData(runtimeResult));
            return CreateToolResult(runtimeResult, isError: LaunchProcessStatusIsToolError(runtimeResult.Status));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LaunchProcessResult failedResult = CreateUnexpectedLaunchProcessFailureResult(request, validation.ExecutableIdentity);
            invocation.CompleteSanitizedFailure(
                failedResult.Reason ?? "Server не смог завершить launch request.",
                exception,
                data: CreateLaunchProcessAuditData(failedResult));
            return CreateToolResult(failedResult, isError: true);
        }
    }

    private static CallToolResult CreateRejectedLaunchProcessToolResult(
        AuditInvocationScope invocation,
        LaunchProcessRequest request,
        LaunchProcessBoundaryValidation validation,
        ToolExecutionDecision decision)
    {
        if (!validation.IsValid)
        {
            return CreateInvalidLaunchProcessToolResult(invocation, request, validation);
        }

        LaunchProcessResult rejectedResult = CreateRejectedLaunchProcessResult(validation.Preview!, validation.ExecutableIdentity, decision);
        invocation.Complete(
            rejectedResult.Status,
            CreateLaunchProcessCompletionMessage(rejectedResult),
            data: CreateLaunchProcessAuditData(rejectedResult));
        return CreateToolResult(rejectedResult, isError: true);
    }

    private static CallToolResult CreateInvalidLaunchProcessToolResult(
        AuditInvocationScope invocation,
        LaunchProcessRequest request,
        LaunchProcessBoundaryValidation validation)
    {
        LaunchProcessResult failedResult = CreateLaunchProcessFailureResult(
            request,
            validation.FailureCode ?? LaunchProcessFailureCodeValues.InvalidRequest,
            validation.Reason ?? "Launch request не прошёл validation.",
            validation.ExecutableIdentity);
        invocation.Complete(
            failedResult.Status,
            failedResult.Reason ?? "Launch request не прошёл validation.",
            data: CreateLaunchProcessAuditData(failedResult));
        return CreateToolResult(failedResult, isError: true);
    }

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

    private static LaunchProcessBoundaryValidation ValidateLaunchProcessRequest(
        LaunchProcessRequest request,
        LaunchProcessTransportBinding binding)
    {
        string? executableIdentity = TryResolveSafeExecutableIdentity(request.Executable);
        if (!binding.IsSuccess)
        {
            return new(false, binding.FailureCode, binding.Reason, executableIdentity, null);
        }

        if (!LaunchProcessRequestValidator.TryValidate(request, out string? failureCode, out string? reason))
        {
            return new(false, failureCode, reason, executableIdentity, null);
        }

        return new(
            true,
            null,
            null,
            executableIdentity,
            new LaunchProcessPreview(
                ExecutableIdentity: executableIdentity ?? string.Empty,
                ResolutionMode: Path.IsPathFullyQualified(request.Executable)
                    ? LaunchProcessPreviewResolutionModeValues.AbsolutePath
                    : LaunchProcessPreviewResolutionModeValues.PathLookup,
                ArgumentCount: request.Args.Count,
                WorkingDirectoryProvided: !string.IsNullOrWhiteSpace(request.WorkingDirectory),
                WaitForWindow: request.WaitForWindow,
                TimeoutMs: request.TimeoutMs));
    }

    private static LaunchProcessTransportBinding BindLaunchProcessRequest(
        RequestContext<CallToolRequestParams> requestContext)
    {
        IDictionary<string, JsonElement>? arguments = requestContext.Params?.Arguments;

        try
        {
            JsonElement rawArguments = JsonSerializer.SerializeToElement(arguments);
            LaunchProcessRequest request = rawArguments.Deserialize<LaunchProcessRequest>()
                ?? throw new JsonException("Transport arguments did not deserialize to LaunchProcessRequest.");
            return new(true, request, null, null);
        }
        catch (JsonException exception)
        {
            return new(
                false,
                new LaunchProcessRequest(),
                LaunchProcessFailureCodeValues.InvalidRequest,
                $"Transport arguments для launch_process не прошли binding: {exception.Message}");
        }
    }

    private static LaunchProcessResult CreateRejectedLaunchProcessResult(
        LaunchProcessPreview preview,
        string? executableIdentity,
        ToolExecutionDecision decision) =>
        new(
            Status: ToLaunchProcessRejectedStatus(decision.Kind),
            Decision: ToLaunchProcessRejectedStatus(decision.Kind),
            Reason: CreateLaunchProcessGateReason(decision.Kind),
            ExecutableIdentity: executableIdentity,
            Preview: preview,
            RiskLevel: ToSnakeCase(decision.RiskLevel),
            GuardCapability: decision.GuardCapability,
            RequiresConfirmation: decision.RequiresConfirmation,
            DryRunSupported: decision.DryRunSupported,
            Reasons: decision.Reasons);

    private static LaunchProcessResult CreateAllowedDryRunLaunchProcessResult(
        LaunchProcessPreview preview,
        ToolExecutionDecision decision) =>
        new(
            Status: LaunchProcessStatusValues.Done,
            Decision: LaunchProcessStatusValues.Done,
            ExecutableIdentity: preview.ExecutableIdentity,
            Preview: preview,
            RiskLevel: ToSnakeCase(decision.RiskLevel),
            GuardCapability: decision.GuardCapability,
            RequiresConfirmation: decision.RequiresConfirmation,
            DryRunSupported: decision.DryRunSupported,
            Reasons: decision.Reasons);

    private static LaunchProcessResult CreateLaunchProcessFailureResult(
        LaunchProcessRequest request,
        string failureCode,
        string reason,
        string? executableIdentity) =>
        new(
            Status: LaunchProcessStatusValues.Failed,
            Decision: LaunchProcessStatusValues.Failed,
            FailureCode: failureCode,
            Reason: reason,
            ExecutableIdentity: executableIdentity,
            MainWindowObservationStatus: request.WaitForWindow
                ? null
                : LaunchMainWindowObservationStatusValues.NotRequested);

    private static LaunchProcessResult CreateUnexpectedLaunchProcessFailureResult(
        LaunchProcessRequest request,
        string? executableIdentity) =>
        CreateLaunchProcessFailureResult(
            request,
            LaunchProcessFailureCodeValues.StartFailed,
            "Server не смог завершить launch request.",
            executableIdentity);

    private static Dictionary<string, string?> CreateLaunchProcessAuditData(LaunchProcessResult result) =>
        new Dictionary<string, string?>
        {
            ["status"] = result.Status,
            ["decision"] = result.Decision,
            ["result_mode"] = result.ResultMode,
            ["failure_code"] = result.FailureCode,
            ["executable_identity"] = result.ExecutableIdentity,
            ["process_id"] = result.ProcessId?.ToString(CultureInfo.InvariantCulture),
            ["artifact_path"] = result.ArtifactPath,
            ["main_window_observation_status"] = result.MainWindowObservationStatus,
            ["preview_resolution_mode"] = result.Preview?.ResolutionMode,
            ["preview_argument_count"] = result.Preview?.ArgumentCount.ToString(CultureInfo.InvariantCulture),
        };

    private static string CreateLaunchProcessCompletionMessage(LaunchProcessResult result)
    {
        if (result.Status == LaunchProcessStatusValues.Done && result.Preview is not null && result.ProcessId is null)
        {
            return "Подготовлен dry-run preview запуска процесса.";
        }

        return result.Status switch
        {
            LaunchProcessStatusValues.Blocked => "Запуск процесса заблокирован shared gate.",
            LaunchProcessStatusValues.NeedsConfirmation => "Запуск процесса требует явного подтверждения.",
            LaunchProcessStatusValues.DryRunOnly => "Live launch недоступен, но dry-run preview разрешён.",
            LaunchProcessStatusValues.Done when result.ResultMode == LaunchProcessResultModeValues.WindowObserved =>
                "Процесс успешно запущен, main window observed.",
            LaunchProcessStatusValues.Done when result.ResultMode == LaunchProcessResultModeValues.ProcessStartedAndExited =>
                "Процесс успешно стартовал и уже завершился.",
            LaunchProcessStatusValues.Done => "Процесс успешно запущен.",
            _ => result.Reason ?? "Launch request завершился ошибкой.",
        };
    }

    private static string CreateLaunchProcessGateReason(ToolExecutionDecisionKind kind) =>
        kind switch
        {
            ToolExecutionDecisionKind.Blocked => "Запуск процесса заблокирован shared gate.",
            ToolExecutionDecisionKind.NeedsConfirmation => "Запуск процесса требует явного подтверждения.",
            ToolExecutionDecisionKind.DryRunOnly => "Live launch недоступен, но dry-run preview разрешён.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static string ToLaunchProcessRejectedStatus(ToolExecutionDecisionKind kind) =>
        kind switch
        {
            ToolExecutionDecisionKind.Blocked => LaunchProcessStatusValues.Blocked,
            ToolExecutionDecisionKind.NeedsConfirmation => LaunchProcessStatusValues.NeedsConfirmation,
            ToolExecutionDecisionKind.DryRunOnly => LaunchProcessStatusValues.DryRunOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static string? TryResolveSafeExecutableIdentity(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        string candidate = executable;
        if (!Path.IsPathFullyQualified(executable)
            && Uri.TryCreate(executable, UriKind.Absolute, out Uri? uri)
            && uri.IsAbsoluteUri)
        {
            candidate = uri.AbsolutePath;
        }

        string normalized = candidate.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        string executableName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(executableName) ? null : executableName;
    }

    private static string ToSnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

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

    private static bool WaitStatusIsToolError(string status) =>
        !string.Equals(status, WaitStatusValues.Done, StringComparison.Ordinal);

    private static bool UiaSnapshotStatusIsToolError(string status) =>
        !string.Equals(status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal);

    private static bool LaunchProcessStatusIsToolError(string status) =>
        !string.Equals(status, LaunchProcessStatusValues.Done, StringComparison.Ordinal);

    private static Dictionary<string, string?> CreateWaitAuditData(
        string condition,
        long? requestedHwnd,
        WaitTargetResolution resolution,
        WaitResult result) =>
        new()
        {
            ["condition"] = condition,
            ["requested_hwnd"] = requestedHwnd?.ToString(CultureInfo.InvariantCulture),
            ["target_source"] = result.TargetSource ?? resolution.Source,
            ["target_failure_code"] = result.TargetFailureCode ?? resolution.FailureCode,
            ["timeout_ms"] = result.TimeoutMs.ToString(CultureInfo.InvariantCulture),
            ["elapsed_ms"] = result.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            ["attempt_count"] = result.AttemptCount.ToString(CultureInfo.InvariantCulture),
            ["artifact_path"] = result.ArtifactPath,
        };

    private static Dictionary<string, string?> CreateUnexpectedWaitFailureAuditData(
        string condition,
        long? requestedHwnd,
        int timeoutMs,
        WaitTargetResolution resolution) =>
        new()
        {
            ["condition"] = condition,
            ["requested_hwnd"] = requestedHwnd?.ToString(CultureInfo.InvariantCulture),
            ["timeout_ms"] = timeoutMs.ToString(CultureInfo.InvariantCulture),
            ["target_source"] = resolution.Source,
            ["target_failure_code"] = resolution.FailureCode,
            ["unexpected_server_failure"] = bool.TrueString,
        };

    private static UiaSnapshotRequest CreateUiaSnapshotRequest(int depth, int maxNodes) =>
        new()
        {
            Depth = depth,
            MaxNodes = maxNodes,
        };

    private readonly record struct LaunchProcessBoundaryValidation(
        bool IsValid,
        string? FailureCode,
        string? Reason,
        string? ExecutableIdentity,
        LaunchProcessPreview? Preview);

    private readonly record struct LaunchProcessTransportBinding(
        bool IsSuccess,
        LaunchProcessRequest Request,
        string? FailureCode,
        string? Reason);

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
