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
            windows: [CreateWindow(hwnd: 101, isForeground: true)],
            onFocus: hwnd => platform.ForegroundWindow = hwnd);
        WindowTargetResolver resolver = new(windowManager);
        WindowActivationService service = new(
            windowManager,
            resolver,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(CreateWindow(hwnd: 101), CancellationToken.None);

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
            windows: [CreateWindow(hwnd: 202, isForeground: false)],
            onFocus: _ => { });
        WindowTargetResolver resolver = new(windowManager);
        WindowActivationService service = new(
            windowManager,
            resolver,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(CreateWindow(hwnd: 202), CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.True(result.WasMinimized);
        Assert.False(result.IsForeground);
    }

    [Fact]
    public async Task ActivateAsyncReturnsFailedWhenForegroundSnapshotIsStaleAfterPoll()
    {
        FakeWindowActivationPlatform platform = new(
            windowExists: true,
            iconic: false,
            foregroundWindow: 0,
            foregroundSequence: [212, 999]);
        FakeWindowManager windowManager = new(
            windows: [CreateWindow(hwnd: 212, isForeground: true)],
            onFocus: hwnd => platform.ForegroundWindow = hwnd);
        WindowTargetResolver resolver = new(windowManager);
        WindowActivationService service = new(
            windowManager,
            resolver,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(CreateWindow(hwnd: 212), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.False(result.WasMinimized);
        Assert.False(result.IsForeground);
        Assert.NotNull(result.Window);
    }

    [Fact]
    public async Task ActivateAsyncReturnsAmbiguousWhenFinalSnapshotIsStillMinimized()
    {
        FakeWindowActivationPlatform platform = new(
            windowExists: true,
            iconic: true,
            foregroundWindow: 0,
            iconicSequence: [true, false, true]);
        FakeWindowManager windowManager = new(
            windows: [CreateWindow(hwnd: 214, isForeground: true)],
            onFocus: hwnd => platform.ForegroundWindow = hwnd);
        WindowTargetResolver resolver = new(windowManager);
        WindowActivationService service = new(
            windowManager,
            resolver,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(CreateWindow(hwnd: 214), CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.True(result.WasMinimized);
        Assert.False(result.IsForeground);
        Assert.NotNull(result.Window);
    }

    [Fact]
    public async Task ActivateAsyncReturnsFailedWhenIdentityChangesDuringFinalVerification()
    {
        WindowDescriptor originalWindow = CreateWindow(hwnd: 216, isForeground: true);
        FakeWindowManager windowManager = new(
            windows: [originalWindow],
            onFocus: _ => { });
        FakeWindowActivationPlatform platform = new(
            windowExists: true,
            iconic: false,
            foregroundWindow: 216,
            snapshotFactory: _ => new ActivatedWindowVerificationSnapshot(
                Exists: true,
                ProcessId: 999,
                ThreadId: 888,
                ClassName: "ReplacementWindow",
                IsForeground: true,
                IsMinimized: false));
        WindowTargetResolver resolver = new(windowManager);
        WindowActivationService service = new(
            windowManager,
            resolver,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(originalWindow, CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("identity", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.Window);
    }

    [Fact]
    public async Task ActivateAsyncReturnsFailedWhenWindowNoLongerExists()
    {
        FakeWindowActivationPlatform platform = new(windowExists: false, iconic: false, foregroundWindow: 0);
        FakeWindowManager windowManager = new(windows: []);
        WindowTargetResolver resolver = new(windowManager);
        WindowActivationService service = new(
            windowManager,
            resolver,
            platform,
            new WindowActivationOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));

        ActivateWindowResult result = await service.ActivateAsync(CreateWindow(hwnd: 303), CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("больше не найдено", result.Reason, StringComparison.Ordinal);
    }

    private static WindowDescriptor CreateWindow(
        long hwnd,
        bool isForeground = false,
        string windowState = WindowStateValues.Normal) =>
        new(
            Hwnd: hwnd,
            Title: "Window",
            ProcessName: "okno-tests",
            ProcessId: 123,
            ThreadId: 456,
            ClassName: "OknoWindow",
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: isForeground,
            IsVisible: true,
            WindowState: windowState,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");

    private sealed class FakeWindowManager(
        IReadOnlyList<WindowDescriptor>? windows = null,
        Func<IReadOnlyList<WindowDescriptor>>? windowsProvider = null,
        Action<long>? onFocus = null) : IWindowManager
    {
        private IReadOnlyList<WindowDescriptor> CurrentWindows =>
            windowsProvider?.Invoke() ?? windows ?? [];

        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => CurrentWindows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return CurrentWindows.FirstOrDefault(window => window.Hwnd == selector.Hwnd);
        }

        public bool TryFocus(long hwnd)
        {
            onFocus?.Invoke(hwnd);
            return CurrentWindows.Any(window => window.Hwnd == hwnd);
        }
    }

    private sealed class FakeWindowActivationPlatform(
        bool windowExists,
        bool iconic,
        long foregroundWindow,
        IReadOnlyList<long>? foregroundSequence = null,
        IReadOnlyList<bool>? iconicSequence = null,
        Func<long, ActivatedWindowVerificationSnapshot>? snapshotFactory = null,
        Action? onGetForegroundWindow = null) : IWindowActivationPlatform
    {
        public long ForegroundWindow { get; set; } = foregroundWindow;
        public bool RestoreCalled { get; private set; }
        private readonly Queue<long> foregroundStates = foregroundSequence is null ? new() : new Queue<long>(foregroundSequence);
        private readonly Queue<bool> iconicStates = iconicSequence is null ? new() : new Queue<bool>(iconicSequence);

        public bool IsWindow(long hwnd) => windowExists;

        public bool IsIconic(long hwnd)
        {
            if (iconicStates.Count > 0)
            {
                iconic = iconicStates.Dequeue();
            }

            return iconic;
        }

        public void RestoreWindow(long hwnd)
        {
            RestoreCalled = true;
            iconic = false;
        }

        public long GetForegroundWindow()
        {
            onGetForegroundWindow?.Invoke();

            if (foregroundStates.Count > 0)
            {
                ForegroundWindow = foregroundStates.Dequeue();
            }

            return ForegroundWindow;
        }

        public ActivatedWindowVerificationSnapshot ProbeWindow(long hwnd)
        {
            if (snapshotFactory is not null)
            {
                return snapshotFactory(hwnd);
            }

            return new(
                Exists: IsWindow(hwnd),
                ProcessId: 123,
                ThreadId: 456,
                ClassName: "OknoWindow",
                IsForeground: GetForegroundWindow() == hwnd,
                IsMinimized: IsIconic(hwnd));
        }
    }
}
