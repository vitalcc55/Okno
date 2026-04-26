using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinWindowContinuityProof
{
    // Windows does not expose a non-reusable public instance id for top-level HWNDs here.
    // The product therefore uses different proof strengths for different paths:
    // - discovery selector (`windowId`) is strict and discovery-scoped;
    // - attached session refresh tolerates ordinary post-action UI drift;
    // - observed state revalidation currently follows the same instance continuity
    //   model as attached refresh, while keeping a separate seam for stricter future
    //   observed-state proof rules.
    public static bool MatchesDiscoverySelector(WindowDescriptor liveWindow, WindowDescriptor discoveredWindow)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(discoveredWindow);

        return MatchesInstanceContinuity(liveWindow, discoveredWindow)
            && string.Equals(liveWindow.ProcessName, discoveredWindow.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(liveWindow.Title, discoveredWindow.Title, StringComparison.Ordinal)
            && Equals(liveWindow.Bounds, discoveredWindow.Bounds)
            && liveWindow.IsVisible == discoveredWindow.IsVisible
            && liveWindow.EffectiveDpi == discoveredWindow.EffectiveDpi
            && liveWindow.DpiScale.Equals(discoveredWindow.DpiScale)
            && string.Equals(liveWindow.WindowState, discoveredWindow.WindowState, StringComparison.Ordinal)
            && string.Equals(liveWindow.MonitorId, discoveredWindow.MonitorId, StringComparison.Ordinal)
            && string.Equals(liveWindow.MonitorFriendlyName, discoveredWindow.MonitorFriendlyName, StringComparison.Ordinal);
    }

    public static bool MatchesAttachedSession(WindowDescriptor liveWindow, WindowDescriptor attachedWindow)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(attachedWindow);

        return MatchesInstanceContinuity(liveWindow, attachedWindow);
    }

    public static bool MatchesObservedState(WindowDescriptor liveWindow, WindowDescriptor observedWindow)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(observedWindow);

        return MatchesInstanceContinuity(liveWindow, observedWindow);
    }

    private static bool MatchesInstanceContinuity(WindowDescriptor liveWindow, WindowDescriptor expectedWindow) =>
        liveWindow.Hwnd == expectedWindow.Hwnd
        && WindowIdentityValidator.MatchesStableIdentity(liveWindow, expectedWindow)
        && string.Equals(liveWindow.ProcessName, expectedWindow.ProcessName, StringComparison.OrdinalIgnoreCase);
}
