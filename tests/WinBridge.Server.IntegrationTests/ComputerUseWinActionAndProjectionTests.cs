using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinActionAndProjectionTests
{
    [Fact]
    public void AccessibilityProjectorCarriesKeyboardFocusIntoStoredElements()
    {
        UiaElementSnapshot root = new()
        {
            ElementId = "root",
            ControlType = "window",
            Children =
            [
                new UiaElementSnapshot
                {
                    ElementId = "child",
                    ControlType = "edit",
                    Name = "Focused input",
                    BoundingRectangle = new Bounds(10, 20, 110, 40),
                    IsEnabled = true,
                    IsOffscreen = false,
                    HasKeyboardFocus = true,
                },
            ],
        };

        IReadOnlyDictionary<int, ComputerUseWinStoredElement> elements = ComputerUseWinAccessibilityProjector.Flatten(root);

        Assert.True(elements[2].HasKeyboardFocus);
        Assert.Contains(ToolNames.ComputerUseWinClick, elements[2].Actions);
    }

    [Fact]
    public async Task ClickTargetResolverRequiresConfirmationForCoordinateTargets()
    {
        ComputerUseWinClickTargetResolver resolver = new(new FakeUiAutomationService());

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30)),
            CancellationToken.None);

        Assert.True(resolution.IsSuccess);
        Assert.True(resolution.RequiresConfirmation);
        Assert.NotNull(resolution.Action);
        Assert.Equal(InputCoordinateSpaceValues.CapturePixels, resolution.Action!.CoordinateSpace);
    }

    [Fact]
    public async Task ClickTargetResolverReresolvesElementAgainstFreshSnapshotBeforeDispatch()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
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
                            ElementId = "path:0",
                            ControlType = "button",
                            Name = "Delete item",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = true,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.True(resolution.IsSuccess);
        Assert.Equal(768, uiAutomationService.LastRequest?.MaxNodes);
        Assert.NotNull(resolution.Action);
        Assert.Equal(InputCoordinateSpaceValues.Screen, resolution.Action!.CoordinateSpace);
        Assert.Equal(new InputPoint(140, 140), resolution.Action.Point);
        Assert.NotNull(resolution.EffectiveElement);
        Assert.True(resolution.RequiresConfirmation);
    }

    [Fact]
    public async Task ClickTargetResolverReturnsStaleStateWhenFreshSnapshotCannotFindStoredElement()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
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
                            ElementId = "other",
                            ControlType = "button",
                            Name = "Different",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = true,
                            IsOffscreen = false,
                        },
                    ],
                },
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
    }

    [Fact]
    public async Task ClickTargetResolverReturnsObservationFailedWhenFreshSnapshotDoesNotComplete()
    {
        FakeUiAutomationService uiAutomationService = new((window, request, _) => Task.FromResult(
            new UiaSnapshotResult(
                Status: UiaSnapshotStatusValues.Failed,
                Reason: "UIA worker did not complete.",
                Window: CreateObservedWindow(window),
                RequestedDepth: request.Depth,
                RequestedMaxNodes: request.MaxNodes,
                CapturedAtUtc: DateTimeOffset.UtcNow)));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, resolution.FailureCode);
        Assert.Contains("UIA worker", resolution.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClickTargetResolverMaterializesUnexpectedSnapshotExceptionsAsStructuredFailure()
    {
        FakeUiAutomationService uiAutomationService = new((_, _, _) => throw new InvalidOperationException("secret uia failure"));
        ComputerUseWinClickTargetResolver resolver = new(uiAutomationService);

        ComputerUseWinClickTargetResolution resolution = await resolver.ResolveAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.ObservationFailed, resolution.FailureCode);
        Assert.DoesNotContain("secret", resolution.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(resolution.FailureDetails);
        Assert.IsType<InvalidOperationException>(resolution.FailureDetails!.AuditException);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCoordinateClickWithoutActivationWhenConfirmMissing()
    {
        FakeWindowActivationService activationService = new(static window => new ActivateWindowResult("done", null, window, false, true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: false),
            CancellationToken.None);

        Assert.True(outcome.IsApprovalRequired);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsMalformedRequestWithoutActivation()
    {
        FakeWindowActivationService activationService = new(static window => new ActivateWindowResult("done", null, window, false, true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, outcome.FailureDetails?.FailureCode);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsRiskyElementWithoutActivationWhenConfirmMissing()
    {
        FakeWindowActivationService activationService = new(static window => new ActivateWindowResult("done", null, window, false, true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: false),
            CancellationToken.None);

        Assert.True(outcome.IsApprovalRequired);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCapturePixelsWithoutStoredCaptureReferenceBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => new ActivateWindowResult("done", null, window, false, true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredStateWithoutCaptureReference(),
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinFailureCodeValues.CaptureReferenceRequired, outcome.FailureDetails?.FailureCode);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorReresolvesElementTargetAfterActivationRetry()
    {
        int snapshotCall = 0;
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
        {
            snapshotCall++;
            Bounds bounds = snapshotCall == 1
                ? new Bounds(100, 100, 140, 140)
                : new Bounds(200, 200, 240, 240);
            return Task.FromResult(
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
                                ElementId = "path:0",
                                ControlType = "button",
                                Name = "Delete item",
                                AutomationId = "DeleteButton",
                                BoundingRectangle = bounds,
                                IsEnabled = true,
                                IsOffscreen = false,
                            },
                        ],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow));
        });
        int activationCalls = 0;
        FakeWindowActivationService activationService = new(window =>
        {
            activationCalls++;
            return new ActivateWindowResult("done", null, window with { Bounds = new Bounds(50, 50, 500, 500) }, false, true);
        });
        int inputCalls = 0;
        FakeInputService inputService = new((request, _, _) =>
        {
            inputCalls++;
            return Task.FromResult(
                inputCalls == 1
                    ? new InputResult(
                        Status: InputStatusValues.Failed,
                        Decision: InputStatusValues.Failed,
                        FailureCode: InputFailureCodeValues.TargetNotForeground,
                        Reason: "target not foreground",
                        TargetHwnd: 101)
                    : new InputResult(
                        Status: InputStatusValues.Done,
                        Decision: InputStatusValues.Done,
                        TargetHwnd: 101));
        });
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, inputService.Calls);
        Assert.Equal(2, activationCalls);
        Assert.Equal(2, snapshotCall);
        Assert.Equal(new InputPoint(220, 220), inputService.LastRequest!.Actions[0].Point);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorReappliesConfirmationAfterRetryReresolution()
    {
        int snapshotCall = 0;
        FakeUiAutomationService uiAutomationService = new((window, request, _) =>
        {
            snapshotCall++;
            UiaElementSnapshot element = snapshotCall == 1
                ? new UiaElementSnapshot
                {
                    ElementId = "path:0",
                    ControlType = "button",
                    Name = "Continue",
                    AutomationId = "ContinueButton",
                    BoundingRectangle = new Bounds(100, 100, 140, 140),
                    IsEnabled = true,
                    IsOffscreen = false,
                }
                : new UiaElementSnapshot
                {
                    ElementId = "path:0",
                    ControlType = "button",
                    Name = "Delete item",
                    AutomationId = "DeleteButton",
                    BoundingRectangle = new Bounds(200, 200, 240, 240),
                    IsEnabled = true,
                    IsOffscreen = false,
                };

            return Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Done,
                    Window: CreateObservedWindow(window),
                    Root: new UiaElementSnapshot
                    {
                        ElementId = "root",
                        ControlType = "window",
                        Children = [element],
                    },
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    CapturedAtUtc: DateTimeOffset.UtcNow));
        });
        int activationCalls = 0;
        FakeWindowActivationService activationService = new(window =>
        {
            activationCalls++;
            return new ActivateWindowResult("done", null, window, false, true);
        });
        FakeInputService inputService = new((request, _, _) =>
            Task.FromResult(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: InputFailureCodeValues.TargetNotForeground,
                    Reason: "target not foreground",
                    TargetHwnd: 101)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateSafeStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: false),
            CancellationToken.None);

        Assert.True(outcome.IsApprovalRequired);
        Assert.Equal(1, inputService.Calls);
        Assert.Equal(2, activationCalls);
        Assert.Equal(2, snapshotCall);
    }

    private static ComputerUseWinStoredState CreateStoredState() =>
        new(
            new ComputerUseWinAppSession("explorer", 101, "Explorer", "explorer", 1001),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Delete item",
                    AutomationId: "DeleteButton",
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateSafeStoredState() =>
        new(
            new ComputerUseWinAppSession("explorer", 101, "Explorer", "explorer", 1001),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Continue",
                    AutomationId: "ContinueButton",
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateStoredStateWithoutCaptureReference() =>
        new(
            new ComputerUseWinAppSession("explorer", 101, "Explorer", "explorer", 1001),
            CreateWindow(),
            CaptureReference: null,
            Elements: new Dictionary<int, ComputerUseWinStoredElement>(),
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

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

    private static InputCaptureReference CreateCaptureReference() =>
        new(
            bounds: new InputBounds(0, 0, 640, 480),
            pixelWidth: 640,
            pixelHeight: 480,
            effectiveDpi: 96,
            capturedAtUtc: DateTimeOffset.UtcNow,
            frameBounds: new InputBounds(0, 0, 640, 480),
            targetIdentity: new InputTargetIdentity(101, 1001, 2002, "TestWindow"));
}
