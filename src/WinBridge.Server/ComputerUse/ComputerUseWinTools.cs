using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;
using RuntimeToolExecution = WinBridge.Runtime.Diagnostics.ToolExecution;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinTools
{
    internal static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ComputerUseWinApprovalStore approvalStore;
    private readonly ComputerUseWinAppStateObserver appStateObserver;
    private readonly ComputerUseWinClickExecutionCoordinator clickExecutionCoordinator;
    private readonly ComputerUseWinStateStore stateStore;
    private readonly AuditLog auditLog;
    private readonly ICaptureService captureService;
    private readonly IInputService inputService;
    private readonly ISessionManager sessionManager;
    private readonly IUiAutomationService uiAutomationService;
    private readonly IWindowActivationService windowActivationService;
    private readonly IWindowManager windowManager;

    public ComputerUseWinTools(
        AuditLog auditLog,
        ISessionManager sessionManager,
        IWindowManager windowManager,
        IWindowActivationService windowActivationService,
        ICaptureService captureService,
        IUiAutomationService uiAutomationService,
        IInputService inputService,
        ComputerUseWinApprovalStore approvalStore,
        ComputerUseWinStateStore stateStore,
        IComputerUseWinInstructionProvider playbookProvider)
    {
        this.auditLog = auditLog;
        this.sessionManager = sessionManager;
        this.windowManager = windowManager;
        this.captureService = captureService;
        this.uiAutomationService = uiAutomationService;
        this.inputService = inputService;
        this.approvalStore = approvalStore;
        this.stateStore = stateStore;
        this.windowActivationService = windowActivationService;
        appStateObserver = new ComputerUseWinAppStateObserver(captureService, uiAutomationService, playbookProvider);
        clickExecutionCoordinator = new ComputerUseWinClickExecutionCoordinator(
            windowActivationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);
    }

    public CallToolResult ListApps()
        => RuntimeToolExecution.Run(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinListApps,
            new { },
            invocation =>
            {
                IReadOnlyList<ComputerUseWinAppDescriptor> apps = BuildAppDescriptors();
                ComputerUseWinListAppsResult payload = new(
                    Status: ComputerUseWinStatusValues.Ok,
                    Apps: apps,
                    Count: apps.Count);

                invocation.Complete(
                    "done",
                    $"Возвращено {apps.Count} app entries для Computer Use for Windows.");

                return CreateToolResult(payload, isError: false);
            });

    public Task<CallToolResult> GetAppState(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinGetAppStateRequest> binding = BindRequest(requestContext, new ComputerUseWinGetAppStateRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinGetAppState,
            binding.Request,
            invocation => ExecuteGetAppStateAsync(invocation, binding, cancellationToken));
    }

    public Task<CallToolResult> Click(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinClickRequest> binding = BindRequest(requestContext, new ComputerUseWinClickRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinClick,
            binding.Request,
            invocation => ExecuteClickAsync(invocation, binding, cancellationToken));
    }

    private async Task<CallToolResult> ExecuteGetAppStateAsync(
        AuditInvocationScope invocation,
        Binding<ComputerUseWinGetAppStateRequest> binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsSuccess)
        {
            return CreateStateFailure(invocation, binding.FailureCode!, binding.Reason!);
        }

        ComputerUseWinGetAppStateRequest request = binding.Request;
        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windowManager.ListWindows(),
            sessionManager,
            request.AppId,
            request.Hwnd);
        if (!resolution.IsSuccess)
        {
            return string.Equals(resolution.FailureCode, ComputerUseWinFailureCodeValues.IdentityProofUnavailable, StringComparison.Ordinal)
                ? CreateStateIdentityProofFailure(invocation, resolution.Window!, resolution.Reason!)
                : CreateStateFailure(invocation, resolution.FailureCode!, resolution.Reason!, resolution.Window?.Hwnd);
        }

        WindowDescriptor selectedWindow = resolution.Window!;
        if (ComputerUseWinTargetPolicy.TryGetBlockedReason(selectedWindow, out string? blockReason))
        {
            return CreateStateBlocked(invocation, selectedWindow, blockReason!);
        }

        if (!ComputerUseWinAppIdentity.TryCreateStableAppId(selectedWindow, out string? stableAppId))
        {
            return CreateStateIdentityProofFailure(
                invocation,
                selectedWindow,
                "Computer Use for Windows не смог подтвердить стабильную process identity окна; approval и observation fail-close-ятся до нового live proof.");
        }

        string appId = stableAppId!;
        if (!approvalStore.IsApproved(appId))
        {
            if (!request.Confirm)
            {
                return CreateStateApprovalRequired(invocation, selectedWindow, appId);
            }

            approvalStore.Approve(appId);
        }

        List<string> warnings = [];
        ActivateWindowResult activation = await windowActivationService.ActivateAsync(selectedWindow, cancellationToken).ConfigureAwait(false);
        if (string.Equals(activation.Status, "done", StringComparison.Ordinal)
            || string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            selectedWindow = activation.Window ?? selectedWindow;
        }
        else if (!string.IsNullOrWhiteSpace(activation.Reason))
        {
            warnings.Add(activation.Reason);
        }

        ComputerUseWinAppStateObservationOutcome observation = await appStateObserver.ObserveAsync(
            selectedWindow,
            appId,
            request.MaxNodes,
            warnings,
            cancellationToken).ConfigureAwait(false);
        if (!observation.IsSuccess)
        {
            return CreateStateFailure(
                invocation,
                observation.FailureDetails!,
                selectedWindow.Hwnd);
        }

        ComputerUseWinPreparedAppState preparedState = observation.PreparedState!;
        return ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
            invocation,
            appId,
            selectedWindow,
            preparedState,
            stateStore,
            sessionManager);
    }

    private async Task<CallToolResult> ExecuteClickAsync(
        AuditInvocationScope invocation,
        Binding<ComputerUseWinClickRequest> binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsSuccess)
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinClick, binding.FailureCode!, binding.Reason!);
        }

        ComputerUseWinClickRequest request = binding.Request;
        if (!TryResolveStoredState(request.StateToken, out ComputerUseWinStoredState? state, out CallToolResult? failureResult, invocation, ToolNames.ComputerUseWinClick))
        {
            return failureResult!;
        }

        ComputerUseWinStoredState resolvedState = state!;

        try
        {
            ComputerUseWinClickExecutionOutcome outcome = await clickExecutionCoordinator.ExecuteAsync(
                resolvedState,
                request,
                cancellationToken).ConfigureAwait(false);

            if (outcome.IsApprovalRequired)
            {
                return CreateActionApprovalRequired(
                    invocation,
                    ToolNames.ComputerUseWinClick,
                    resolvedState.Session.Hwnd,
                    request.ElementIndex,
                    outcome.ApprovalReason!,
                    outcome.Phase);
            }

            if (!outcome.IsSuccess)
            {
                return CreateActionFailure(
                    invocation,
                    ToolNames.ComputerUseWinClick,
                    outcome.FailureDetails!,
                    resolvedState.Session.Hwnd,
                    request.ElementIndex,
                    outcome.Phase);
            }

            return CreateActionToolResult(invocation, ToolNames.ComputerUseWinClick, resolvedState.Session.Hwnd, request.ElementIndex, outcome.Input!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InputExecutionFailureException exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                resolvedState.Session.Hwnd,
                request.ElementIndex,
                exception.InnerException ?? exception,
                exception.Result);
        }
        catch (Exception exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                resolvedState.Session.Hwnd,
                request.ElementIndex,
                exception,
                preDispatchStateMutationPossible: true);
        }
    }

    private ComputerUseWinAppDescriptor[] BuildAppDescriptors()
    {
        IReadOnlyList<WindowDescriptor> windows = windowManager.ListWindows();
        return windows
            .Where(static item => item.IsVisible)
            .Select(static item => ComputerUseWinAppIdentity.TryCreateStableAppId(item, out string? appId)
                ? new StableIdentityWindow(item, appId!)
                : null)
            .Where(static item => item is not null)
            .Cast<StableIdentityWindow>()
            .GroupBy(static item => item.AppId, static item => item.Window)
            .Select(group =>
            {
                WindowDescriptor selected = group
                    .OrderByDescending(static item => item.IsForeground)
                    .ThenByDescending(static item => item.IsVisible)
                    .ThenBy(static item => item.Hwnd)
                    .First();

                bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(selected, out string? blockReason);
                return new ComputerUseWinAppDescriptor(
                    AppId: group.Key,
                    Hwnd: selected.Hwnd,
                    Title: selected.Title,
                    ProcessName: selected.ProcessName,
                    ProcessId: selected.ProcessId,
                    IsForeground: selected.IsForeground,
                    IsVisible: selected.IsVisible,
                    IsApproved: approvalStore.IsApproved(group.Key),
                    IsBlocked: isBlocked,
                    BlockReason: blockReason);
            })
            .OrderByDescending(static item => item.IsForeground)
            .ThenBy(static item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ComputerUseWinStoredElement? TryGetElement(ComputerUseWinStoredState state, int? elementIndex)
    {
        if (elementIndex is null)
        {
            return null;
        }

        return state.Elements.TryGetValue(elementIndex.Value, out ComputerUseWinStoredElement? element)
            ? element
            : null;
    }

    private bool TryResolveStoredState(
        string? stateToken,
        out ComputerUseWinStoredState? state,
        out CallToolResult? failureResult,
        AuditInvocationScope invocation,
        string toolName)
    {
        if (string.IsNullOrWhiteSpace(stateToken))
        {
            state = null;
            failureResult = CreateActionFailure(invocation, toolName, ComputerUseWinFailureCodeValues.StateRequired, "Сначала вызови get_app_state и передай stateToken.");
            return false;
        }

        if (!stateStore.TryGet(stateToken, out state) || state is null)
        {
            failureResult = CreateActionFailure(invocation, toolName, ComputerUseWinFailureCodeValues.StaleState, "stateToken больше не найден; заново вызови get_app_state.");
            return false;
        }

        ComputerUseWinStoredState storedState = state;
        WindowDescriptor expectedWindow = storedState.Window;
        WindowDescriptor? liveWindow = windowManager.ListWindows().SingleOrDefault(item =>
            item.Hwnd == expectedWindow.Hwnd
            && WindowIdentityValidator.MatchesStableIdentity(item, expectedWindow));
        if (liveWindow is null)
        {
            failureResult = CreateActionFailure(invocation, toolName, ComputerUseWinFailureCodeValues.StaleState, "Окно из stateToken больше не совпадает с текущим live target.");
            return false;
        }

        state = storedState with { Window = liveWindow };
        failureResult = null;
        return true;
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

    private static CallToolResult CreateImageToolResult<T>(T payload, byte[] pngBytes, string mimeType)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = false,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, PayloadJsonOptions),
                },
                new ImageContentBlock
                {
                    Data = Encoding.ASCII.GetBytes(Convert.ToBase64String(pngBytes)),
                    MimeType = mimeType,
                },
            ],
        };
    }

    private static Binding<T> BindRequest<T>(RequestContext<CallToolRequestParams> requestContext, T fallbackRequest)
        => ToolRequestBinder.TryBind(
            requestContext.Params?.Arguments,
            fallbackRequest,
            out T request,
            out string? reason,
            ComputerUseWinRequestContractValidator.Validate)
            ? new(true, request, null, null)
            : new(false, fallbackRequest, ComputerUseWinFailureCodeValues.InvalidRequest, $"Transport arguments не прошли binding: {reason}");

    private static async Task<CallToolResult> ExecuteActionAsync(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Func<CancellationToken, Task<InputResult>> executeAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            InputResult input = await executeAsync(cancellationToken).ConfigureAwait(false);
            return CreateActionToolResult(invocation, toolName, targetHwnd, elementIndex, input);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InputExecutionFailureException exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                toolName,
                targetHwnd,
                elementIndex,
                exception.InnerException ?? exception,
                exception.Result);
        }
        catch (Exception exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                toolName,
                targetHwnd,
                elementIndex,
                exception);
        }
    }

    private static CallToolResult CreateStateFailure(AuditInvocationScope invocation, ComputerUseWinFailureDetails failure, long? targetHwnd = null) =>
        CreateStateFailure(invocation, failure.FailureCode, failure.Reason, targetHwnd, failure.AuditException);

    private static CallToolResult CreateStateFailure(
        AuditInvocationScope invocation,
        string failureCode,
        string reason,
        long? targetHwnd = null,
        Exception? auditException = null)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateFailurePayload(failureCode, reason);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, reason, failureCode, targetHwnd, auditException);
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateStateBlocked(AuditInvocationScope invocation, WindowDescriptor window, string reason)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateBlockedPayload(reason);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, reason, ComputerUseWinFailureCodeValues.BlockedTarget, window.Hwnd);
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateStateApprovalRequired(AuditInvocationScope invocation, WindowDescriptor window, string appId)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateApprovalRequiredPayload(window, appId);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, payload.Reason!, ComputerUseWinFailureCodeValues.ApprovalRequired, window.Hwnd);
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateStateIdentityProofFailure(AuditInvocationScope invocation, WindowDescriptor window, string reason)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateIdentityProofFailurePayload(reason);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, reason, ComputerUseWinFailureCodeValues.IdentityProofUnavailable, window.Hwnd);
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateActionFailure(
        AuditInvocationScope invocation,
        string toolName,
        ComputerUseWinFailureDetails failure,
        long? targetHwnd = null,
        int? elementIndex = null,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation) =>
        CreateActionFailure(invocation, toolName, failure.FailureCode, failure.Reason, targetHwnd, elementIndex, failure.AuditException, phase);

    private static CallToolResult CreateActionFailure(
        AuditInvocationScope invocation,
        string toolName,
        string failureCode,
        string reason,
        long? targetHwnd = null,
        int? elementIndex = null,
        Exception? auditException = null,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation)
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
            failureCode,
            reason,
            targetHwnd,
            elementIndex,
            phase);
        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            reason,
            failureCode,
            targetHwnd,
            auditException,
            data: ComputerUseWinActionFinalizer.CreateStructuredPhaseAuditData(phase));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateActionApprovalRequired(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        string reason,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation)
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredApprovalRequiredPayload(
            reason,
            targetHwnd,
            elementIndex,
            phase);
        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            reason,
            ComputerUseWinFailureCodeValues.ApprovalRequired,
            targetHwnd,
            data: ComputerUseWinActionFinalizer.CreateStructuredPhaseAuditData(phase));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateActionToolResult(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        InputResult input) =>
        ComputerUseWinActionFinalizer.FinalizeResult(invocation, toolName, targetHwnd, elementIndex, input);

    private readonly record struct Binding<T>(
        bool IsSuccess,
        T Request,
        string? FailureCode,
        string? Reason);

    private sealed record StableIdentityWindow(WindowDescriptor Window, string AppId);
}
