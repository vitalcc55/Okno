// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowSessionToolTests
{
    private const string RunId = "window-session-tests";

    [Fact]
    public void AttachWindowReturnsFailedWhenSelectorIsMissing()
    {
        WindowTools tools = CreateTools(windows: [CreateWindow()]);

        AttachWindowResult result = tools.AttachWindow();

        AssertAttachFailed(result, "Нужно указать хотя бы один селектор", sessionMode: "desktop");
    }

    [Fact]
    public void AttachWindowReturnsAmbiguousWhenSelectorMatchesMultipleWindows()
    {
        WindowTools tools = CreateTools(
            windows:
            [
                CreateWindow(hwnd: 101, title: "One", processName: "shared"),
                CreateWindow(hwnd: 202, title: "Two", processName: "shared"),
            ]);

        AttachWindowResult result = tools.AttachWindow(processName: "shared");

        Assert.Equal("ambiguous", result.Status);
        Assert.Contains("найдено несколько окон", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.AttachedWindow);
        Assert.Equal("desktop", result.Session.Mode);
    }

    [Fact]
    public void AttachWindowReturnsFailedWhenTitlePatternTimesOut()
    {
        WindowTools tools = CreateTools(
            windows: [CreateWindow()],
            titlePatternsThatTimeout: new HashSet<string>(StringComparer.Ordinal) { "timeout-pattern" });

        AttachWindowResult result = tools.AttachWindow(titlePattern: "timeout-pattern");

        AssertAttachFailed(result, "превысил допустимое время");
    }

    [Fact]
    public void AttachWindowReturnsAlreadyAttachedForSameWindow()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 303, title: "Attached");
        TestContext context = CreateContext(windows: [attachedWindow], attachedWindow: attachedWindow);

        AttachWindowResult result = context.Tools.AttachWindow(hwnd: attachedWindow.Hwnd);

        Assert.Equal("already_attached", result.Status);
        Assert.Equal(attachedWindow.Hwnd, result.AttachedWindow?.Window.Hwnd);
        Assert.Equal("window", result.Session.Mode);
    }

    [Fact]
    public void ListMonitorsReturnsConfiguredMonitorInventory()
    {
        WindowTools tools = CreateTools(
            windows: [],
            monitors:
            [
                WindowToolTestData.CreateMonitor(monitorId: "display-source:0000000100000000:1", friendlyName: "Primary monitor", handle: 501),
                WindowToolTestData.CreateMonitor(monitorId: "display-source:0000000100000000:2", friendlyName: "Secondary monitor", isPrimary: false, handle: 502),
            ]);

        ListMonitorsResult result = tools.ListMonitors();

        Assert.Equal(2, result.Count);
        Assert.Equal("display-source:0000000100000000:1", result.Monitors[0].MonitorId);
        Assert.Equal("Secondary monitor", result.Monitors[1].FriendlyName);
        Assert.Equal(DisplayIdentityModeValues.DisplayConfigStrong, result.Diagnostics.IdentityMode);
    }

    [Fact]
    public async Task ActivateWindowRejectsExplicitHwndWithoutAttachedIdentity()
    {
        WindowTools tools = CreateTools(windows: [CreateWindow(hwnd: 350, title: "Activatable")]);

        CallToolResult result = await tools.ActivateWindow();

        JsonElement payload = AssertToolError(result, "failed");
        Assert.Contains("сначала прикрепи окно", JsonString(payload, "reason"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ActivationFailureKindValues.MissingTarget, JsonString(payload, "failureKind"));
    }

    [Fact]
    public async Task ActivateWindowUsesAttachedWindowWhenHwndIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 351, title: "Attached");
        FakeWindowActivationService activationService = new(target => ActivateWindowResult.Done(target, wasMinimized: false, isForeground: true));
        WindowTools tools = CreateTools(windows: [attachedWindow], attachedWindow: attachedWindow, activationService: activationService);

        CallToolResult result = await tools.ActivateWindow();

        AssertToolSuccess(result, "done");
        Assert.Equal(attachedWindow.Hwnd, activationService.LastHwnd);
    }

    [Fact]
    public async Task ActivateWindowMarksAmbiguousAsToolError()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 352, title: "Ambiguous");
        FakeWindowActivationService activationService = new(
            target => ActivateWindowResult.Ambiguous(
                "Окно восстановлено, но foreground focus не удалось подтвердить.",
                target,
                wasMinimized: true,
                isForeground: false,
                failureKind: ActivationFailureKindValues.ForegroundNotConfirmed));
        WindowTools tools = CreateTools(windows: [targetWindow], attachedWindow: targetWindow, activationService: activationService);

        CallToolResult result = await tools.ActivateWindow();

        JsonElement payload = AssertToolError(result, "ambiguous");
        Assert.True(payload.GetProperty("wasMinimized").GetBoolean());
        Assert.False(payload.GetProperty("isForeground").GetBoolean());
        Assert.Equal(ActivationFailureKindValues.ForegroundNotConfirmed, JsonString(payload, "failureKind"));
    }

    [Fact]
    public async Task ActivateWindowReturnsFailedWhenTargetIsMissing()
    {
        WindowTools tools = CreateTools(windows: []);

        CallToolResult result = await tools.ActivateWindow();

        JsonElement payload = AssertToolError(result, "failed");
        Assert.Contains("сначала прикрепи окно", JsonString(payload, "reason"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ActivationFailureKindValues.MissingTarget, JsonString(payload, "failureKind"));
        Assert.False(payload.TryGetProperty("window", out _));
    }

    [Fact]
    public async Task ActivateWindowReturnsFailedWhenAttachedWindowIdentityIsReused()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 360, title: "Original", threadId: 900, className: "MainWindow");
        WindowDescriptor reusedLiveWindow = attachedWindow with { Title = "Different", ThreadId = 901 };
        FakeWindowActivationService activationService = new(
            _ => throw new InvalidOperationException("Activation service should not be called for reused attached HWND."));
        WindowTools tools = CreateTools(windows: [reusedLiveWindow], attachedWindow: attachedWindow, activationService: activationService);

        CallToolResult result = await tools.ActivateWindow();

        JsonElement payload = AssertToolError(result, "failed");
        Assert.Contains("identity", JsonString(payload, "reason"), StringComparison.Ordinal);
        Assert.Equal(ActivationFailureKindValues.IdentityChanged, JsonString(payload, "failureKind"));
    }

    [Fact]
    public void AttachWindowReturnsFailedWhenWindowIdentityIsIncomplete()
    {
        WindowDescriptor weakIdentityWindow = CreateWindow(hwnd: 304, title: "Weak identity") with { ProcessId = null };
        WindowTools tools = CreateTools(windows: [weakIdentityWindow]);

        AttachWindowResult result = tools.AttachWindow(hwnd: weakIdentityWindow.Hwnd);

        AssertAttachFailed(result, "отсутствует ProcessId", sessionMode: "desktop");
    }

    [Fact]
    public void FocusWindowUsesExplicitHwndWhenAvailable()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 401, title: "Focus target");
        TestContext context = CreateContext(windows: [targetWindow], focusResults: FocusResultFor(targetWindow));

        SessionSnapshot before = context.SessionManager.GetSnapshot();
        FocusWindowResult result = context.Tools.FocusWindow(hwnd: targetWindow.Hwnd);
        SessionSnapshot after = context.SessionManager.GetSnapshot();

        AssertFocusDone(result, targetWindow);
        Assert.Equal(before, after);
    }

    [Fact]
    public void FocusWindowUsesExplicitHwndEvenWhenStableIdentitySignalsAreMissing()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 401, title: "Weak explicit") with { ProcessId = null, ThreadId = null, ClassName = null };
        TestContext context = CreateContext(windows: [targetWindow], focusResults: FocusResultFor(targetWindow));

        FocusWindowResult result = context.Tools.FocusWindow(hwnd: targetWindow.Hwnd);

        AssertFocusDone(result, targetWindow);
    }

    [Fact]
    public void FocusWindowUsesAttachedWindowWhenHwndIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 402, title: "Attached target");
        TestContext context = CreateContext(windows: [attachedWindow], attachedWindow: attachedWindow, focusResults: FocusResultFor(attachedWindow));

        FocusWindowResult result = context.Tools.FocusWindow();

        AssertFocusDone(result, attachedWindow);
    }

    [Fact]
    public void FocusWindowDoesNotFallbackToAttachedWindowForExplicitZeroHwnd()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 402, title: "Attached target");
        TestContext context = CreateContext(windows: [attachedWindow], attachedWindow: attachedWindow, focusResults: FocusResultFor(attachedWindow));

        FocusWindowResult result = context.Tools.FocusWindow(hwnd: 0);

        AssertFocusFailedWithoutWindow(result, "Окно для фокуса больше не найдено");
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenTargetIsMissing()
    {
        WindowTools tools = CreateTools(windows: []);

        FocusWindowResult result = tools.FocusWindow();

        AssertFocusFailedWithoutWindow(result, "сначала прикрепить окно", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenAttachedTargetIsNoLongerLive()
    {
        WindowDescriptor staleWindow = CreateWindow(hwnd: 403, title: "Stale");
        WindowTools tools = CreateTools(windows: [], attachedWindow: staleWindow);

        FocusWindowResult result = tools.FocusWindow();

        AssertFocusFailedWithoutWindow(result, "больше не найдено");
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenAttachedWindowIdentityIsReused()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 403, title: "Original", threadId: 900, className: "MainWindow");
        WindowDescriptor reusedLiveWindow = attachedWindow with { Title = "Different", ThreadId = 901 };
        WindowTools tools = CreateTools(windows: [reusedLiveWindow], attachedWindow: attachedWindow);

        FocusWindowResult result = tools.FocusWindow();

        AssertFocusFailedWithoutWindow(result, "не совпадает с live target");
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenForegroundRequestIsRejected()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 404, title: "Rejected");
        WindowTools tools = CreateTools(windows: [targetWindow], focusResults: FocusResultFor(targetWindow, succeeds: false));

        FocusWindowResult result = tools.FocusWindow(hwnd: targetWindow.Hwnd);

        Assert.Equal("failed", result.Status);
        Assert.Contains("Windows отказалась перевести окно в foreground", result.Reason, StringComparison.Ordinal);
        Assert.NotNull(result.Window);
        Assert.Equal(targetWindow.Hwnd, result.Window!.Hwnd);
    }

    private static WindowTools CreateTools(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow = null,
        IReadOnlySet<string>? titlePatternsThatTimeout = null,
        IReadOnlyDictionary<long, bool>? focusResults = null,
        IReadOnlyList<MonitorInfo>? monitors = null,
        FakeWindowActivationService? activationService = null) =>
        CreateContext(windows, attachedWindow, titlePatternsThatTimeout, focusResults, monitors, activationService).Tools;

    private static TestContext CreateContext(
        IReadOnlyList<WindowDescriptor> windows,
        WindowDescriptor? attachedWindow = null,
        IReadOnlySet<string>? titlePatternsThatTimeout = null,
        IReadOnlyDictionary<long, bool>? focusResults = null,
        IReadOnlyList<MonitorInfo>? monitors = null,
        FakeWindowActivationService? activationService = null)
    {
        AuditLogOptions options = CreateAuditLogOptions();
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext(RunId));

        if (attachedWindow is not null)
        {
            sessionManager.Attach(attachedWindow, "hwnd");
        }

        FakeWindowManager windowManager = new(windows, titlePatternsThatTimeout, focusResults);
        WindowTools tools = new(
            auditLog,
            sessionManager,
            windowManager,
            new NoopCaptureService(),
            new FakeMonitorManager(monitors),
            activationService ?? new FakeWindowActivationService(),
            new WindowTargetResolver(windowManager),
            new FakeUiAutomationService(),
            new FakeWaitService(),
            new WaitResultMaterializer(auditLog, options, WaitOptions.Default),
            new FakeToolExecutionGate(),
            new FakeInputService(),
            new FakeProcessLaunchService(),
            new FakeOpenTargetService());

        return new TestContext(tools, sessionManager);
    }

    private static AuditLogOptions CreateAuditLogOptions()
    {
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        string diagnosticsRoot = Path.Combine(root, "artifacts", "diagnostics");
        string runDirectory = Path.Combine(diagnosticsRoot, RunId);
        Directory.CreateDirectory(root);

        return new AuditLogOptions(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: RunId,
            DiagnosticsRoot: diagnosticsRoot,
            RunDirectory: runDirectory,
            EventsPath: Path.Combine(runDirectory, "events.jsonl"),
            SummaryPath: Path.Combine(runDirectory, "summary.md"));
    }

    private static WindowDescriptor CreateWindow(
        long hwnd = 42,
        string title = "Window",
        string processName = "okno-tests",
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: processName,
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: true,
            IsVisible: true);

    private static Dictionary<long, bool> FocusResultFor(WindowDescriptor window, bool succeeds = true) =>
        new Dictionary<long, bool> { [window.Hwnd] = succeeds };

    private static void AssertAttachFailed(AttachWindowResult result, string reasonFragment, string? sessionMode = null)
    {
        Assert.Equal("failed", result.Status);
        Assert.Contains(reasonFragment, result.Reason, StringComparison.Ordinal);
        Assert.Null(result.AttachedWindow);

        if (sessionMode is not null)
        {
            Assert.Equal(sessionMode, result.Session.Mode);
        }
    }

    private static void AssertFocusDone(FocusWindowResult result, WindowDescriptor expectedWindow)
    {
        Assert.Equal("done", result.Status);
        Assert.Equal(expectedWindow.Hwnd, result.Window?.Hwnd);
    }

    private static void AssertFocusFailedWithoutWindow(
        FocusWindowResult result,
        string reasonFragment,
        StringComparison comparison = StringComparison.Ordinal)
    {
        Assert.Equal("failed", result.Status);
        Assert.Contains(reasonFragment, result.Reason, comparison);
        Assert.Null(result.Window);
    }

    private static JsonElement AssertToolError(CallToolResult result, string expectedStatus) =>
        AssertToolPayload(result, expectedStatus, expectedIsError: true);

    private static JsonElement AssertToolSuccess(CallToolResult result, string expectedStatus) =>
        AssertToolPayload(result, expectedStatus, expectedIsError: false);

    private static JsonElement AssertToolPayload(CallToolResult result, string expectedStatus, bool expectedIsError)
    {
        Assert.Equal(expectedIsError, result.IsError);
        Assert.NotNull(result.StructuredContent);
        JsonElement payload = result.StructuredContent!.Value;
        Assert.Equal(expectedStatus, JsonString(payload, "status"));
        return payload;
    }

    private static string? JsonString(JsonElement payload, string propertyName) =>
        payload.GetProperty(propertyName).GetString();

    private sealed record TestContext(WindowTools Tools, InMemorySessionManager SessionManager);

    private sealed class NoopCaptureService : ICaptureService
    {
        public Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Capture не должен вызываться в тестах attach/focus.");
    }

    private sealed class FakeWindowManager(
        IReadOnlyList<WindowDescriptor> windows,
        IReadOnlySet<string>? titlePatternsThatTimeout,
        IReadOnlyDictionary<long, bool>? focusResults) : IWindowManager
    {
        private const RegexOptions TitlePatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();

            if (!string.IsNullOrWhiteSpace(selector.TitlePattern)
                && titlePatternsThatTimeout?.Contains(selector.TitlePattern) == true)
            {
                throw new RegexMatchTimeoutException(selector.TitlePattern, selector.TitlePattern, TimeSpan.FromMilliseconds(1));
            }

            WindowDescriptor? match = null;

            foreach (WindowDescriptor window in windows)
            {
                if (selector.Hwnd is long hwnd && window.Hwnd != hwnd)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(selector.ProcessName)
                    && !string.Equals(window.ProcessName, selector.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(selector.TitlePattern)
                    && !Regex.IsMatch(window.Title, selector.TitlePattern, TitlePatternOptions))
                {
                    continue;
                }

                if (match is not null)
                {
                    throw new InvalidOperationException(
                        "По указанному селектору найдено несколько окон; уточни hwnd, titlePattern или processName.");
                }

                match = window;
            }

            return match;
        }

        public bool TryFocus(long hwnd) =>
            focusResults is not null && focusResults.TryGetValue(hwnd, out bool result)
                ? result
                : windows.Any(window => window.Hwnd == hwnd);
    }
}
