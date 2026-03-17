using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

public interface IMonitorManager
{
    DisplayTopologySnapshot GetTopologySnapshot();

    MonitorInfo? FindMonitorById(string monitorId, DisplayTopologySnapshot? snapshot = null);

    MonitorInfo? FindMonitorByHandle(long handle, DisplayTopologySnapshot? snapshot = null);

    long? GetMonitorHandleForWindow(long hwnd);

    MonitorInfo? FindMonitorForWindow(long hwnd, DisplayTopologySnapshot? snapshot = null);

    MonitorInfo? GetPrimaryMonitor(DisplayTopologySnapshot? snapshot = null);
}
