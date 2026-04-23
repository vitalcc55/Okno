using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinGetAppStateTargetResolution(
    WindowDescriptor? Window,
    string? FailureCode = null,
    string? Reason = null)
{
    public bool IsSuccess => Window is not null && string.IsNullOrWhiteSpace(FailureCode);

    public static ComputerUseWinGetAppStateTargetResolution Success(WindowDescriptor window) =>
        new(window);

    public static ComputerUseWinGetAppStateTargetResolution Failure(string failureCode, string reason, WindowDescriptor? window = null) =>
        new(window, failureCode, reason);
}

internal static class ComputerUseWinGetAppStateTargetResolver
{
    public static ComputerUseWinGetAppStateTargetResolution Resolve(
        IReadOnlyList<WindowDescriptor> windows,
        ISessionManager sessionManager,
        string? appId,
        long? hwnd)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(sessionManager);

        if (hwnd is not null)
        {
            WindowDescriptor? explicitWindow = windows.SingleOrDefault(item => item.Hwnd == hwnd.Value);
            return explicitWindow is null
                ? ComputerUseWinGetAppStateTargetResolution.Failure(
                    ComputerUseWinFailureCodeValues.MissingTarget,
                    "Окно по указанному hwnd не найдено.")
                : ComputerUseWinGetAppStateTargetResolution.Success(explicitWindow);
        }

        if (!string.IsNullOrWhiteSpace(appId))
        {
            WindowDescriptor[] candidates = windows
                .Where(item => ComputerUseWinAppIdentity.TryCreateStableAppId(item, out string? candidateAppId)
                    && string.Equals(candidateAppId, appId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 0)
            {
                return ComputerUseWinGetAppStateTargetResolution.Failure(
                    ComputerUseWinFailureCodeValues.MissingTarget,
                    $"App '{appId}' не найдена среди текущих окон.");
            }

            WindowDescriptor[] foregroundCandidates = candidates.Where(static item => item.IsForeground).ToArray();
            if (foregroundCandidates.Length == 1)
            {
                return ComputerUseWinGetAppStateTargetResolution.Success(foregroundCandidates[0]);
            }

            if (candidates.Length == 1)
            {
                return ComputerUseWinGetAppStateTargetResolution.Success(candidates[0]);
            }

            return ComputerUseWinGetAppStateTargetResolution.Failure(
                ComputerUseWinFailureCodeValues.AmbiguousTarget,
                $"App '{appId}' соответствует нескольким окнам; укажи hwnd или сфокусируй нужное окно.");
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
                    return ComputerUseWinGetAppStateTargetResolution.Failure(
                        ComputerUseWinFailureCodeValues.IdentityProofUnavailable,
                        "Computer Use for Windows не смог подтвердить стабильную process identity attached window; повтори get_app_state после нового live proof.",
                        liveAttached);
                }

                if (WindowIdentityValidator.MatchesStableIdentity(liveAttached, attachedWindow))
                {
                    return ComputerUseWinGetAppStateTargetResolution.Success(liveAttached);
                }
            }
        }

        return ComputerUseWinGetAppStateTargetResolution.Failure(
            ComputerUseWinFailureCodeValues.MissingTarget,
            "Для get_app_state нужно передать appId или hwnd, либо сначала иметь актуальный attached window.");
    }
}
