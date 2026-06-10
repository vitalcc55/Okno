// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinObservationTests
{
    private const string AppId = "explorer";
    private const string WindowId = "cw_test_window";
    private const int DefaultMaxNodes = 128;
    private static readonly ICaptureService DefaultCaptureService = new SuccessfulCaptureService();

    [Fact]
    public async Task AppStateObserverReturnsStructuredFailureWhenCaptureThrows()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            captureService: new ThrowingCaptureService(new CaptureOperationException("Свернутое окно нельзя использовать для window capture.")));

        AssertObservationFailed(outcome);
        Assert.Contains("Свернутое окно", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppStateObserverReturnsStructuredFailureWhenSnapshotDoesNotComplete()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            uiAutomationService: SnapshotService(
                status: UiaSnapshotStatusValues.Failed,
                reason: "Параметр maxNodes для UIA snapshot должен быть <= 1024."),
            maxNodes: 2048);

        AssertObservationFailed(outcome);
        Assert.Contains("maxNodes", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppStateObserverSanitizesInitialSnapshotFailureReason()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            uiAutomationService: SnapshotService(
                status: UiaSnapshotStatusValues.Failed,
                reason: "secret raw provider invalid operation from UIA"));

        AssertObservationFailed(outcome);
        Assert.DoesNotContain("secret raw provider", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get_app_state", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppStateObserverReturnsVisualSuccessWhenSnapshotFailsAfterCapture()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            uiAutomationService: SnapshotService(
                status: UiaSnapshotStatusValues.Failed,
                reason: "raw provider traversal failure"));

        Assert.True(outcome.IsSuccess, outcome.Reason);
        Assert.NotNull(outcome.PreparedState);

        ComputerUseWinGetAppStateResult payload = outcome.PreparedState!.CreatePayload("visual-state-token");
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.Status);
        Assert.Equal("visual-state-token", payload.StateToken);
        Assert.Empty(payload.AccessibilityTree!);

        JsonElement json = JsonSerializer.SerializeToElement(
            payload,
            ComputerUseWinToolResultFactory.PayloadJsonOptions);
        Assert.True(json.TryGetProperty("semanticPreview", out JsonElement semanticPreview));
        Assert.Equal("failed", semanticPreview.GetProperty("status").GetString());
        Assert.Equal(UiaSnapshotDefaults.Depth, semanticPreview.GetProperty("requestedDepth").GetInt32());
        Assert.Equal(DefaultMaxNodes, semanticPreview.GetProperty("requestedMaxNodes").GetInt32());
        Assert.DoesNotContain("raw provider traversal failure", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppStateObserverMaterializesUnexpectedSnapshotExceptionsAsStructuredFailure()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            uiAutomationService: new FakeUiAutomationService((_, _, _) => throw new InvalidOperationException("secret uia failure")));

        AssertObservationFailed(outcome);
        Assert.DoesNotContain("secret", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        AssertAuditException<InvalidOperationException>(outcome);
    }

    [Fact]
    public async Task AppStateObserverPublishesKeyboardFocusAndStateTokenOnSuccess()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            uiAutomationService: SnapshotService(root: RootWindowElement(
                new UiaElementSnapshot
                {
                    ElementId = "child",
                    ControlType = "button",
                    Name = "Run semantic smoke",
                    BoundingRectangle = new Bounds(10, 20, 110, 50),
                    IsEnabled = true,
                    IsOffscreen = false,
                    HasKeyboardFocus = true,
                })),
            warnings: ["activation degraded"]);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.PreparedState);
        Assert.Equal(DefaultMaxNodes, outcome.PreparedState!.StoredState.Observation.RequestedMaxNodes);
        ComputerUseWinGetAppStateResult payload = outcome.PreparedState.CreatePayload("token-1");
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.Status);
        Assert.Equal("token-1", payload.StateToken);
        Assert.Contains("activation degraded", payload.Warnings!);
        Assert.Contains(payload.AccessibilityTree!, element => element.HasKeyboardFocus);
    }

    [Fact]
    public async Task AppStateObserverTreatsAdvisoryInstructionFailureAsWarningWithoutStateCommit()
    {
        DateTimeOffset capturedAtUtc = new(2026, 4, 21, 18, 0, 0, TimeSpan.Zero);
        ComputerUseWinStateStore stateStore = new(new FixedTimeProvider(capturedAtUtc), TimeSpan.FromSeconds(30), maxEntries: 1);
        string existingToken = stateStore.Create(CreateStoredState(capturedAtUtc));

        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            instructionProvider: new ThrowingInstructionProvider(
                new ComputerUseWinInstructionUnavailableException(
                    "Computer Use for Windows не смог прочитать advisory instructions для этого приложения.",
                    new IOException("instructions unavailable"))));

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.PreparedState);
        Assert.Empty(outcome.PreparedState!.Instructions);
        Assert.Contains(outcome.PreparedState.Warnings, warning => warning.Contains("advisory instructions", StringComparison.OrdinalIgnoreCase));
        Assert.True(stateStore.TryGet(existingToken, out _));
    }

    [Fact]
    public async Task AppStateObserverTreatsUnexpectedInstructionProviderBugAsStructuredFailure()
    {
        ComputerUseWinAppStateObservationOutcome outcome = await ObserveAsync(
            instructionProvider: new ThrowingInstructionProvider(new InvalidOperationException("secret provider bug")));

        AssertObservationFailed(outcome);
        Assert.DoesNotContain("secret", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        AssertAuditException<InvalidOperationException>(outcome);
    }

    private static Task<ComputerUseWinAppStateObservationOutcome> ObserveAsync(
        ICaptureService? captureService = null,
        FakeUiAutomationService? uiAutomationService = null,
        IComputerUseWinInstructionProvider? instructionProvider = null,
        int maxNodes = DefaultMaxNodes,
        string[]? warnings = null) =>
        CreateObserver(
                captureService ?? DefaultCaptureService,
                uiAutomationService ?? SnapshotService(root: RootWindowElement()),
                instructionProvider)
            .ObserveAsync(
                CreateWindow(),
                appId: AppId,
                windowId: WindowId,
                maxNodes: maxNodes,
                warnings: warnings ?? [],
                CancellationToken.None);

    private static ComputerUseWinAppStateObserver CreateObserver(
        ICaptureService captureService,
        FakeUiAutomationService uiAutomationService,
        IComputerUseWinInstructionProvider? instructionProvider = null) =>
        new(captureService, uiAutomationService, instructionProvider ?? CreatePlaybookProvider());

    private static ComputerUseWinPlaybookProvider CreatePlaybookProvider()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", nameof(ComputerUseWinObservationTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return new ComputerUseWinPlaybookProvider(
            new ComputerUseWinOptions(
                PluginRoot: root,
                AppInstructionsRoot: Path.Combine(root, "references", "AppInstructions"),
                ApprovalStorePath: Path.Combine(root, "AppApprovals.json")));
    }

    private static FakeUiAutomationService SnapshotService(
        string? status = null,
        UiaElementSnapshot? root = null,
        string? reason = null) =>
        new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: status ?? UiaSnapshotStatusValues.Done,
                Reason: reason,
                Window: CreateObservedWindow(window),
                Root: root,
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));

    private static UiaElementSnapshot RootWindowElement() =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
        };

    private static UiaElementSnapshot RootWindowElement(params UiaElementSnapshot[] children) =>
        new()
        {
            ElementId = "root",
            ControlType = "window",
            Children = [.. children],
        };

    private static void AssertObservationFailed(ComputerUseWinAppStateObservationOutcome outcome)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, outcome.FailureCode);
    }

    private static void AssertAuditException<TException>(ComputerUseWinAppStateObservationOutcome outcome)
        where TException : Exception
    {
        Assert.NotNull(outcome.FailureDetails?.AuditException);
        Assert.IsType<TException>(outcome.FailureDetails!.AuditException);
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
            new ComputerUseWinAppSession(AppId, WindowId, 101, "Explorer", AppId, 1001),
            CreateWindow(),
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, DefaultMaxNodes),
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

    private sealed class ThrowingInstructionProvider(Exception exception) : IComputerUseWinInstructionProvider
    {
        public IReadOnlyList<string> GetInstructions(string? processName) => throw exception;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
