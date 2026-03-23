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

public sealed class WindowWaitToolTests
{
    [Fact]
    public async Task WaitUsesExplicitTargetAndPublishesRuntimePayload()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 202, title: "Explicit", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        FakeWaitService waitService = new((target, request, _) => Task.FromResult(CreateDoneResult(target, request)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow, explicitWindow, activeWindow],
            attachedWindow: attachedWindow,
            waitService: waitService);

        CallToolResult result = await tools.Wait(
            condition: WaitConditionValues.ElementExists,
            selector: new WaitElementSelector(AutomationId: "RunSemanticSmokeButton"),
            hwnd: explicitWindow.Hwnd,
            timeoutMs: 1500);

        Assert.False(result.IsError);
        Assert.Equal(explicitWindow.Hwnd, waitService.LastTarget?.Window?.Hwnd);
        Assert.Equal(WaitTargetSourceValues.Explicit, waitService.LastTarget?.Source);
        Assert.Equal("RunSemanticSmokeButton", waitService.LastRequest?.Selector?.AutomationId);
        Assert.Single(result.Content);
        TextContentBlock textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Contains("\"condition\":\"element_exists\"", textBlock.Text, StringComparison.Ordinal);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(WaitStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(WaitConditionValues.ElementExists, payload.GetProperty("condition").GetString());
        Assert.Equal(WaitTargetSourceValues.Explicit, payload.GetProperty("targetSource").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.Equal("RunSemanticSmokeButton", payload.GetProperty("matchedElement").GetProperty("automationId").GetString());
    }

    [Fact]
    public async Task WaitUsesAttachedTargetWhenExplicitTargetIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeWaitService waitService = new((target, request, _) => Task.FromResult(CreateDoneResult(target, request)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            waitService: waitService);

        CallToolResult result = await tools.Wait(
            condition: WaitConditionValues.ActiveWindowMatches,
            timeoutMs: 1200);

        Assert.False(result.IsError);
        Assert.Equal(WaitTargetSourceValues.Attached, waitService.LastTarget?.Source);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(WaitTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
    }

    [Fact]
    public async Task WaitUsesActiveTargetWhenNoExplicitOrAttachedTargetExists()
    {
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        FakeWaitService waitService = new((target, request, _) => Task.FromResult(CreateDoneResult(target, request)));
        WindowTools tools = CreateTools(
            windows: [activeWindow],
            attachedWindow: null,
            waitService: waitService);

        CallToolResult result = await tools.Wait(
            condition: WaitConditionValues.ActiveWindowMatches,
            timeoutMs: 900);

        Assert.False(result.IsError);
        Assert.Equal(activeWindow.Hwnd, waitService.LastTarget?.Window?.Hwnd);
        Assert.Equal(WaitTargetSourceValues.Active, waitService.LastTarget?.Source);
    }

    [Theory]
    [InlineData(WaitStatusValues.Timeout, true)]
    [InlineData(WaitStatusValues.Ambiguous, true)]
    [InlineData(WaitStatusValues.Failed, true)]
    [InlineData(WaitStatusValues.Done, false)]
    public async Task WaitMapsRuntimeStatusToIsError(string status, bool expectedIsError)
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeWaitService waitService = new((target, request, _) => Task.FromResult(
            new WaitResult(
                Status: status,
                Condition: request.Condition,
                TargetSource: target.Source,
                TargetFailureCode: target.FailureCode,
                Reason: status == WaitStatusValues.Done ? null : "wait failed",
                Window: CreateObservedWindow(target.Window ?? attachedWindow),
                TimeoutMs: request.TimeoutMs,
                ElapsedMs: 50,
                AttemptCount: 2)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            waitService: waitService);

        CallToolResult result = await tools.Wait(
            condition: WaitConditionValues.ActiveWindowMatches,
            timeoutMs: 1000);

        Assert.Equal(expectedIsError, result.IsError);
    }

    [Fact]
    public async Task WaitPassesStaleExplicitResolutionToRuntimeWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        WindowDescriptor activeWindow = CreateWindow(hwnd: 303, title: "Active", isForeground: true);
        FakeWaitService waitService = new((target, request, _) => Task.FromResult(
            new WaitResult(
                Status: WaitStatusValues.Failed,
                Condition: request.Condition,
                TargetSource: target.Source,
                TargetFailureCode: target.FailureCode,
                Reason: "stale target",
                TimeoutMs: request.TimeoutMs)));
        WindowTools tools = CreateTools(
            windows: [attachedWindow, activeWindow],
            attachedWindow: attachedWindow,
            waitService: waitService);

        CallToolResult result = await tools.Wait(
            condition: WaitConditionValues.ActiveWindowMatches,
            hwnd: 999,
            timeoutMs: 800);

        Assert.True(result.IsError);
        Assert.Null(waitService.LastTarget?.Window);
        Assert.Null(waitService.LastTarget?.Source);
        Assert.Equal(WaitTargetFailureValues.StaleExplicitTarget, waitService.LastTarget?.FailureCode);
    }

    [Fact]
    public async Task WaitReturnsFailedToolResultWhenServiceThrowsUnexpectedException()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 101, title: "Attached", isForeground: false);
        FakeWaitService waitService = new((_, _, _) => throw new InvalidOperationException("secret internal failure"));
        TestContext context = CreateContext(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            waitService: waitService);

        CallToolResult result = await context.Tools.Wait(
            condition: WaitConditionValues.ActiveWindowMatches,
            timeoutMs: 700);

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(WaitStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("Server не смог завершить wait request.", payload.GetProperty("reason").GetString());
    }

    [Fact]
    public void LiveHandlerDefaultsMatchCanonicalWaitDefaults()
    {
        MethodInfo method = typeof(WindowTools).GetMethod(nameof(WindowTools.Wait))!;
        ParameterInfo timeoutParameter = method
            .GetParameters()
            .Single(parameter => string.Equals(parameter.Name, "timeoutMs", StringComparison.Ordinal));

        Assert.Equal(WaitDefaults.TimeoutMs, Assert.IsType<int>(timeoutParameter.DefaultValue));
    }

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

    private static WindowTools CreateTools(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow,
        FakeWaitService waitService) =>
        CreateContext(windows, attachedWindow, waitService).Tools;

    private static TestContext CreateContext(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow,
        FakeWaitService waitService)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "window-wait-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "window-wait-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "window-wait-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "window-wait-tests", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("window-wait-tests"));

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
                new FakeUiAutomationService(),
                waitService),
            options.EventsPath);
    }

    private static WaitResult CreateDoneResult(WaitTargetResolution target, WaitRequest request)
    {
        WindowDescriptor targetWindow = target.Window ?? CreateWindow(hwnd: 909, title: "Fallback", isForeground: true);
        ObservedWindowDescriptor observedWindow = CreateObservedWindow(targetWindow);
        UiaElementSnapshot matchedElement = new()
        {
            ElementId = "rid:1.2/button",
            Name = "Run semantic smoke",
            AutomationId = request.Selector?.AutomationId ?? "RunSemanticSmokeButton",
            ControlType = request.Selector?.ControlType ?? "button",
            ControlTypeId = 50000,
            IsControlElement = true,
            IsContentElement = true,
            IsEnabled = true,
            Children = [],
        };
        return new WaitResult(
            Status: WaitStatusValues.Done,
            Condition: request.Condition,
            TargetSource: target.Source,
            Window: observedWindow,
            MatchedElement: matchedElement,
            LastObserved: new WaitObservation(
                MatchCount: 1,
                TargetIsForeground: observedWindow.IsForeground,
                MatchedText: request.ExpectedText,
                MatchedTextSource: request.ExpectedText is null ? null : "name"),
            ArtifactPath: @"C:\artifacts\wait.json",
            TimeoutMs: request.TimeoutMs,
            ElapsedMs: 100,
            AttemptCount: 2);
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

    private sealed record TestContext(WindowTools Tools, string EventsPath);

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в wait tests.");
    }

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => selector.Hwnd == window.Hwnd);
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }
}
