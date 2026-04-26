using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinGetAppStateTargetResolution(
    ComputerUseWinExecutionTarget? Target,
    WindowDescriptor? FailureWindow = null,
    string? FailureCode = null,
    string? Reason = null)
{
    public WindowDescriptor? Window => Target?.Window ?? FailureWindow;

    public bool IsSuccess => Target is not null && string.IsNullOrWhiteSpace(FailureCode);

    public static ComputerUseWinGetAppStateTargetResolution Success(ComputerUseWinExecutionTarget target) =>
        new(target);

    public static ComputerUseWinGetAppStateTargetResolution Failure(string failureCode, string reason, WindowDescriptor? window = null) =>
        new(null, window, failureCode, reason);

    public static ComputerUseWinGetAppStateTargetResolution MissingExplicitHwnd() =>
        Failure(
            ComputerUseWinFailureCodeValues.MissingTarget,
            "Окно по указанному hwnd не найдено.");

    public static ComputerUseWinGetAppStateTargetResolution MissingWindowId() =>
        Failure(
            ComputerUseWinFailureCodeValues.MissingTarget,
            "Окно по указанному windowId не найдено среди текущих visible instances.");

    public static ComputerUseWinGetAppStateTargetResolution MissingSelector() =>
        Failure(
            ComputerUseWinFailureCodeValues.MissingTarget,
            "Для get_app_state нужно передать windowId или hwnd, либо сначала иметь актуальный attached window.");

    public static ComputerUseWinGetAppStateTargetResolution IdentityProofUnavailable(WindowDescriptor window) =>
        Failure(
            ComputerUseWinFailureCodeValues.IdentityProofUnavailable,
            "Computer Use for Windows не смог подтвердить instance identity окна; повтори get_app_state после нового live proof.",
            window);
}

internal static class ComputerUseWinGetAppStateTargetResolver
{
    public static ComputerUseWinGetAppStateTargetResolution Resolve(
        IReadOnlyList<WindowDescriptor> windows,
        ComputerUseWinExecutionTargetCatalog executionTargetCatalog,
        ISessionManager sessionManager,
        string? windowId,
        long? hwnd)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(executionTargetCatalog);
        ArgumentNullException.ThrowIfNull(sessionManager);

        if (hwnd is not null)
        {
            WindowDescriptor? explicitWindow = windows.SingleOrDefault(item => item.Hwnd == hwnd.Value);
            if (explicitWindow is null)
            {
                return ComputerUseWinGetAppStateTargetResolution.MissingExplicitHwnd();
            }

            _ = executionTargetCatalog.TryIssue(explicitWindow, out ComputerUseWinExecutionTarget? explicitTarget);
            return explicitTarget is null
                ? ComputerUseWinGetAppStateTargetResolution.IdentityProofUnavailable(explicitWindow)
                : ComputerUseWinGetAppStateTargetResolution.Success(explicitTarget);
        }

        if (!string.IsNullOrWhiteSpace(windowId))
        {
            bool resolved = executionTargetCatalog.TryResolveWindowId(
                windowId,
                windows,
                out ComputerUseWinExecutionTarget? selectedTarget,
                out WindowDescriptor? failureWindow,
                out bool continuityFailed);
            if (!resolved)
            {
                return continuityFailed
                    ? ComputerUseWinGetAppStateTargetResolution.IdentityProofUnavailable(failureWindow!)
                    : ComputerUseWinGetAppStateTargetResolution.MissingWindowId();
            }

            return ComputerUseWinGetAppStateTargetResolution.Success(selectedTarget!);
        }

        AttachedWindow? attached = sessionManager.GetAttachedWindow();
        if (attached?.Window is WindowDescriptor attachedWindow)
        {
            WindowDescriptor? liveAttached = windows.SingleOrDefault(item => item.Hwnd == attachedWindow.Hwnd);
            if (liveAttached is not null)
            {
                bool expectedHasStableIdentity = WindowIdentityValidator.TryValidateStableIdentity(attachedWindow, out _);
                bool liveHasStableIdentity = WindowIdentityValidator.TryValidateStableIdentity(liveAttached, out _);
                if (!expectedHasStableIdentity || !liveHasStableIdentity)
                {
                    return ComputerUseWinGetAppStateTargetResolution.IdentityProofUnavailable(liveAttached);
                }

                if (ComputerUseWinWindowContinuityProof.MatchesDiscoverySnapshot(liveAttached, attachedWindow))
                {
                    _ = executionTargetCatalog.TryIssue(liveAttached, out ComputerUseWinExecutionTarget? attachedTarget);
                    return attachedTarget is null
                        ? ComputerUseWinGetAppStateTargetResolution.IdentityProofUnavailable(liveAttached)
                        : ComputerUseWinGetAppStateTargetResolution.Success(attachedTarget);
                }

                return ComputerUseWinGetAppStateTargetResolution.IdentityProofUnavailable(liveAttached);
            }
        }

        return ComputerUseWinGetAppStateTargetResolution.MissingSelector();
    }
}
