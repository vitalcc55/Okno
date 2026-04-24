using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinActionAndProjectionTests
{
    [Fact]
    public void ListAppsGroupsVisibleWindowsByStableAppIdAndPrefersForegroundRepresentative()
    {
        using TempDirectoryScope temp = new();
        ComputerUseWinApprovalStore approvalStore = new(
            new ComputerUseWinOptions(
                PluginRoot: temp.Root,
                AppInstructionsRoot: Path.Combine(temp.Root, "references", "AppInstructions"),
                ApprovalStorePath: Path.Combine(temp.Root, "AppApprovals.json")));
        approvalStore.Approve("explorer");

        ComputerUseWinTools tools = CreateComputerUseWinTools(
            new FakeListAppsWindowManager(
            [
                CreateWindow(hwnd: 101, title: "Explorer A", processName: "explorer", processId: 1001, isForeground: false),
                CreateWindow(hwnd: 202, title: "Explorer B", processName: "explorer", processId: 1001, isForeground: true),
                CreateWindow(hwnd: 303, title: "Admin Console", processName: "powershell", processId: 2002, isForeground: false),
                CreateWindow(hwnd: 404, title: "Hidden Helper", processName: "notepad", processId: 3003, isForeground: false, isVisible: false),
            ]),
            approvalStore);

        ModelContextProtocol.Protocol.CallToolResult result = tools.ListApps();

        Assert.False(result.IsError);
        ComputerUseWinListAppsResult payload = JsonSerializer.Deserialize<ComputerUseWinListAppsResult>(
            result.StructuredContent!.Value.GetRawText(),
            ComputerUseWinToolResultFactory.PayloadJsonOptions)!;
        Assert.Equal(ComputerUseWinStatusValues.Ok, payload.Status);
        Assert.Equal(2, payload.Count);
        Assert.Equal(2, payload.Apps.Count);

        ComputerUseWinAppDescriptor foregroundExplorer = payload.Apps[0];
        Assert.Equal("explorer", foregroundExplorer.AppId);
        Assert.Equal(202, foregroundExplorer.Hwnd);
        Assert.Equal("Explorer B", foregroundExplorer.Title);
        Assert.True(foregroundExplorer.IsForeground);
        Assert.True(foregroundExplorer.IsApproved);
        Assert.False(foregroundExplorer.IsBlocked);

        ComputerUseWinAppDescriptor blockedConsole = payload.Apps[1];
        Assert.Equal("powershell", blockedConsole.AppId);
        Assert.Equal(303, blockedConsole.Hwnd);
        Assert.False(blockedConsole.IsApproved);
        Assert.True(blockedConsole.IsBlocked);
        Assert.Contains("powershell", blockedConsole.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

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
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
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
        Assert.Equal(ComputerUseWinActionLifecyclePhase.BeforeActivation, outcome.Phase);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsMalformedRequestWithoutActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
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
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
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
    public async Task ClickExecutionCoordinatorRejectsStoredElementWithoutClickAffordanceBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
        FakeInputService inputService = new((_, _, _) => Task.FromResult(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done)));
        ComputerUseWinClickExecutionCoordinator coordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(new FakeUiAutomationService()),
            inputService);

        ComputerUseWinClickExecutionOutcome outcome = await coordinator.ExecuteAsync(
            CreateNonActionableStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1, Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.InvalidRequest, outcome.FailureDetails?.FailureCode);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCapturePixelsWithoutStoredCaptureReferenceBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
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
        Assert.Equal(ComputerUseWinActionLifecyclePhase.BeforeActivation, outcome.Phase);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorRejectsCapturePixelsPointOutsideStoredRasterBeforeActivation()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true));
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
            new ComputerUseWinClickRequest(Point: new InputPoint(9999, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsApprovalRequired);
        Assert.Equal(ComputerUseWinFailureCodeValues.PointOutOfBounds, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.BeforeActivation, outcome.Phase);
        Assert.Null(activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsMissingActivationTargetWithoutPolicyBlock()
    {
        FakeWindowActivationService activationService = new(static _ => ActivateWindowResult.Failed(
            "Окно для активации больше не найдено.",
            wasMinimized: false,
            failureKind: ActivationFailureKindValues.MissingTarget));
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
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.MissingTarget, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsMinimizedActivationFailureWithoutPolicyBlock()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Ambiguous(
            "Окно снова оказалось свернутым до завершения активации.",
            window with { WindowState = WindowStateValues.Minimized },
            wasMinimized: true,
            isForeground: false,
            failureKind: ActivationFailureKindValues.RestoreFailedStillMinimized));
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
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.TargetMinimized, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorDoesNotTreatMinimizedIdentityLossAsMinimizedFailure()
    {
        FakeWindowActivationService activationService = new(static _ => ActivateWindowResult.Failed(
            "Окно для активации больше не найдено или больше не совпадает с исходной identity.",
            wasMinimized: true,
            failureKind: ActivationFailureKindValues.MissingTarget));
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
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.MissingTarget, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.TargetMinimized, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsTypedActivationIdentityChangedAsStaleState()
    {
        FakeWindowActivationService activationService = new(static _ => ActivateWindowResult.Failed(
            "Окно для активации больше не совпадает с исходной identity в финальном activation snapshot.",
            wasMinimized: true,
            failureKind: ActivationFailureKindValues.IdentityChanged));
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
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.TargetMinimized, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickExecutionCoordinatorMapsForegroundActivationFailureWithoutPolicyBlock()
    {
        FakeWindowActivationService activationService = new(static window => ActivateWindowResult.Failed(
            "Windows отказалась перевести окно в foreground.",
            window,
            wasMinimized: false,
            isForeground: false,
            failureKind: ActivationFailureKindValues.ForegroundNotConfirmed));
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
            new ComputerUseWinClickRequest(Point: new InputPoint(20, 30), Confirm: true),
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.TargetNotForeground, outcome.FailureDetails?.FailureCode);
        Assert.NotEqual(ComputerUseWinFailureCodeValues.BlockedTarget, outcome.FailureDetails?.FailureCode);
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch, outcome.Phase);
        Assert.Equal(101, activationService.LastHwnd);
        Assert.Equal(0, inputService.Calls);
    }

    [Fact]
    public async Task ClickTargetResolverReturnsStaleStateWhenFreshElementLosesClickAffordance()
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
                            AutomationId = "DeleteButton",
                            BoundingRectangle = new Bounds(100, 120, 180, 160),
                            IsEnabled = false,
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
    public async Task ClickTargetResolverDoesNotFallbackOnControlTypeOnlyMatch()
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
            CreateUnnamedStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
    }

    [Fact]
    public async Task ClickTargetResolverDoesNotFallbackOnLabelOnlyMatch()
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
            CreateLabelOnlyStoredState(),
            new ComputerUseWinClickRequest(ElementIndex: 1),
            CancellationToken.None);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(ComputerUseWinFailureCodeValues.StaleState, resolution.FailureCode);
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
            return ActivateWindowResult.Done(
                window with { Bounds = new Bounds(50, 50, 500, 500) },
                wasMinimized: false,
                isForeground: true);
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
            return ActivateWindowResult.Done(window, wasMinimized: false, isForeground: true);
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
        Assert.Equal(ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch, outcome.Phase);
        Assert.Equal(1, inputService.Calls);
        Assert.Equal(2, activationCalls);
        Assert.Equal(2, snapshotCall);
    }

    private static ComputerUseWinTools CreateComputerUseWinTools(
        IWindowManager windowManager,
        ComputerUseWinApprovalStore approvalStore)
    {
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("computer-use-win-stage-2-test"));
        ComputerUseWinStateStore stateStore = new();
        FakeUiAutomationService uiAutomationService = new();
        FakeWindowActivationService activationService = new();
        FakeInputService inputService = new();
        ComputerUseWinAppStateObserver appStateObserver = new(
            new NoopCaptureService(),
            uiAutomationService,
            new EmptyInstructionProvider());
        ComputerUseWinClickExecutionCoordinator clickExecutionCoordinator = new(
            activationService,
            new ComputerUseWinClickTargetResolver(uiAutomationService),
            inputService);

        return new(
            CreateAuditLog(),
            sessionManager,
            new ComputerUseWinListAppsHandler(new ComputerUseWinAppDiscoveryService(windowManager, approvalStore)),
            new ComputerUseWinGetAppStateHandler(
                windowManager,
                sessionManager,
                approvalStore,
                stateStore,
                activationService,
                appStateObserver),
            new ComputerUseWinClickHandler(
                new ComputerUseWinStoredStateResolver(stateStore, windowManager),
                clickExecutionCoordinator));
    }

    private static AuditLog CreateAuditLog()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "tests",
            RunId: "computer-use-win-stage-2-test",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-stage-2-test"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-stage-2-test", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "computer-use-win-stage-2-test", "summary.md"));
        return new AuditLog(options, TimeProvider.System);
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

    private static ComputerUseWinStoredState CreateNonActionableStoredState() =>
        new(
            new ComputerUseWinAppSession("explorer", 101, "Explorer", "explorer", 1001),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: "Status only",
                    AutomationId: "StatusLabel",
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: []),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateUnnamedStoredState() =>
        new(
            new ComputerUseWinAppSession("explorer", 101, "Explorer", "explorer", 1001),
            CreateWindow(),
            CaptureReference: CreateCaptureReference(),
            Elements: new Dictionary<int, ComputerUseWinStoredElement>
            {
                [1] = new(
                    Index: 1,
                    ElementId: "path:0",
                    Name: null,
                    AutomationId: null,
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static ComputerUseWinStoredState CreateLabelOnlyStoredState() =>
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
                    AutomationId: null,
                    ControlType: "button",
                    Bounds: new Bounds(10, 20, 50, 60),
                    HasKeyboardFocus: false,
                    Actions: [ToolNames.ComputerUseWinClick]),
            },
            Observation: new ComputerUseWinObservationEnvelope(UiaSnapshotDefaults.Depth, 768),
            CapturedAtUtc: DateTimeOffset.UtcNow);

    private static WindowDescriptor CreateWindow(
        long hwnd = 101,
        string title = "Test window",
        string processName = "explorer",
        int processId = 1001,
        bool isForeground = true,
        bool isVisible = true) =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: processName,
            ProcessId: processId,
            ThreadId: 2002,
            ClassName: "TestWindow",
            Bounds: new Bounds(0, 0, 640, 480),
            IsForeground: isForeground,
            IsVisible: isVisible);

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

    private sealed class FakeListAppsWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) =>
            includeInvisible ? windows : windows.Where(static window => window.IsVisible).ToArray();

        public WindowDescriptor? FindWindow(WindowSelector selector) =>
            throw new NotSupportedException("FindWindow не должен вызываться в list_apps characterization test.");

        public bool TryFocus(long hwnd) =>
            throw new NotSupportedException("TryFocus не должен вызываться в list_apps characterization test.");
    }

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в list_apps characterization test.");
    }

    private sealed class EmptyInstructionProvider : IComputerUseWinInstructionProvider
    {
        public IReadOnlyList<string> GetInstructions(string? processName) => [];
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
