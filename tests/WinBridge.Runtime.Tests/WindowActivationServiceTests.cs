using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class WindowActivationServiceTests
{
    [Fact]
    public async Task ActivateAsyncReturnsDoneWhenMinimizedWindowIsRestoredAndFocused()
    {
        FakeWindowActivationPlatform platform = new(windowExists: true, iconic: true, foregroundWindow: 0);
        FakeWindowManager windowManager = new(
            windows: [CreateWindow(hwnd: 101)],
            onFocus: hwnd => platform.ForegroundWindow = hwnd);
        WindowActivationService service = new(
            windowManager,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(101, CancellationToken.None);

        Assert.Equal("done", result.Status);
        Assert.True(result.WasMinimized);
        Assert.True(result.IsForeground);
        Assert.True(platform.RestoreCalled);
    }

    [Fact]
    public async Task ActivateAsyncReturnsAmbiguousWhenRestoreSucceedsButForegroundIsNotConfirmed()
    {
        FakeWindowActivationPlatform platform = new(windowExists: true, iconic: true, foregroundWindow: 999);
        FakeWindowManager windowManager = new(
            windows: [CreateWindow(hwnd: 202)],
            onFocus: _ => { });
        WindowActivationService service = new(
            windowManager,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(202, CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.True(result.WasMinimized);
        Assert.False(result.IsForeground);
    }

    [Fact]
    public async Task ActivateAsyncReturnsFailedWhenWindowNoLongerExists()
    {
        FakeWindowActivationPlatform platform = new(windowExists: false, iconic: false, foregroundWindow: 0);
        FakeWindowManager windowManager = new(windows: []);
        WindowActivationService service = new(
            windowManager,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(303, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("больше не найдено", result.Reason, StringComparison.Ordinal);
    }

    private static WindowDescriptor CreateWindow(long hwnd) =>
        new(
            Hwnd: hwnd,
            Title: "Window",
            ProcessName: "okno-tests",
            ProcessId: 123,
            ThreadId: 456,
            ClassName: "OknoWindow",
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: false,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");

    private sealed class FakeWindowManager(
        IReadOnlyList<WindowDescriptor> windows,
        Action<long>? onFocus = null) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => window.Hwnd == selector.Hwnd);
        }

        public bool TryFocus(long hwnd)
        {
            onFocus?.Invoke(hwnd);
            return windows.Any(window => window.Hwnd == hwnd);
        }
    }

    private sealed class FakeWindowActivationPlatform(
        bool windowExists,
        bool iconic,
        long foregroundWindow) : IWindowActivationPlatform
    {
        public long ForegroundWindow { get; set; } = foregroundWindow;
        public bool RestoreCalled { get; private set; }

        public bool IsWindow(long hwnd) => windowExists;

        public bool IsIconic(long hwnd) => iconic;

        public void RestoreWindow(long hwnd)
        {
            RestoreCalled = true;
            iconic = false;
        }

        public long GetForegroundWindow() => ForegroundWindow;
    }
}
