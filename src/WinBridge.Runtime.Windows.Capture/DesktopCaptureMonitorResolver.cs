using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Capture;

internal static class DesktopCaptureMonitorResolver
{
    public static MonitorInfo? Resolve(
        WindowDescriptor? window,
        string? explicitMonitorId,
        IMonitorManager monitorManager,
        DisplayTopologySnapshot topology)
    {
        return !string.IsNullOrWhiteSpace(explicitMonitorId)
            ? monitorManager.FindMonitorById(explicitMonitorId, topology)
            : window is null
                ? monitorManager.GetPrimaryMonitor(topology)
                : monitorManager.FindMonitorForWindow(window.Hwnd, topology);
    }
}
