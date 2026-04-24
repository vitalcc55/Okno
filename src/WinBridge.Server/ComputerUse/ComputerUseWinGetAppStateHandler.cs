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
    ComputerUseWinStateStore stateStore,
    IWindowActivationService windowActivationService,
    ComputerUseWinAppStateObserver appStateObserver)
{
    public async Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinGetAppStateRequest request,
        CancellationToken cancellationToken)
    {
        ComputerUseWinGetAppStateTargetResolution resolution = ComputerUseWinGetAppStateTargetResolver.Resolve(
            windowManager.ListWindows(),
            sessionManager,
            request.AppId,
            request.Hwnd);
        if (!resolution.IsSuccess)
        {
            return string.Equals(resolution.FailureCode, ComputerUseWinFailureCodeValues.IdentityProofUnavailable, StringComparison.Ordinal)
                ? ComputerUseWinToolResultFactory.CreateStateIdentityProofFailure(invocation, resolution.Window!, resolution.Reason!)
                : ComputerUseWinToolResultFactory.CreateStateFailure(invocation, resolution.FailureCode!, resolution.Reason!, resolution.Window?.Hwnd);
        }

        WindowDescriptor selectedWindow = resolution.Window!;
        if (ComputerUseWinTargetPolicy.TryGetBlockedReason(selectedWindow, out string? blockReason))
        {
            return ComputerUseWinToolResultFactory.CreateStateBlocked(invocation, selectedWindow, blockReason!);
        }

        if (!ComputerUseWinAppIdentity.TryCreateStableAppId(selectedWindow, out string? stableAppId))
        {
            return ComputerUseWinToolResultFactory.CreateStateIdentityProofFailure(
                invocation,
                selectedWindow,
                "Computer Use for Windows не смог подтвердить стабильную process identity окна; approval и observation fail-close-ятся до нового live proof.");
        }

        string appId = stableAppId!;
        if (!approvalStore.IsApproved(appId))
        {
            if (!request.Confirm)
            {
                return ComputerUseWinToolResultFactory.CreateStateApprovalRequired(invocation, selectedWindow, appId);
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
            return ComputerUseWinToolResultFactory.CreateStateFailure(
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
}
