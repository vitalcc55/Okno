// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowUiaSnapshotToolTests
{
    private const long AttachedHwnd = 101;
    private const long ExplicitHwnd = 202;
    private const long ActiveHwnd = 303;
    private const string TestRunId = "window-uia-snapshot-tests";

    [Fact]
    public async Task UiaSnapshotPrefersExplicitTargetOverAttachedAndActive()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        WindowDescriptor explicitWindow = CreateExplicitWindow();
        WindowDescriptor activeWindow = CreateActiveWindow();
        FakeUiAutomationService uiaService = CreateSuccessfulUiAutomationService();
        WindowTools tools = CreateTools([attachedWindow, explicitWindow, activeWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot(hwnd: explicitWindow.Hwnd, depth: 1, maxNodes: 12);

        JsonElement payload = AssertSnapshotSucceeded(result);
        Assert.Equal(explicitWindow.Hwnd, uiaService.LastWindow?.Hwnd);
        Assert.Equal(1, uiaService.LastRequest?.Depth);
        Assert.Equal(12, uiaService.LastRequest?.MaxNodes);
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(UiaSnapshotTargetSourceValues.Explicit, payload.GetProperty("targetSource").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("requestedHwnd").GetInt64());
        Assert.Equal(12, payload.GetProperty("requestedMaxNodes").GetInt32());
    }

    [Fact]
    public async Task UiaSnapshotUsesAttachedWindowWhenExplicitTargetIsMissing()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeUiAutomationService uiaService = CreateSuccessfulUiAutomationService();
        WindowTools tools = CreateTools([attachedWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot(depth: 2, maxNodes: 33);

        JsonElement payload = AssertSnapshotSucceeded(result);
        Assert.Equal(attachedWindow.Hwnd, uiaService.LastWindow?.Hwnd);
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
        Assert.Equal(2, payload.GetProperty("requestedDepth").GetInt32());
        Assert.Equal(33, payload.GetProperty("requestedMaxNodes").GetInt32());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.NotNull(payload.GetProperty("capturedAtUtc").GetString());
    }

    [Fact]
    public async Task UiaSnapshotPublishesObservedWindowMetadataReturnedByRuntime()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        ObservedWindowDescriptor observedWindow = CreateObservedWindow(attachedWindow) with
        {
            Title = "Observed",
            ProcessId = 999,
            ThreadId = 998,
            ClassName = "ObservedWindow",
            MonitorId = null,
            EffectiveDpi = null,
        };
        FakeUiAutomationService uiaService = new((_, request, _) => Task.FromResult(CreateSuccessfulRuntimeResult(observedWindow, request)));
        WindowTools tools = CreateTools([attachedWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotSucceeded(result);
        JsonElement windowPayload = payload.GetProperty("window");
        Assert.Equal(attachedWindow.Hwnd, uiaService.LastWindow?.Hwnd);
        Assert.Equal("Observed", windowPayload.GetProperty("title").GetString());
        Assert.Equal(999, windowPayload.GetProperty("processId").GetInt32());
        Assert.Equal(998, windowPayload.GetProperty("threadId").GetInt32());
        Assert.Equal("ObservedWindow", windowPayload.GetProperty("className").GetString());
        AssertNoProperty(windowPayload, "monitorId");
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
    }

    [Fact]
    public async Task UiaSnapshotUsesActiveWindowWhenNoExplicitOrAttachedTargetExists()
    {
        WindowDescriptor activeWindow = CreateActiveWindow();
        FakeUiAutomationService uiaService = CreateSuccessfulUiAutomationService();
        WindowTools tools = CreateTools([activeWindow], attachedWindow: null, uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotSucceeded(result);
        Assert.Equal(activeWindow.Hwnd, uiaService.LastWindow?.Hwnd);
        Assert.Equal(UiaSnapshotTargetSourceValues.Active, payload.GetProperty("targetSource").GetString());
        Assert.Equal(activeWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForStaleExplicitTargetWithoutCallingService()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools([attachedWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot(hwnd: 999);

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(0, uiaService.Calls);
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleExplicitTarget, payload.GetProperty("targetFailureCode").GetString());
        AssertNoProperty(payload, "capturedAtUtc");
        AssertNoProperty(payload, "root");
    }

    [Fact]
    public async Task UiaSnapshotRejectsInvalidDepthBeforeTargetResolution()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools([attachedWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot(hwnd: 999, depth: -1);

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(0, uiaService.Calls);
        AssertNoNonNullProperty(payload, "targetFailureCode");
        Assert.Contains("depth", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForStaleAttachedTargetWithoutCallingService()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        WindowDescriptor reusedWindow = CreateWindow(hwnd: AttachedHwnd, title: "Different", threadId: 999);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools([reusedWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(0, uiaService.Calls);
        Assert.Equal(UiaSnapshotTargetFailureValues.StaleAttachedTarget, payload.GetProperty("targetFailureCode").GetString());
        Assert.Contains("Прикрепленное окно", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiaSnapshotRejectsTooLargeMaxNodesBeforeTargetResolution()
    {
        WindowDescriptor staleAttachedWindow = CreateAttachedWindow();
        WindowDescriptor reusedWindow = CreateWindow(hwnd: AttachedHwnd, title: "Different", threadId: 999);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools([reusedWindow], staleAttachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot(maxNodes: UiaSnapshotRequestValidator.MaxNodesCeiling + 1);

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(0, uiaService.Calls);
        Assert.Contains(UiaSnapshotRequestValidator.MaxNodesCeiling.ToString(CultureInfo.InvariantCulture), payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
        AssertNoNonNullProperty(payload, "targetFailureCode");
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForMissingTargetWithoutCallingService()
    {
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools([], attachedWindow: null, uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(0, uiaService.Calls);
        Assert.Equal(UiaSnapshotTargetFailureValues.MissingTarget, payload.GetProperty("targetFailureCode").GetString());
    }

    [Fact]
    public async Task UiaSnapshotReturnsTypedFailureForAmbiguousActiveTargetWithoutCallingService()
    {
        WindowDescriptor firstCandidate = CreateWindow(hwnd: ActiveHwnd, title: "Active 1", isForeground: true);
        WindowDescriptor secondCandidate = CreateWindow(hwnd: 404, title: "Active 2", isForeground: true, threadId: 777);
        FakeUiAutomationService uiaService = new();
        WindowTools tools = CreateTools([firstCandidate, secondCandidate], attachedWindow: null, uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(0, uiaService.Calls);
        Assert.Equal(UiaSnapshotTargetFailureValues.AmbiguousActiveTarget, payload.GetProperty("targetFailureCode").GetString());
    }

    [Fact]
    public async Task UiaSnapshotReturnsRuntimeFailureAsToolErrorWithoutExceptionLeak()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
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
        WindowTools tools = CreateTools([attachedWindow], attachedWindow, uiaService);

        CallToolResult result = await tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal(UiaSnapshotTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
        Assert.Equal(attachedWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.Contains("root element", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiaSnapshotReturnsFailedToolResultWhenServiceThrowsUnexpectedException()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeUiAutomationService uiaService = new((_, _, _) => throw new InvalidOperationException("secret internal failure"));
        TestContext context = CreateContext([attachedWindow], attachedWindow, uiaService);

        CallToolResult result = await context.Tools.UiaSnapshot();

        JsonElement payload = AssertSnapshotFailed(result);
        Assert.Equal("Server не смог завершить UIA snapshot request.", payload.GetProperty("reason").GetString());
        Assert.DoesNotContain("secret", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        AssertNoProperty(payload, "window");

        string completedEvent = Assert.Single(
            File.ReadLines(context.EventsPath),
            line => line.Contains("\"event_name\":\"tool.invocation.completed\"", StringComparison.Ordinal));
        Assert.Contains("\"exception_type\":\"System.InvalidOperationException\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_applied\":\"true\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redaction_class\":\"target_metadata\"", completedEvent, StringComparison.Ordinal);
        Assert.Contains("\"redacted_fields\":\"exception_message\"", completedEvent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret internal failure", completedEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveHandlerDefaultsMatchCanonicalSnapshotDefaults()
    {
        MethodInfo method = typeof(WindowTools).GetMethod(nameof(WindowTools.UiaSnapshot))!;

        AssertDefaultParameter(method, "depth", UiaSnapshotDefaults.Depth);
        AssertDefaultParameter(method, "maxNodes", UiaSnapshotDefaults.MaxNodes);
    }

    private static JsonElement AssertSnapshotSucceeded(CallToolResult result)
    {
        Assert.False(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Done, payload.GetProperty("status").GetString());
        return payload;
    }

    private static JsonElement AssertSnapshotFailed(CallToolResult result)
    {
        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(UiaSnapshotStatusValues.Failed, payload.GetProperty("status").GetString());
        return payload;
    }

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

    private static void AssertNoProperty(JsonElement payload, string propertyName) =>
        Assert.False(payload.TryGetProperty(propertyName, out _));

    private static void AssertNoNonNullProperty(JsonElement payload, string propertyName) =>
        Assert.False(payload.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind != JsonValueKind.Null);

    private static void AssertDefaultParameter(MethodInfo method, string parameterName, int expectedDefault)
    {
        ParameterInfo parameter = method.GetParameters().Single(candidate => string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));
        Assert.Equal(expectedDefault, Assert.IsType<int>(parameter.DefaultValue));
    }

    private static FakeUiAutomationService CreateSuccessfulUiAutomationService() =>
        new((targetWindow, request, _) => Task.FromResult(CreateSuccessfulRuntimeResult(targetWindow, request)));

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
        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, TestRunId);
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: TestRunId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
        TimeProvider timeProvider = TimeProvider.System;
        AuditLog auditLog = new(options, timeProvider);
        InMemorySessionManager sessionManager = new(timeProvider, new SessionContext(TestRunId));

        if (attachedWindow is not null)
        {
            sessionManager.Attach(attachedWindow, "hwnd");
        }

        FakeWindowManager windowManager = new(windows);
        WaitResultMaterializer waitResultMaterializer = new(auditLog, options, WaitOptions.Default);
        return new TestContext(
            new WindowTools(
                auditLog,
                sessionManager,
                windowManager,
                new NoopCaptureService(),
                new FakeMonitorManager(),
                new FakeWindowActivationService(),
                new WindowTargetResolver(windowManager),
                uiAutomationService,
                new FakeWaitService(),
                waitResultMaterializer,
                new FakeToolExecutionGate(),
                new FakeInputService(),
                new FakeProcessLaunchService(),
                new FakeOpenTargetService()),
            options.EventsPath);
    }

    private static WindowDescriptor CreateAttachedWindow() => CreateWindow(AttachedHwnd, "Attached");

    private static WindowDescriptor CreateExplicitWindow() => CreateWindow(ExplicitHwnd, "Explicit");

    private static WindowDescriptor CreateActiveWindow() => CreateWindow(ActiveHwnd, "Active", isForeground: true);

    private static WindowDescriptor CreateWindow(
        long hwnd,
        string title,
        bool isForeground = false,
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
            Root: CreateRootSnapshot(targetWindow));

    private static UiaElementSnapshot CreateRootSnapshot(ObservedWindowDescriptor window) =>
        new()
        {
            ElementId = "rid:1.2",
            Depth = 0,
            Ordinal = 0,
            Name = window.Title,
            AutomationId = "SmokeRoot",
            ClassName = window.ClassName,
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
            BoundingRectangle = window.Bounds,
            NativeWindowHandle = window.Hwnd,
            Children = [CreateButtonSnapshot(window.Hwnd)],
        };

    private static UiaElementSnapshot CreateButtonSnapshot(long hwnd) =>
        new()
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
            NativeWindowHandle = hwnd,
        };

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
