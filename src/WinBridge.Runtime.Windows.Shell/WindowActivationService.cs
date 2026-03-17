using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class WindowActivationService(
    IWindowManager windowManager,
    IWindowTargetResolver windowTargetResolver,
    IWindowActivationPlatform platform,
    WindowActivationOptions options) : IWindowActivationService
{
    public async Task<ActivateWindowResult> ActivateAsync(WindowDescriptor targetWindow, CancellationToken cancellationToken)
    {
        long hwnd = targetWindow.Hwnd;
        if (!platform.IsWindow(hwnd))
        {
            return Failed("Окно для активации больше не найдено.", wasMinimized: false);
        }

        bool wasMinimized = platform.IsIconic(hwnd);
        if (wasMinimized)
        {
            platform.RestoreWindow(hwnd);
            bool restored = await WaitUntilAsync(() => !platform.IsIconic(hwnd), options.RestoreTimeout, cancellationToken).ConfigureAwait(false);
            if (!restored)
            {
                return Failed("Окно осталось свернутым после restore.", wasMinimized);
            }
        }

        bool isForeground = await WaitUntilAsync(
            () =>
            {
                _ = windowManager.TryFocus(hwnd);
                return platform.GetForegroundWindow() == hwnd;
            },
            options.FocusTimeout,
            cancellationToken).ConfigureAwait(false);

        ActivatedWindowVerificationResult verification = VerifyActivatedWindow(targetWindow);
        if (!verification.IdentityConfirmed || !verification.Exists || verification.ResolvedWindow is null)
        {
            return Failed(verification.FailureReason!, wasMinimized);
        }

        if (IsUsableActivatedWindow(verification))
        {
            return new ActivateWindowResult("done", null, verification.ResolvedWindow, wasMinimized, true);
        }

        return CreateUnconfirmedResult(verification.ResolvedWindow, verification, wasMinimized, isForeground);
    }

    private static ActivateWindowResult Failed(string reason, bool wasMinimized) =>
        new("failed", reason, null, wasMinimized, false);

    private ActivatedWindowVerificationResult VerifyActivatedWindow(WindowDescriptor expectedWindow)
    {
        WindowDescriptor? resolvedWindow = windowTargetResolver.ResolveLiveWindowByIdentity(expectedWindow);
        if (resolvedWindow is null)
        {
            return new(null, false, false, false, false, "Окно для активации больше не найдено или больше не совпадает с исходной identity.");
        }

        ActivatedWindowVerificationSnapshot finalSnapshot = platform.ProbeWindow(resolvedWindow.Hwnd);
        if (!finalSnapshot.Exists)
        {
            return new(null, false, false, finalSnapshot.IsForeground, finalSnapshot.IsMinimized, "Окно исчезло до завершения финальной activation verification.");
        }

        if (!WindowIdentityValidator.MatchesStableIdentity(finalSnapshot, expectedWindow))
        {
            return new(null, false, false, finalSnapshot.IsForeground, finalSnapshot.IsMinimized, "Окно для активации больше не найдено или больше не совпадает с исходной identity в финальном activation snapshot.");
        }

        return new(
            ApplyFinalSnapshot(resolvedWindow, finalSnapshot),
            true,
            true,
            finalSnapshot.IsForeground,
            finalSnapshot.IsMinimized,
            null);
    }

    private static WindowDescriptor ApplyFinalSnapshot(WindowDescriptor window, ActivatedWindowVerificationSnapshot snapshot) =>
        window with
        {
            ProcessId = snapshot.ProcessId,
            ThreadId = snapshot.ThreadId,
            ClassName = snapshot.ClassName,
            IsForeground = snapshot.IsForeground,
            WindowState = snapshot.IsMinimized
                ? WindowStateValues.Minimized
                : string.Equals(window.WindowState, WindowStateValues.Minimized, StringComparison.Ordinal)
                    ? WindowStateValues.Normal
                    : window.WindowState,
        };

    private static bool IsUsableActivatedWindow(ActivatedWindowVerificationResult verification) =>
        verification.IsForeground && !verification.IsMinimized;

    private static ActivateWindowResult CreateUnconfirmedResult(
        WindowDescriptor window,
        ActivatedWindowVerificationResult verification,
        bool wasMinimized,
        bool pollObservedForeground)
    {
        string reason = verification.IsMinimized
            ? "Окно снова оказалось свернутым до завершения активации."
            : wasMinimized
                ? "Окно восстановлено, но foreground focus не удалось подтвердить по финальному live-state."
                : pollObservedForeground
                    ? "Окно кратковременно было foreground, но финальный live-state не подтвердил usability."
                    : "Windows отказалась перевести окно в foreground.";

        string status = wasMinimized ? "ambiguous" : "failed";
        return new ActivateWindowResult(status, reason, window, wasMinimized, false);
    }

    private readonly record struct ActivatedWindowVerificationResult(
        WindowDescriptor? ResolvedWindow,
        bool IdentityConfirmed,
        bool Exists,
        bool IsForeground,
        bool IsMinimized,
        string? FailureReason);

    private async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (condition())
        {
            return true;
        }

        DateTime deadlineUtc = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
            if (condition())
            {
                return true;
            }
        }

        return condition();
    }
}
