using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

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
                IsPrimary: isPrimary),
            handle,
            [handle]);
}

internal sealed class FakeMonitorManager(
    IReadOnlyList<MonitorInfo>? monitors = null,
    DisplayIdentityDiagnostics? diagnostics = null,
    IReadOnlyDictionary<long, string>? windowToMonitorMap = null) : IMonitorManager
{
    private readonly IReadOnlyList<MonitorInfo> _monitors = monitors ?? [WindowToolTestData.CreateMonitor()];
    private readonly DisplayIdentityDiagnostics _diagnostics = diagnostics ?? new(
        IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
        FailedStage: null,
        ErrorCode: null,
        ErrorName: null,
        MessageHuman: "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
        CapturedAtUtc: DateTimeOffset.UtcNow);
    private readonly IReadOnlyDictionary<long, string> _windowToMonitorMap = windowToMonitorMap ?? new Dictionary<long, string>();

    public DisplayTopologySnapshot GetTopologySnapshot() => new(_monitors, _diagnostics);

    public MonitorInfo? FindMonitorById(string monitorId, DisplayTopologySnapshot? snapshot = null) =>
        (snapshot?.Monitors ?? _monitors).FirstOrDefault(
            monitor => string.Equals(
                monitor.Descriptor.MonitorId,
                monitorId,
                StringComparison.OrdinalIgnoreCase));

    public MonitorInfo? FindMonitorByHandle(long handle, DisplayTopologySnapshot? snapshot = null)
    {
        IReadOnlyList<MonitorInfo> source = snapshot?.Monitors ?? _monitors;
        return source.FirstOrDefault(monitor => monitor.Handles.Contains(handle));
    }

    public long? GetMonitorHandleForWindow(long hwnd)
    {
        MonitorInfo? monitor = FindMonitorForWindow(hwnd);
        return monitor?.CaptureHandle;
    }

    public MonitorInfo? FindMonitorForWindow(long hwnd, DisplayTopologySnapshot? snapshot = null)
    {
        if (_windowToMonitorMap.TryGetValue(hwnd, out string? monitorId))
        {
            return FindMonitorById(monitorId, snapshot);
        }

        IReadOnlyList<MonitorInfo> source = snapshot?.Monitors ?? _monitors;
        return source.Count > 0 ? source[0] : null;
    }

    public MonitorInfo? GetPrimaryMonitor(DisplayTopologySnapshot? snapshot = null)
    {
        IReadOnlyList<MonitorInfo> source = snapshot?.Monitors ?? _monitors;
        for (int index = 0; index < source.Count; index++)
        {
            if (source[index].Descriptor.IsPrimary)
            {
                return source[index];
            }
        }

        return source.Count > 0 ? source[0] : null;
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

internal sealed class FakeUiAutomationService(
    Func<WindowDescriptor, UiaSnapshotRequest, CancellationToken, Task<UiaSnapshotResult>>? handler = null) : IUiAutomationService
{
    public int Calls { get; private set; }

    public WindowDescriptor? LastWindow { get; private set; }

    public UiaSnapshotRequest? LastRequest { get; private set; }

    public Task<UiaSnapshotResult> SnapshotAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastWindow = targetWindow;
        LastRequest = request;

        if (handler is null)
        {
            throw new NotSupportedException("UIA snapshot не должен вызываться в этом тесте.");
        }

        return handler(targetWindow, request, cancellationToken);
    }
}
