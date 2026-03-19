using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowUiaSnapshotToolTests
{
    [Fact]
    public async Task UiaSnapshotPrefersExplicitTargetOverAttachedAndActive()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 202, title: "Explicit", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        FakeUiAutomationService uiaService = new((targetWindow, request, _) => Task.FromResult(CreateSuccessfulRuntimeResult(targetWindow, request)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow, explicitWindow, activeWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot(hwnd: explicitWindow.Hwnd, depth: 1, maxNodes: 12);

        Assert.False(result.IsError);
        Assert.Equal(explicitWindow.Hwnd, uiaService.LastWindow?.Hwnd);
        Assert.Equal(1, uiaService.LastRequest?.Depth);
        Assert.Equal(12, uiaService.LastRequest?.MaxNodes);
        Assert.Single(result.Content);
        Assert.IsType<TextContentBlock>(result.Content[0]);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(UiaSnapshotTargetSourceValues.Explicit, payload.GetProperty("targetSource").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("requestedHwnd").GetInt64());
        Assert.Equal(12, payload.GetProperty("requestedMaxNodes").GetInt32());
    }

    [Fact]
    public async Task UiaSnapshotUsesAttachedWindowWhenExplicitTargetIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeUiAutomationService uiaService = new((targetWindow, request, _) => Task.FromResult(CreateSuccessfulRuntimeResult(targetWindow, request)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot(depth: 2, maxNodes: 33);

        Assert.False(result.IsError);
        Assert.Equal(attachedWindow.Hwnd, uiaService.LastWindow?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
        Assert.Equal(2, payload.GetProperty("requestedDepth").GetInt32());
        Assert.Equal(33, payload.GetProperty("requestedMaxNodes").GetInt32());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.True(payload.GetProperty("capturedAtUtc").GetString() is not null);
    }

    [Fact]
    public async Task UiaSnapshotPublishesObservedWindowMetadataReturnedByRuntime()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        ObservedWindowDescriptor observedWindow = CreateObservedWindow(attachedWindow) with
        {
            Title = "Observed",
            ProcessId = 999,
            ThreadId = 998,
            ClassName = "ObservedWindow",
            MonitorId = null,
            EffectiveDpi = null,
        };
        FakeUiAutomationService uiaService = new((targetWindow, request, _) => Task.FromResult(CreateSuccessfulRuntimeResult(observedWindow, request)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        Assert.False(result.IsError);
        Assert.Equal(attachedWindow.Hwnd, uiaService.LastWindow?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("Observed", payload.GetProperty("window").GetProperty("title").GetString());
        Assert.Equal(999, payload.GetProperty("window").GetProperty("processId").GetInt32());
        Assert.Equal(998, payload.GetProperty("window").GetProperty("threadId").GetInt32());
        Assert.Equal("ObservedWindow", payload.GetProperty("window").GetProperty("className").GetString());
        Assert.False(payload.GetProperty("window").TryGetProperty("monitorId", out _));
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
    }

    [Fact]
    public async Task UiaSnapshotUsesActiveWindowWhenNoExplicitOrAttachedTargetExists()
    {
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        FakeUiAutomationService uiaService = new((targetWindow, request, _) => Task.FromResult(CreateSuccessfulRuntimeResult(targetWindow, request)));
        WindowTools tools = CreateTools(
            windows: [activeWindow],
            attachedWindow: null,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        Assert.False(result.IsError);
        Assert.Equal(activeWindow.Hwnd, uiaService.LastWindow?.Hwnd);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotTargetSourceValues.Active, payload.GetProperty("targetSource").GetString());
        Assert.Equal(activeWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForStaleExplicitTargetWithoutCallingService()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot(hwnd: 999);

        Assert.True(result.IsError);
        Assert.Equal(0, uiaService.Calls);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleExplicitTarget, payload.GetProperty("targetFailureCode").GetString());
        Assert.False(payload.TryGetProperty("capturedAtUtc", out _));
        Assert.False(payload.TryGetProperty("root", out _));
    }

    [Fact]
    public async Task UiaSnapshotRejectsInvalidDepthBeforeTargetResolution()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot(hwnd: 999, depth: -1);

        Assert.True(result.IsError);
        Assert.Equal(0, uiaService.Calls);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.False(payload.TryGetProperty("targetFailureCode", out JsonElement targetFailureCode) && targetFailureCode.ValueKind != JsonValueKind.Null);
        Assert.Contains("depth", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForStaleAttachedTargetWithoutCallingService()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        WindowDescriptor reusedWindow = CreateWindow(hwnd: 101, title: "Different", threadId: 999, isForeground: false);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools(
            windows: [reusedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        Assert.True(result.IsError);
        Assert.Equal(0, uiaService.Calls);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleAttachedTarget, payload.GetProperty("targetFailureCode").GetString());
        Assert.Contains("Прикрепленное окно", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiaSnapshotRejectsTooLargeMaxNodesBeforeTargetResolution()
    {
        WindowDescriptor staleAttachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        WindowDescriptor reusedWindow = CreateWindow(hwnd: 101, title: "Different", threadId: 999, isForeground: false);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools(
            windows: [reusedWindow],
            attachedWindow: staleAttachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot(maxNodes: UiaSnapshotRequestValidator.MaxNodesCeiling + 1);

        Assert.True(result.IsError);
        Assert.Equal(0, uiaService.Calls);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Contains(UiaSnapshotRequestValidator.MaxNodesCeiling.ToString(CultureInfo.InvariantCulture), payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.False(payload.TryGetProperty("targetFailureCode", out JsonElement targetFailureCode) && targetFailureCode.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForMissingTargetWithoutCallingService()
    {
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools(
            windows: [],
            attachedWindow: null,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        Assert.True(result.IsError);
        Assert.Equal(0, uiaService.Calls);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotTargetFailureValues.MissingTarget, payload.GetProperty("targetFailureCode").GetString());
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForAmbiguousActiveTargetWithoutCallingService()
    {
        WindowDescriptor firstCandidate = CreateWindow(hwnd: 303, title: "Active 1", isForeground: true);
        WindowDescriptor secondCandidate = CreateWindow(hwnd: 404, title: "Active 2", isForeground: true, threadId: 777);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools(
            windows: [firstCandidate, secondCandidate],
            attachedWindow: null,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        Assert.True(result.IsError);
        Assert.Equal(0, uiaService.Calls);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotTargetFailureValues.AmbiguousActiveTarget, payload.GetProperty("targetFailureCode").GetString());
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UiaSnapshotReturnsRuntimeFailureAsToolErrorWithoutExceptionLeak()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeUiAutomationService uiaService = new(
            (targetWindow, request, _) => Task.FromResult(
                new UiaSnapshotResult(
                    Status: UiaSnapshotStatusValues.Failed,
                    Reason: "UI Automation не смогла получить root element для выбранного hwnd.",
                    Window: CreateObservedWindow(targetWindow),
                    RequestedDepth: request.Depth,
                    RequestedMaxNodes: request.MaxNodes,
                    View: UiaSnapshotDefaults.View,
                    CapturedAtUtc: new DateTimeOffset(2026, 3, 19, 10, 30, 0, TimeSpan.Zero))));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.Contains("root element", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiaSnapshotReturnsFailedToolResultWhenServiceThrowsUnexpectedException()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeUiAutomationService uiaService = new((_, _, _) => throw new InvalidOperationException("secret internal failure"));
        TestContext context = CreateContext(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            uiAutomationService: uiaService);

        CallToolResult result = await context.Tools.UiaSnapshot();

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("Server не смог завершить UIA snapshot request.", payload.GetProperty("reason").GetString());
        Assert.DoesNotContain("secret", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(payload.TryGetProperty("window", out _));
        string[] eventLines = await File.ReadAllLinesAsync(context.EventsPath);
        string completedEvent = Assert.Single(
            eventLines,
            line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"exception_message\":\"secret internal failure\"", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveHandlerDefaultsMatchCanonicalSnapshotDefaults()
    {
        MethodInfo method = typeof(WindowTools).GetMethod(nameof(WindowTools.UiaSnapshot))!;
        ParameterInfo depthParameter = method
            .GetParameters()
            .Single(parameter => string.Equals(parameter.Name, "depth", StringComparison.Ordinal));
        ParameterInfo maxNodesParameter = method
            .GetParameters()
            .Single(parameter => string.Equals(parameter.Name, "maxNodes", StringComparison.Ordinal));

        Assert.Equal(UiaSnapshotDefaults.Depth, Assert.IsType<int>(depthParameter.DefaultValue));
        Assert.Equal(UiaSnapshotDefaults.MaxNodes, Assert.IsType<int>(maxNodesParameter.DefaultValue));
    }

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

    private static WindowTools CreateTools(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow,
        FakeUiAutomationService uiAutomationService) =>
        CreateContext(windows, attachedWindow, uiAutomationService).Tools;

    private static TestContext CreateContext(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow,
        FakeUiAutomationService uiAutomationService)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "window-uia-snapshot-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "window-uia-snapshot-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "window-uia-snapshot-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "window-uia-snapshot-tests", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("window-uia-snapshot-tests"));

        if (attachedWindow is not null)
        {
            sessionManager.Attach(attachedWindow, "hwnd");
        }

        FakeWindowManager windowManager = new(windows);
        return new TestContext(
            new WindowTools(
                auditLog,
                sessionManager,
                windowManager,
                new NoopCaptureService(),
                new FakeMonitorManager(),
                new FakeWindowActivationService(),
                new WindowTargetResolver(windowManager),
                uiAutomationService),
            options.EventsPath);
    }

    private static WindowDescriptor CreateWindow(
        long hwnd,
        string title,
        bool isForeground,
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: "okno-tests",
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: isForeground,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");

    private static UiaSnapshotResult CreateSuccessfulRuntimeResult(WindowDescriptor targetWindow, UiaSnapshotRequest request) =>
        CreateSuccessfulRuntimeResult(CreateObservedWindow(targetWindow), request);

    private static UiaSnapshotResult CreateSuccessfulRuntimeResult(ObservedWindowDescriptor targetWindow, UiaSnapshotRequest request) =>
        new(
            Status: UiaSnapshotStatusValues.Done,
            Reason: null,
            Window: targetWindow,
            View: UiaSnapshotDefaults.View,
            RequestedDepth: request.Depth,
            RequestedMaxNodes: request.MaxNodes,
            RealizedDepth: 1,
            NodeCount: 3,
            Truncated: false,
            DepthBoundaryReached: false,
            NodeBudgetBoundaryReached: false,
            AcquisitionMode: "element_from_handle",
            ArtifactPath: @"C:\artifacts\uia-snapshot.json",
            CapturedAtUtc: new DateTimeOffset(2026, 3, 19, 10, 0, 0, TimeSpan.Zero),
            Root: new UiaElementSnapshot
            {
                ElementId = "rid:1.2",
                Depth = 0,
                Ordinal = 0,
                Name = targetWindow.Title,
                AutomationId = "SmokeRoot",
                ClassName = targetWindow.ClassName,
                FrameworkId = "Win32",
                ControlType = "window",
                ControlTypeId = 50032,
                LocalizedControlType = "окно",
                IsControlElement = true,
                IsContentElement = true,
                IsEnabled = true,
                IsOffscreen = false,
                HasKeyboardFocus = true,
                Patterns = ["window"],
                BoundingRectangle = targetWindow.Bounds,
                NativeWindowHandle = targetWindow.Hwnd,
                Children =
                [
                    new UiaElementSnapshot
                    {
                        ElementId = "rid:1.2/button",
                        ParentElementId = "rid:1.2",
                        Depth = 1,
                        Ordinal = 0,
                        Name = "Run",
                        AutomationId = "RunButton",
                        ClassName = "Button",
                        FrameworkId = "Win32",
                        ControlType = "button",
                        ControlTypeId = 50000,
                        LocalizedControlType = "кнопка",
                        IsControlElement = true,
                        IsContentElement = true,
                        IsEnabled = true,
                        IsOffscreen = false,
                        HasKeyboardFocus = false,
                        Patterns = ["invoke"],
                        BoundingRectangle = new Bounds(20, 20, 80, 40),
                        NativeWindowHandle = targetWindow.Hwnd,
                    },
                ],
            });

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

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в UIA snapshot tests.");
    }

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => window.Hwnd == selector.Hwnd);
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }

    private sealed record TestContext(WindowTools Tools, string EventsPath);
}
