// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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

public sealed class WindowWaitToolTests
{
    private const string RunId = "window-wait-tests";
    private const string SemanticSmokeButtonAutomationId = "RunSemanticSmokeButton";

    [Fact]
    public async Task WaitUsesExplicitTargetAndPublishesRuntimePayload()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        WindowDescriptor explicitWindow = CreateExplicitWindow();
        WindowDescriptor activeWindow = CreateActiveWindow();
        FakeWaitService waitService = CreateWaitService(CreateDoneResult);
        WindowTools tools = CreateToolsWithAttachedWindow(waitService, attachedWindow, explicitWindow, activeWindow);

        CallToolResult result = await tools.Wait(
            condition: WaitConditionValues.ElementExists,
            selector: new WaitElementSelector(AutomationId: SemanticSmokeButtonAutomationId),
            hwnd: explicitWindow.Hwnd,
            timeoutMs: 1500);

        Assert.False(result.IsError);
        Assert.Equal(explicitWindow.Hwnd, waitService.LastTarget?.Window?.Hwnd);
        Assert.Equal(WaitTargetSourceValues.Explicit, waitService.LastTarget?.Source);
        Assert.Equal(SemanticSmokeButtonAutomationId, waitService.LastRequest?.Selector?.AutomationId);

        TextContentBlock textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("\"condition\":\"element_exists\"", textBlock.Text, StringComparison.Ordinal);

        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(WaitStatusValues.Done, payload.GetProperty("status").GetString());
        Assert.Equal(WaitConditionValues.ElementExists, payload.GetProperty("condition").GetString());
        Assert.Equal(WaitTargetSourceValues.Explicit, payload.GetProperty("targetSource").GetString());
        Assert.Equal(explicitWindow.Hwnd, payload.GetProperty("window").GetProperty("hwnd").GetInt64());
        Assert.Equal(SemanticSmokeButtonAutomationId, payload.GetProperty("matchedElement").GetProperty("automationId").GetString());
    }

    [Fact]
    public async Task WaitUsesAttachedTargetWhenExplicitTargetIsMissing()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeWaitService waitService = CreateWaitService(CreateDoneResult);
        WindowTools tools = CreateToolsWithAttachedWindow(waitService, attachedWindow);

        CallToolResult result = await tools.Wait(condition: WaitConditionValues.ActiveWindowMatches, timeoutMs: 1200);

        Assert.False(result.IsError);
        Assert.Equal(WaitTargetSourceValues.Attached, waitService.LastTarget?.Source);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(WaitTargetSourceValues.Attached, payload.GetProperty("targetSource").GetString());
    }

    [Fact]
    public async Task WaitUsesActiveTargetWhenNoExplicitOrAttachedTargetExists()
    {
        WindowDescriptor activeWindow = CreateActiveWindow();
        FakeWaitService waitService = CreateWaitService(CreateDoneResult);
        WindowTools tools = CreateToolsWithoutAttachedWindow(waitService, activeWindow);

        CallToolResult result = await tools.Wait(condition: WaitConditionValues.ActiveWindowMatches, timeoutMs: 900);

        Assert.False(result.IsError);
        Assert.Equal(activeWindow.Hwnd, waitService.LastTarget?.Window?.Hwnd);
        Assert.Equal(WaitTargetSourceValues.Active, waitService.LastTarget?.Source);
    }

    [Fact]
    public async Task WaitPublishesVisualEvidenceStatusAsFlatLastObservedField()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeWaitService waitService = CreateWaitService(CreateVisualDoneResult);
        WindowTools tools = CreateToolsWithAttachedWindow(waitService, attachedWindow);

        CallToolResult result = await tools.Wait(condition: WaitConditionValues.VisualChanged, timeoutMs: 1200);

        Assert.False(result.IsError);
        JsonElement lastObserved = AssertStructuredPayload(result).GetProperty("lastObserved");
        Assert.Equal(WaitVisualEvidenceStatusValues.Timeout, lastObserved.GetProperty("visualEvidenceStatus").GetString());
        Assert.False(lastObserved.TryGetProperty("visualBaselineArtifactPath", out _));
        Assert.False(lastObserved.TryGetProperty("visualCurrentArtifactPath", out _));
    }

    [Theory]
    [InlineData(WaitStatusValues.Timeout, true)]
    [InlineData(WaitStatusValues.Ambiguous, true)]
    [InlineData(WaitStatusValues.Failed, true)]
    [InlineData(WaitStatusValues.Done, false)]
    public async Task WaitMapsRuntimeStatusToIsError(string status, bool expectedIsError)
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeWaitService waitService = CreateWaitService(
            (target, request) => CreateRuntimeStatusResult(status, target, request, attachedWindow));
        WindowTools tools = CreateToolsWithAttachedWindow(waitService, attachedWindow);

        CallToolResult result = await tools.Wait(condition: WaitConditionValues.ActiveWindowMatches, timeoutMs: 1000);

        Assert.Equal(expectedIsError, result.IsError);
    }

    [Fact]
    public async Task WaitPassesStaleExplicitResolutionToRuntimeWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        WindowDescriptor activeWindow = CreateActiveWindow();
        FakeWaitService waitService = CreateWaitService((target, request) => new WaitResult(
            Status: WaitStatusValues.Failed,
            Condition: request.Condition,
            TargetSource: target.Source,
            TargetFailureCode: target.FailureCode,
            Reason: "stale target",
            TimeoutMs: request.TimeoutMs));
        WindowTools tools = CreateToolsWithAttachedWindow(waitService, attachedWindow, activeWindow);

        CallToolResult result = await tools.Wait(condition: WaitConditionValues.ActiveWindowMatches, hwnd: 999, timeoutMs: 800);

        Assert.True(result.IsError);
        Assert.Null(waitService.LastTarget?.Window);
        Assert.Null(waitService.LastTarget?.Source);
        Assert.Equal(WaitTargetFailureValues.StaleExplicitTarget, waitService.LastTarget?.FailureCode);
    }

    [Fact]
    public async Task WaitReturnsFailedToolResultWhenServiceThrowsUnexpectedException()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeWaitService waitService = new((_, _, _) => throw new InvalidOperationException("secret internal failure"));
        TestContext context = CreateContextWithAttachedWindow(waitService, attachedWindow);

        CallToolResult result = await context.Tools.Wait(condition: WaitConditionValues.ActiveWindowMatches, timeoutMs: 700);

        await AssertUnexpectedFailureIsMaterializedWithoutSecretAsync(context, result, "secret internal failure");
    }

    [Fact]
    public async Task WaitReturnsFailedToolResultWhenTargetResolutionThrowsUnexpectedException()
    {
        WindowDescriptor attachedWindow = CreateAttachedWindow();
        FakeWaitService waitService = new((_, _, _) => throw new NotSupportedException("Wait service не должен вызываться."));
        TestContext context = CreateContextWithAttachedWindow(
            waitService,
            attachedWindow,
            new ThrowingWindowTargetResolver(new InvalidOperationException("secret resolution failure")));

        CallToolResult result = await context.Tools.Wait(condition: WaitConditionValues.ActiveWindowMatches, timeoutMs: 700);

        Assert.Equal(0, waitService.Calls);
        await AssertUnexpectedFailureIsMaterializedWithoutSecretAsync(context, result, "secret resolution failure");
    }

    [Fact]
    public void LiveHandlerDefaultsMatchCanonicalWaitDefaults()
    {
        ParameterInfo timeoutParameter = typeof(WindowTools)
            .GetMethod(nameof(WindowTools.Wait))!
            .GetParameters()
            .Single(parameter => string.Equals(parameter.Name, "timeoutMs", StringComparison.Ordinal));

        Assert.Equal(WaitDefaults.TimeoutMs, Assert.IsType<int>(timeoutParameter.DefaultValue));
    }

    private static async Task AssertUnexpectedFailureIsMaterializedWithoutSecretAsync(
        TestContext context,
        CallToolResult result,
        string secret)
    {
        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal(WaitStatusValues.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("Server не смог завершить wait request.", payload.GetProperty("reason").GetString());

        string artifactPath = payload.GetProperty("artifactPath").GetString()!;
        Assert.True(File.Exists(artifactPath), $"Wait artifact '{artifactPath}' was not created.");

        string[] eventLines = await File.ReadAllLinesAsync(context.EventsPath);
        Assert.Contains(eventLines, line => line.Contains("\"event_name\":\"wait.runtime.completed\"", StringComparison.Ordinal));
        Assert.DoesNotContain(eventLines, line => line.Contains(secret, StringComparison.Ordinal));
        Assert.Contains(eventLines, line => line.Contains("\"redacted_fields\":\"exception_message\"", StringComparison.Ordinal));
    }

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

    private static WindowTools CreateToolsWithAttachedWindow(
        FakeWaitService waitService,
        WindowDescriptor attachedWindow,
        params WindowDescriptor[] otherWindows) =>
        CreateContext([attachedWindow, ..otherWindows], attachedWindow, waitService).Tools;

    private static WindowTools CreateToolsWithoutAttachedWindow(FakeWaitService waitService, params WindowDescriptor[] windows) =>
        CreateContext(windows, null, waitService).Tools;

    private static TestContext CreateContextWithAttachedWindow(
        FakeWaitService waitService,
        WindowDescriptor attachedWindow,
        IWindowTargetResolver? windowTargetResolver = null) =>
        CreateContext([attachedWindow], attachedWindow, waitService, windowTargetResolver);

    private static TestContext CreateContext(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow,
        FakeWaitService waitService,
        IWindowTargetResolver? windowTargetResolver = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, RunId);
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: RunId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext(RunId));

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
                windowTargetResolver ?? new WindowTargetResolver(windowManager),
                new FakeUiAutomationService(),
                waitService,
                waitResultMaterializer,
                new FakeToolExecutionGate(),
                new FakeInputService(),
                new FakeProcessLaunchService(),
                new FakeOpenTargetService()),
            options.EventsPath);
    }

    private static FakeWaitService CreateWaitService(Func<WaitTargetResolution, WaitRequest, WaitResult> resultFactory) =>
        new((target, request, _) => Task.FromResult(resultFactory(target, request)));

    private static WaitResult CreateDoneResult(WaitTargetResolution target, WaitRequest request)
    {
        WindowDescriptor targetWindow = target.Window ?? CreateFallbackWindow();
        ObservedWindowDescriptor observedWindow = CreateObservedWindow(targetWindow);
        UiaElementSnapshot matchedElement = new()
        {
            ElementId = "rid:1.2/button",
            Name = "Run semantic smoke",
            AutomationId = request.Selector?.AutomationId ?? SemanticSmokeButtonAutomationId,
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

    private static WaitResult CreateVisualDoneResult(WaitTargetResolution target, WaitRequest request)
    {
        WindowDescriptor targetWindow = target.Window ?? CreateFallbackWindow();
        ObservedWindowDescriptor observedWindow = CreateObservedWindow(targetWindow);
        return new WaitResult(
            Status: WaitStatusValues.Done,
            Condition: request.Condition,
            TargetSource: target.Source,
            Window: observedWindow,
            LastObserved: new WaitObservation(
                Detail: "Визуальное изменение подтверждено.",
                VisualDifferenceRatio: 0.25,
                VisualDifferenceThreshold: 0.0625,
                VisualEvidenceStatus: WaitVisualEvidenceStatusValues.Timeout),
            ArtifactPath: @"C:\artifacts\wait.json",
            TimeoutMs: request.TimeoutMs,
            ElapsedMs: 100,
            AttemptCount: 3);
    }

    private static WaitResult CreateRuntimeStatusResult(
        string status,
        WaitTargetResolution target,
        WaitRequest request,
        WindowDescriptor fallbackWindow) =>
        new(
            Status: status,
            Condition: request.Condition,
            TargetSource: target.Source,
            TargetFailureCode: target.FailureCode,
            Reason: status == WaitStatusValues.Done ? null : "wait failed",
            Window: CreateObservedWindow(target.Window ?? fallbackWindow),
            TimeoutMs: request.TimeoutMs,
            ElapsedMs: 50,
            AttemptCount: 2);

    private static WindowDescriptor CreateAttachedWindow() => CreateWindow(101, "Attached", isForeground: false);
    private static WindowDescriptor CreateExplicitWindow() => CreateWindow(202, "Explicit", isForeground: false);
    private static WindowDescriptor CreateActiveWindow() => CreateWindow(303, "Active", isForeground: true);
    private static WindowDescriptor CreateFallbackWindow() => CreateWindow(909, "Fallback", isForeground: true);

    private static WindowDescriptor CreateWindow(long hwnd, string title, bool isForeground) =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: "okno-tests",
            ProcessId: 123,
            ThreadId: 456,
            ClassName: "OknoWindow",
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

    private sealed class ThrowingWindowTargetResolver(Exception exception) : IWindowTargetResolver
    {
        public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow) => throw exception;
        public LiveWindowIdentityResolution ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow) => throw exception;
        public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) => throw exception;
        public InputTargetResolution ResolveInputTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) => throw exception;
        public WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) => throw exception;
    }
}
