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

    public Task<CallToolResult> TypeText(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinTypeTextRequest> binding = BindRequest(requestContext, new ComputerUseWinTypeTextRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinTypeText,
            binding.Request,
            invocation => ExecuteTypeTextAsync(invocation, binding, cancellationToken));
    }

    public Task<CallToolResult> PressKey(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinPressKeyRequest> binding = BindRequest(requestContext, new ComputerUseWinPressKeyRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinPressKey,
            binding.Request,
            invocation => ExecutePressKeyAsync(invocation, binding, cancellationToken));
    }

    public Task<CallToolResult> Scroll(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinScrollRequest> binding = BindRequest(requestContext, new ComputerUseWinScrollRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinScroll,
            binding.Request,
            invocation => ExecuteScrollAsync(invocation, binding, cancellationToken));
    }

    public Task<CallToolResult> Drag(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        Binding<ComputerUseWinDragRequest> binding = BindRequest(requestContext, new ComputerUseWinDragRequest());
        return RuntimeToolExecution.RunAsync(
            auditLog,
            sessionManager.GetSnapshot(),
            ToolNames.ComputerUseWinDrag,
            binding.Request,
            invocation => ExecuteDragAsync(invocation, binding, cancellationToken));
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
        WindowDescriptor? selectedWindow = ResolveSelectedWindow(request.AppId, request.Hwnd, out string? failureCode, out string? reason);
        if (selectedWindow is null)
        {
            return CreateStateFailure(invocation, failureCode!, reason!);
        }

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

    private async Task<CallToolResult> ExecuteTypeTextAsync(
        AuditInvocationScope invocation,
        Binding<ComputerUseWinTypeTextRequest> binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsSuccess)
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinTypeText, binding.FailureCode!, binding.Reason!);
        }

        ComputerUseWinTypeTextRequest request = binding.Request;
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinTypeText, ComputerUseWinFailureCodeValues.InvalidRequest, "Параметр text обязателен для type_text.");
        }

        if (!TryResolveStoredState(request.StateToken, out ComputerUseWinStoredState? state, out CallToolResult? failureResult, invocation, ToolNames.ComputerUseWinTypeText))
        {
            return failureResult!;
        }

        return await ExecuteActionAsync(
            invocation,
            ToolNames.ComputerUseWinTypeText,
            state!.Session.Hwnd,
            null,
            ct => inputService.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = state.Session.Hwnd,
                    Actions =
                    [
                        new InputAction
                        {
                            Type = InputActionTypeValues.Type,
                            Text = request.Text,
                        },
                    ],
                },
                new InputExecutionContext(state.Window),
                InputExecutionProfileValues.ComputerUseCore,
                ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CallToolResult> ExecutePressKeyAsync(
        AuditInvocationScope invocation,
        Binding<ComputerUseWinPressKeyRequest> binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsSuccess)
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinPressKey, binding.FailureCode!, binding.Reason!);
        }

        ComputerUseWinPressKeyRequest request = binding.Request;
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinPressKey, ComputerUseWinFailureCodeValues.InvalidRequest, "Параметр key обязателен для press_key.");
        }

        if (!TryResolveStoredState(request.StateToken, out ComputerUseWinStoredState? state, out CallToolResult? failureResult, invocation, ToolNames.ComputerUseWinPressKey))
        {
            return failureResult!;
        }

        if (ComputerUseWinTargetPolicy.RequiresRiskConfirmation(null, ToolNames.ComputerUseWinPressKey, request.Key) && !request.Confirm)
        {
            return CreateActionApprovalRequired(invocation, ToolNames.ComputerUseWinPressKey, state!.Session.Hwnd, null, "Нажатие выбранной клавиши требует явного подтверждения.");
        }

        return await ExecuteActionAsync(
            invocation,
            ToolNames.ComputerUseWinPressKey,
            state!.Session.Hwnd,
            null,
            ct => inputService.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = state.Session.Hwnd,
                    Actions =
                    [
                        new InputAction
                        {
                            Type = InputActionTypeValues.Keypress,
                            Key = request.Key,
                            Repeat = request.Repeat,
                        },
                    ],
                },
                new InputExecutionContext(state.Window),
                InputExecutionProfileValues.ComputerUseCore,
                ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CallToolResult> ExecuteScrollAsync(
        AuditInvocationScope invocation,
        Binding<ComputerUseWinScrollRequest> binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsSuccess)
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinScroll, binding.FailureCode!, binding.Reason!);
        }

        ComputerUseWinScrollRequest request = binding.Request;
        if (!TryResolveStoredState(request.StateToken, out ComputerUseWinStoredState? state, out CallToolResult? failureResult, invocation, ToolNames.ComputerUseWinScroll))
        {
            return failureResult!;
        }

        ComputerUseWinStoredState resolvedState = state!;
        if (!TryCreateScrollAction(resolvedState, request, out InputAction? action, out string? failureCode, out string? reason))
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinScroll, failureCode!, reason!, resolvedState.Session.Hwnd, request.ElementIndex);
        }

        return await ExecuteActionAsync(
            invocation,
            ToolNames.ComputerUseWinScroll,
            resolvedState.Session.Hwnd,
            request.ElementIndex,
            ct => inputService.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = resolvedState.Session.Hwnd,
                    Actions = [action!],
                },
                new InputExecutionContext(resolvedState.Window),
                InputExecutionProfileValues.ComputerUseCore,
                ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CallToolResult> ExecuteDragAsync(
        AuditInvocationScope invocation,
        Binding<ComputerUseWinDragRequest> binding,
        CancellationToken cancellationToken)
    {
        if (!binding.IsSuccess)
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinDrag, binding.FailureCode!, binding.Reason!);
        }

        ComputerUseWinDragRequest request = binding.Request;
        if (!TryResolveStoredState(request.StateToken, out ComputerUseWinStoredState? state, out CallToolResult? failureResult, invocation, ToolNames.ComputerUseWinDrag))
        {
            return failureResult!;
        }

        ComputerUseWinStoredState resolvedState = state!;
        if (!TryCreateDragAction(resolvedState, request, out InputAction? action, out string? failureCode, out string? reason))
        {
            return CreateActionFailure(invocation, ToolNames.ComputerUseWinDrag, failureCode!, reason!, resolvedState.Session.Hwnd);
        }

        return await ExecuteActionAsync(
            invocation,
            ToolNames.ComputerUseWinDrag,
            resolvedState.Session.Hwnd,
            request.FromElementIndex ?? request.ToElementIndex,
            ct => inputService.ExecuteAsync(
                new InputRequest
                {
                    Hwnd = resolvedState.Session.Hwnd,
                    Actions = [action!],
                },
                new InputExecutionContext(resolvedState.Window),
                InputExecutionProfileValues.ComputerUseCore,
                ct),
            cancellationToken).ConfigureAwait(false);
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

    private WindowDescriptor? ResolveSelectedWindow(string? appId, long? hwnd, out string? failureCode, out string? reason)
    {
        IReadOnlyList<WindowDescriptor> windows = windowManager.ListWindows();
        if (hwnd is not null)
        {
            WindowDescriptor? explicitWindow = windows.SingleOrDefault(item => item.Hwnd == hwnd.Value);
            if (explicitWindow is null)
            {
                failureCode = ComputerUseWinFailureCodeValues.MissingTarget;
                reason = "Окно по указанному hwnd не найдено.";
                return null;
            }

            failureCode = null;
            reason = null;
            return explicitWindow;
        }

        if (!string.IsNullOrWhiteSpace(appId))
        {
            WindowDescriptor[] candidates = windows
                .Where(item => ComputerUseWinAppIdentity.TryCreateStableAppId(item, out string? candidateAppId)
                    && string.Equals(candidateAppId, appId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 0)
            {
                failureCode = ComputerUseWinFailureCodeValues.MissingTarget;
                reason = $"App '{appId}' не найдена среди текущих окон.";
                return null;
            }

            WindowDescriptor[] foregroundCandidates = candidates.Where(static item => item.IsForeground).ToArray();
            if (foregroundCandidates.Length == 1)
            {
                failureCode = null;
                reason = null;
                return foregroundCandidates[0];
            }

            if (candidates.Length == 1)
            {
                failureCode = null;
                reason = null;
                return candidates[0];
            }

            failureCode = ComputerUseWinFailureCodeValues.AmbiguousTarget;
            reason = $"App '{appId}' соответствует нескольким окнам; укажи hwnd или сфокусируй нужное окно.";
            return null;
        }

        AttachedWindow? attached = sessionManager.GetAttachedWindow();
        if (attached?.Window is WindowDescriptor attachedWindow)
        {
            WindowDescriptor? liveAttached = windows.SingleOrDefault(item =>
                item.Hwnd == attachedWindow.Hwnd
                && WindowIdentityValidator.MatchesStableIdentity(item, attachedWindow));
            if (liveAttached is not null)
            {
                failureCode = null;
                reason = null;
                return liveAttached;
            }
        }

        failureCode = ComputerUseWinFailureCodeValues.MissingTarget;
        reason = "Для get_app_state нужно передать appId или hwnd, либо сначала иметь актуальный attached window.";
        return null;
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

    private static bool TryCreateScrollAction(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        out InputAction? action,
        out string? failureCode,
        out string? reason)
    {
        InputPoint? point = null;
        string coordinateSpace;
        InputCaptureReference? captureReference = null;
        if (request.ElementIndex is int elementIndex)
        {
            ComputerUseWinStoredElement? element = TryGetElement(state, elementIndex);
            if (element?.Bounds is not Bounds elementBounds)
            {
                action = null;
                failureCode = ComputerUseWinFailureCodeValues.InvalidRequest;
                reason = $"elementIndex {elementIndex} не существует или не даёт scroll bounds.";
                return false;
            }

            point = new InputPoint((elementBounds.Left + elementBounds.Right) / 2, (elementBounds.Top + elementBounds.Bottom) / 2);
            coordinateSpace = InputCoordinateSpaceValues.Screen;
        }
        else if (request.Point is InputPoint requestedPoint)
        {
            point = requestedPoint;
            coordinateSpace = string.IsNullOrWhiteSpace(request.CoordinateSpace)
                ? InputCoordinateSpaceValues.CapturePixels
                : request.CoordinateSpace!;
            captureReference = string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
                ? state.CaptureReference
                : null;
        }
        else
        {
            action = null;
            failureCode = ComputerUseWinFailureCodeValues.InvalidRequest;
            reason = "Для scroll требуется elementIndex или point.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Direction))
        {
            action = null;
            failureCode = ComputerUseWinFailureCodeValues.InvalidRequest;
            reason = "Для scroll требуется direction.";
            return false;
        }

        int pages = request.Pages.GetValueOrDefault(1);
        action = new InputAction
        {
            Type = InputActionTypeValues.Scroll,
            CoordinateSpace = coordinateSpace,
            Point = point,
            Direction = request.Direction,
            Delta = Math.Max(1, pages) * 120,
            CaptureReference = captureReference,
        };
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryCreateDragAction(
        ComputerUseWinStoredState state,
        ComputerUseWinDragRequest request,
        out InputAction? action,
        out string? failureCode,
        out string? reason)
    {
        if (!TryResolveDragEndpoint(state, request.FromElementIndex, request.FromPoint, request.CoordinateSpace, out string? coordinateSpace, out InputPoint? fromPoint, out InputCaptureReference? captureReference, out failureCode, out reason)
            || !TryResolveDragEndpoint(state, request.ToElementIndex, request.ToPoint, coordinateSpace, out coordinateSpace, out InputPoint? toPoint, out _, out failureCode, out reason))
        {
            action = null;
            return false;
        }

        action = new InputAction
        {
            Type = InputActionTypeValues.Drag,
            CoordinateSpace = coordinateSpace,
            Path =
            [
                fromPoint!,
                toPoint!,
            ],
            CaptureReference = captureReference,
        };
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryResolveDragEndpoint(
        ComputerUseWinStoredState state,
        int? elementIndex,
        InputPoint? point,
        string? requestedCoordinateSpace,
        out string coordinateSpace,
        out InputPoint? resolvedPoint,
        out InputCaptureReference? captureReference,
        out string? failureCode,
        out string? reason)
    {
        if (elementIndex is int index)
        {
            ComputerUseWinStoredElement? element = TryGetElement(state, index);
            if (element?.Bounds is not Bounds elementBounds)
            {
                coordinateSpace = InputCoordinateSpaceValues.Screen;
                resolvedPoint = null;
                captureReference = null;
                failureCode = ComputerUseWinFailureCodeValues.InvalidRequest;
                reason = $"elementIndex {index} не существует или не даёт drag bounds.";
                return false;
            }

            coordinateSpace = InputCoordinateSpaceValues.Screen;
            resolvedPoint = new InputPoint((elementBounds.Left + elementBounds.Right) / 2, (elementBounds.Top + elementBounds.Bottom) / 2);
            captureReference = null;
            failureCode = null;
            reason = null;
            return true;
        }

        if (point is not InputPoint explicitPoint)
        {
            coordinateSpace = InputCoordinateSpaceValues.Screen;
            resolvedPoint = null;
            captureReference = null;
            failureCode = ComputerUseWinFailureCodeValues.InvalidRequest;
            reason = "Для drag требуются from/to elementIndex или point.";
            return false;
        }

        coordinateSpace = string.IsNullOrWhiteSpace(requestedCoordinateSpace)
            ? InputCoordinateSpaceValues.CapturePixels
            : requestedCoordinateSpace!;
        resolvedPoint = explicitPoint;
        captureReference = string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
            ? state.CaptureReference
            : null;
        failureCode = null;
        reason = null;
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
