using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinGetAppStateHandler(
    IWindowManager windowManager,
    ISessionManager sessionManager,
    ComputerUseWinApprovalStore approvalStore,
    ComputerUseWinExecutionTargetCatalog executionTargetCatalog,
    ComputerUseWinStateStore stateStore,
    IWindowActivationService windowActivationService,
    ComputerUseWinAppStateObserver appStateObserver)
{
    public async Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinGetAppStateRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WindowDescriptor> windows = windowManager.ListWindows();
        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windows,
            executionTargetCatalog,
            sessionManager,
            request.WindowId,
            request.Hwnd);
        if (!resolution.IsSuccess)
        {
            return string.Equals(resolution.FailureCode, ComputerUseWinFailureCodeValues.IdentityProofUnavailable, StringComparison.Ordinal)
                ? ComputerUseWinToolResultFactory.CreateStateIdentityProofFailure(invocation, resolution.Window!, resolution.Reason!)
                : ComputerUseWinToolResultFactory.CreateStateFailure(invocation, resolution.FailureCode!, resolution.Reason!, resolution.Window?.Hwnd);
        }

        ComputerUseWinExecutionTarget selectedTarget = resolution.Target!;
        WindowDescriptor selectedWindow = selectedTarget.Window;
        if (ComputerUseWinTargetPolicy.TryGetBlockedReason(selectedWindow, out string? blockReason))
        {
            ComputerUseWinRuntimeState blockedState = ComputerUseWinRuntimeStateModel.Blocked();
            if (ComputerUseWinRuntimeStateModel.CanPromoteToObserved(blockedState, hasFreshObservation: false))
            {
                throw new InvalidOperationException("Blocked runtime state must not become observed without new live proof.");
            }

            return ComputerUseWinToolResultFactory.CreateStateBlocked(invocation, selectedWindow, blockReason!);
        }

        string appId = selectedTarget.ApprovalKey.Value;
        string windowId = selectedTarget.WindowId.Value;
        ComputerUseWinRuntimeState preObservationState = approvalStore.IsApproved(appId)
            ? ComputerUseWinRuntimeStateModel.Approved()
            : ComputerUseWinRuntimeStateModel.Attached();
        if (!approvalStore.IsApproved(appId))
        {
            if (!request.Confirm)
            {
                return ComputerUseWinToolResultFactory.CreateStateApprovalRequired(invocation, selectedWindow, appId, windowId);
            }

            approvalStore.Approve(appId);
            preObservationState = ComputerUseWinRuntimeStateModel.Approved();
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
            windowId,
            request.MaxNodes,
            warnings,
            cancellationToken).ConfigureAwait(false);
        if (!observation.IsSuccess)
        {
            return ComputerUseWinToolResultFactory.CreateStateFailure(
                invocation,
                observation.FailureDetails!,
                selectedWindow.Hwnd);
        }

        if (!ComputerUseWinRuntimeStateModel.CanPromoteToObserved(preObservationState, hasFreshObservation: true))
        {
            return ComputerUseWinToolResultFactory.CreateStateFailure(
                invocation,
                ComputerUseWinFailureCodeValues.ObservationFailed,
                "Computer Use for Windows не смог подтвердить explicit observed state transition после live proof.",
                selectedWindow.Hwnd);
        }

        ComputerUseWinPreparedAppState preparedState = observation.PreparedState!;
        return ComputerUseWinGetAppStateFinalizer.FinalizeSuccess(
            invocation,
            selectedTarget,
            selectedWindow,
            preparedState,
            stateStore,
            sessionManager);
    }
}
