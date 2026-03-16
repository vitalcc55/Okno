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

        WindowDescriptor? window = windowTargetResolver.ResolveLiveWindowByIdentity(targetWindow);
        if (window is null)
        {
            return Failed("Окно для активации больше не найдено или больше не совпадает с исходной identity.", wasMinimized);
        }

        if (isForeground)
        {
            return new ActivateWindowResult("done", null, window, wasMinimized, true);
        }

        string reason = wasMinimized
            ? "Окно восстановлено, но foreground focus не удалось подтвердить."
            : "Windows отказалась перевести окно в foreground.";
        string status = wasMinimized ? "ambiguous" : "failed";
        return new ActivateWindowResult(status, reason, window, wasMinimized, false);
    }

    private static ActivateWindowResult Failed(string reason, bool wasMinimized) =>
        new("failed", reason, null, wasMinimized, false);

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
