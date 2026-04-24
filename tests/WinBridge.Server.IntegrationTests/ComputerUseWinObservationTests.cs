using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinObservationTests
{
    private const string WindowId = "cw_test_window";

    [Fact]
    public async Task AppStateObserverReturnsStructuredFailureWhenCaptureThrows()
    {
        ComputerUseWinAppStateObserver observer = CreateObserver(
            captureService: new ThrowingCaptureService(new CaptureOperationException("Свернутое окно нельзя использовать для window capture.")),
            uiAutomationService: new FakeUiAutomationService());

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            windowId: WindowId,
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
            windowId: WindowId,
            maxNodes: 2048,
            warnings: [],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, outcome.FailureCode);
        Assert.Contains("maxNodes", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppStateObserverMaterializesUnexpectedSnapshotExceptionsAsStructuredFailure()
    {
        ComputerUseWinAppStateObserver observer = CreateObserver(
            captureService: new SuccessfulCaptureService(),
            uiAutomationService: new FakeUiAutomationService((_, _, _) => throw new InvalidOperationException("secret uia failure")));

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            windowId: WindowId,
            maxNodes: 128,
            warnings: [],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, outcome.FailureCode);
        Assert.DoesNotContain("secret", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(outcome.FailureDetails);
        Assert.IsType<InvalidOperationException>(outcome.FailureDetails!.AuditException);
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
            windowId: WindowId,
            maxNodes: 128,
            warnings: ["activation degraded"],
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.PreparedState);
        Assert.Equal(128, outcome.PreparedState!.StoredState.Observation.RequestedMaxNodes);
        ComputerUseWinGetAppStateResult payload = outcome.PreparedState.CreatePayload("token-1");
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.Status);
        Assert.Equal("token-1", payload.StateToken);
        Assert.Contains("activation degraded", payload.Warnings!);
        Assert.Contains(payload.AccessibilityTree!, element => element.HasKeyboardFocus);
    }

    [Fact]
    public async Task AppStateObserverTreatsAdvisoryInstructionFailureAsWarningWithoutStateCommit()
    {
        MutableTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 21, 18, 0, 0, TimeSpan.Zero));
        ComputerUseWinStateStore stateStore = new(timeProvider, TimeSpan.FromSeconds(30), maxEntries: 1);
        string existingToken = stateStore.Create(CreateStoredState(timeProvider.GetUtcNow()));
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
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow))),
            instructionProvider: new ThrowingInstructionProvider());

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            windowId: WindowId,
            maxNodes: 128,
            warnings: [],
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.PreparedState);
        Assert.Empty(outcome.PreparedState!.Instructions);
        Assert.Contains(outcome.PreparedState.Warnings, warning => warning.Contains("advisory instructions", StringComparison.OrdinalIgnoreCase));
        Assert.True(stateStore.TryGet(existingToken, out _));
    }

    [Fact]
    public async Task AppStateObserverTreatsUnexpectedInstructionProviderBugAsStructuredFailure()
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
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow))),
            instructionProvider: new BuggyInstructionProvider());

        ComputerUseWinAppStateObservationOutcome outcome = await observer.ObserveAsync(
            CreateWindow(),
            appId: "explorer",
            windowId: WindowId,
            maxNodes: 128,
            warnings: [],
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, outcome.FailureCode);
        Assert.DoesNotContain("secret", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(outcome.FailureDetails?.AuditException);
        Assert.IsType<InvalidOperationException>(outcome.FailureDetails!.AuditException);
    }

    private static ComputerUseWinAppStateObserver CreateObserver(
        ICaptureService captureService,
        FakeUiAutomationService uiAutomationService,
        IComputerUseWinInstructionProvider? instructionProvider = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return new ComputerUseWinAppStateObserver(
            captureService,
            uiAutomationService,
            instructionProvider ?? new ComputerUseWinPlaybookProvider(
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

    private static ComputerUseWinStoredState CreateStoredState(DateTimeOffset capturedAtUtc) =>
        new(
            new ComputerUseWinAppSession("explorer", WindowId, 101, "Explorer", "explorer", 1001),
            CreateWindow(),
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 128),
            CapturedAtUtc: capturedAtUtc);

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

    private sealed class ThrowingInstructionProvider : IComputerUseWinInstructionProvider
    {
        public IReadOnlyList<string> GetInstructions(string? processName) =>
            throw new ComputerUseWinInstructionUnavailableException(
                "Computer Use for Windows не смог прочитать advisory instructions для этого приложения.",
                new IOException("instructions unavailable"));
    }

    private sealed class BuggyInstructionProvider : IComputerUseWinInstructionProvider
    {
        public IReadOnlyList<string> GetInstructions(string? processName) =>
            throw new InvalidOperationException("secret provider bug");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan delta) => current = current.Add(delta);
    }
}
