using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinObservationTests
{
    [Fact]
    public async Task AppStateObserverReturnsStructuredFailureWhenCaptureThrows()
    {
        ComputerUseWinAppStateObserver observer = CreateObserver(
            captureService: new ThrowingCaptureService(new CaptureOperationException("Свернутое окно нельзя использовать для window capture.")),
            uiAutomationService: new FakeUiAutomationService());

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            maxNodes: 128,
            warnings: [],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, outcome.FailureCode);
        Assert.Contains("Свернутое окно", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppStateObserverReturnsStructuredFailureWhenSnapshotDoesNotComplete()
    {
        ComputerUseWinAppStateObserver observer = CreateObserver(
            captureService: new SuccessfulCaptureService(),
            uiAutomationService: new FakeUiAutomationService((window, request, _) => Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Failed,
                    Reason: "Параметр maxNodes для UIA snapshot должен быть <= 1024.",
                    Window: CreateObservedWindow(window),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow))));

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            maxNodes: 2048,
            warnings: [],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, outcome.FailureCode);
        Assert.Contains("maxNodes", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppStateObserverPublishesKeyboardFocusAndStateTokenOnSuccess()
    {
        ComputerUseWinAppStateObserver observer = CreateObserver(
            captureService: new SuccessfulCaptureService(),
            uiAutomationService: new FakeUiAutomationService((window, request, _) => Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children =
                        [
                            new UiaElementSnapshot
                            {
                                ElementId = "child",
                                ControlType = "button",
                                Name = "Run semantic smoke",
                                BoundingRectangle = new Bounds(10, 20, 110, 50),
                                IsEnabled = true,
                                IsOffscreen = false,
                                HasKeyboardFocus = true,
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow))));

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            maxNodes: 128,
            warnings: ["activation degraded"],
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Payload);
        Assert.NotNull(outcome.PngBytes);
        Assert.Equal(ComputerUseWinStatusValues.Ok, outcome.Payload!.Status);
        Assert.NotNull(outcome.Payload.StateToken);
        Assert.Contains("activation degraded", outcome.Payload.Warnings!);
        Assert.Contains(outcome.Payload.AccessibilityTree!, element => element.HasKeyboardFocus);
    }

    private static ComputerUseWinAppStateObserver CreateObserver(
        ICaptureService captureService,
        FakeUiAutomationService uiAutomationService)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return new ComputerUseWinAppStateObserver(
            captureService,
            uiAutomationService,
            new ComputerUseWinStateStore(),
            new ComputerUseWinPlaybookProvider(
                new ComputerUseWinOptions(
                    PluginRoot: root,
                    AppInstructionsRoot: Path.Combine(root, "references", "AppInstructions"),
                    ApprovalStorePath: Path.Combine(root, "AppApprovals.json"))));
    }

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 101,
            Title: "Test window",
            ProcessName: "explorer",
            ProcessId: 1001,
            ThreadId: 2002,
            ClassName: "TestWindow",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: true,
            IsVisible: true);

    private static ObservedWindowDescriptor CreateObservedWindow(WindowDescriptor window) =>
        new(
            Hwnd: window.Hwnd,
            Title: window.Title,
            ProcessName: window.ProcessName,
            ProcessId: window.ProcessId,
            ThreadId: window.ThreadId,
            ClassName: window.ClassName,
            Bounds: window.Bounds,
            IsForeground: window.IsForeground,
            IsVisible: window.IsVisible,
            EffectiveDpi: window.EffectiveDpi,
            DpiScale: window.DpiScale,
            WindowState: window.WindowState,
            MonitorId: window.MonitorId,
            MonitorFriendlyName: window.MonitorFriendlyName);

    private sealed class ThrowingCaptureService(CaptureOperationException exception) : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            Task.FromException<CaptureResult>(exception);
    }

    private sealed class SuccessfulCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(
                    new CaptureResult(
                        new CaptureMetadata(
                            Scope: "window",
                            TargetKind: "window",
                            Hwnd: target.Window!.Hwnd,
                            Title: target.Window.Title,
                            ProcessName: target.Window.ProcessName,
                            Bounds: target.Window.Bounds,
                            CoordinateSpace: "physical_pixels",
                            PixelWidth: 320,
                            PixelHeight: 200,
                            CapturedAtUtc: DateTimeOffset.UtcNow,
                            ArtifactPath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
                            MimeType: "image/png",
                            ByteSize: 3,
                            SessionRunId: "tests",
                            EffectiveDpi: 96,
                            DpiScale: 1.0,
                            CaptureReference: null),
                    [1, 2, 3]));
    }
}
