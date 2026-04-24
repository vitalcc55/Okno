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
            return ActivateWindowResult.Failed(
                "Окно для активации больше не найдено.",
                wasMinimized: false,
                failureKind: ActivationFailureKindValues.MissingTarget);
        }

        bool wasMinimized = platform.IsIconic(hwnd);
        if (wasMinimized)
        {
            platform.RestoreWindow(hwnd);
            bool restored = await WaitUntilAsync(() => !platform.IsIconic(hwnd), options.RestoreTimeout, cancellationToken).ConfigureAwait(false);
            if (!restored)
            {
                return ActivateWindowResult.Failed(
                    "Окно осталось свернутым после restore.",
                    wasMinimized,
                    failureKind: ActivationFailureKindValues.RestoreFailedStillMinimized);
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
            return ActivateWindowResult.Failed(verification.FailureReason!, wasMinimized, failureKind: verification.FailureKind!);
        }

        if (IsUsableActivatedWindow(verification))
        {
            return ActivateWindowResult.Done(verification.ResolvedWindow, wasMinimized, isForeground: true);
        }

        return CreateUnconfirmedResult(verification.ResolvedWindow, verification, wasMinimized, isForeground);
    }

    private ActivatedWindowVerificationResult VerifyActivatedWindow(WindowDescriptor expectedWindow)
    {
        LiveWindowIdentityResolution resolution = windowTargetResolver.ResolveLiveWindowByIdentity(expectedWindow);
        if (!resolution.IsResolved)
        {
            return new(
                null,
                false,
                false,
                false,
                false,
                resolution.Reason ?? "Окно для активации больше не найдено или больше не совпадает с исходной identity.",
                FailureKind: resolution.FailureKind ?? ActivationFailureKindValues.PreflightFailed);
        }

        WindowDescriptor resolvedWindow = resolution.Window!;
        ActivatedWindowVerificationSnapshot finalSnapshot = platform.ProbeWindow(resolvedWindow.Hwnd);
        if (!finalSnapshot.Exists)
        {
            return new(
                null,
                false,
                false,
                finalSnapshot.IsForeground,
                finalSnapshot.IsMinimized,
                "Окно исчезло до завершения финальной activation verification.",
                FailureKind: ActivationFailureKindValues.MissingTarget);
        }

        if (!WindowIdentityValidator.MatchesStableIdentity(finalSnapshot, expectedWindow))
        {
            return new(
                null,
                false,
                false,
                finalSnapshot.IsForeground,
                finalSnapshot.IsMinimized,
                "Окно для активации больше не найдено или больше не совпадает с исходной identity в финальном activation snapshot.",
                FailureKind: ActivationFailureKindValues.IdentityChanged);
        }

        return new(
            ApplyFinalSnapshot(resolvedWindow, finalSnapshot),
            true,
            true,
            finalSnapshot.IsForeground,
            finalSnapshot.IsMinimized,
            null,
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

        string failureKind = verification.IsMinimized
            ? ActivationFailureKindValues.RestoreFailedStillMinimized
            : ActivationFailureKindValues.ForegroundNotConfirmed;
        return wasMinimized
            ? ActivateWindowResult.Ambiguous(reason, window, wasMinimized, isForeground: false, failureKind: failureKind)
            : ActivateWindowResult.Failed(reason, window, wasMinimized, isForeground: false, failureKind: failureKind);
    }

    private readonly record struct ActivatedWindowVerificationResult(
        WindowDescriptor? ResolvedWindow,
        bool IdentityConfirmed,
        bool Exists,
        bool IsForeground,
        bool IsMinimized,
        string? FailureReason,
        string? FailureKind);

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
