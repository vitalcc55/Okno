using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.IntegrationTests;

internal static class WindowToolTestData
{
    public static MonitorInfo CreateMonitor(
        string monitorId = "display-source:0000000100000000:1",
        string friendlyName = "Primary monitor",
        string gdiDeviceName = @"\\.\DISPLAY1",
        bool isPrimary = true,
        long handle = 501) =>
        new(
            new MonitorDescriptor(
                MonitorId: monitorId,
                FriendlyName: friendlyName,
                GdiDeviceName: gdiDeviceName,
                Bounds: new Bounds(0, 0, 1920, 1080),
                WorkArea: new Bounds(0, 0, 1920, 1040),
                DpiScale: 1.0,
                IsPrimary: isPrimary),
            handle,
            [handle]);
}

internal sealed class FakeMonitorManager(
    IReadOnlyList<MonitorInfo>? monitors = null,
    IReadOnlyDictionary<long, string>? windowToMonitorMap = null) : IMonitorManager
{
    private readonly IReadOnlyList<MonitorInfo> _monitors = monitors ?? [WindowToolTestData.CreateMonitor()];
    private readonly IReadOnlyDictionary<long, string> _windowToMonitorMap = windowToMonitorMap ?? new Dictionary<long, string>();

    public IReadOnlyList<MonitorInfo> ListMonitors() => _monitors;

    public MonitorInfo? FindMonitorById(string monitorId) =>
        _monitors.FirstOrDefault(
            monitor => string.Equals(
                monitor.Descriptor.MonitorId,
                monitorId,
                StringComparison.OrdinalIgnoreCase));

    public MonitorInfo? FindMonitorByHandle(long handle, IReadOnlyList<MonitorInfo>? monitors = null)
    {
        IReadOnlyList<MonitorInfo> source = monitors ?? _monitors;
        return source.FirstOrDefault(monitor => monitor.Handles.Contains(handle));
    }

    public long? GetMonitorHandleForWindow(long hwnd)
    {
        MonitorInfo? monitor = FindMonitorForWindow(hwnd);
        return monitor?.CaptureHandle;
    }

    public MonitorInfo? FindMonitorForWindow(long hwnd)
    {
        if (_windowToMonitorMap.TryGetValue(hwnd, out string? monitorId))
        {
            return FindMonitorById(monitorId);
        }

        return _monitors.Count > 0 ? _monitors[0] : null;
    }

    public MonitorInfo? GetPrimaryMonitor()
    {
        for (int index = 0; index < _monitors.Count; index++)
        {
            if (_monitors[index].Descriptor.IsPrimary)
            {
                return _monitors[index];
            }
        }

        return _monitors.Count > 0 ? _monitors[0] : null;
    }
}

internal sealed class FakeWindowActivationService(Func<WindowDescriptor, ActivateWindowResult>? handler = null) : IWindowActivationService
{
    public long? LastHwnd { get; private set; }

    public Task<ActivateWindowResult> ActivateAsync(WindowDescriptor targetWindow, CancellationToken cancellationToken)
    {
        LastHwnd = targetWindow.Hwnd;
        if (handler is null)
        {
            throw new NotSupportedException("ActivateWindow не должен вызываться в этом тесте.");
        }

        return Task.FromResult(handler(targetWindow));
    }
}
