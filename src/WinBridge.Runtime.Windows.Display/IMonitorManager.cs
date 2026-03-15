using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

public interface IMonitorManager
{
    IReadOnlyList<MonitorInfo> ListMonitors();

    MonitorInfo? FindMonitorById(string monitorId);

    MonitorInfo? FindMonitorByHandle(long handle, IReadOnlyList<MonitorInfo>? monitors = null);

    long? GetMonitorHandleForWindow(long hwnd);

    MonitorInfo? FindMonitorForWindow(long hwnd);

    MonitorInfo? GetPrimaryMonitor();
}
