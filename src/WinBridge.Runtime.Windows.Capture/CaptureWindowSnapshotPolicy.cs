using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Capture;

internal static class CaptureWindowSnapshotPolicy
{
    public static WindowDescriptor BuildRefreshedWindowSnapshot(
        WindowDescriptor requestWindow,
        WindowDescriptor? liveWindow,
        Bounds frameBounds,
        int effectiveDpi,
        double dpiScale,
        MonitorInfo? monitor)
    {
        WindowDescriptor sourceWindow = liveWindow is not null && liveWindow.Hwnd == requestWindow.Hwnd
            ? liveWindow
            : requestWindow with
            {
                ProcessId = null,
                ThreadId = null,
                ClassName = null,
            };

        return sourceWindow with
        {
            Bounds = frameBounds,
            EffectiveDpi = effectiveDpi,
            DpiScale = dpiScale,
            MonitorId = monitor?.Descriptor.MonitorId,
            MonitorFriendlyName = monitor?.Descriptor.FriendlyName,
        };
    }
}
