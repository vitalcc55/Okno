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
