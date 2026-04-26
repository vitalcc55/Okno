using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinWindowContinuityProof
{
    // Windows does not expose a non-reusable public instance id for top-level HWNDs here,
    // so computer-use-win treats windowId as a discovery-scoped opaque handle and fails
    // closed on any snapshot drift instead of silently retargeting a replacement window.
    public static bool MatchesDiscoverySnapshot(WindowDescriptor liveWindow, WindowDescriptor discoveredWindow)
    {
        ArgumentNullException.ThrowIfNull(liveWindow);
        ArgumentNullException.ThrowIfNull(discoveredWindow);

        return liveWindow.Hwnd == discoveredWindow.Hwnd
            && WindowIdentityValidator.MatchesStableIdentity(liveWindow, discoveredWindow)
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
}
