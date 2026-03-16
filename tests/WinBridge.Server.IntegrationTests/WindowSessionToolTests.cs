using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Server.Tools;

namespace WinBridge.Server.IntegrationTests;

public sealed class WindowSessionToolTests
{
    [Fact]
    public void AttachWindowReturnsFailedWhenSelectorIsMissing()
    {
        WindowTools tools = CreateTools(windows: [CreateWindow()]);

        AttachWindowResult result = tools.AttachWindow();

        Assert.Equal("failed", result.Status);
        Assert.Contains("Нужно указать хотя бы один селектор", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.AttachedWindow);
        Assert.Equal("desktop", result.Session.Mode);
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
            titlePatternsThatTimeout: new HashSet<string>(StringComparer.Ordinal)
            {
                "timeout-pattern",
            });

        AttachWindowResult result = tools.AttachWindow(titlePattern: "timeout-pattern");

        Assert.Equal("failed", result.Status);
        Assert.Contains("превысил допустимое время", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.AttachedWindow);
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
        IReadOnlyList<MonitorInfo> monitors =
        [
            WindowToolTestData.CreateMonitor(
                monitorId: "display-source:0000000100000000:1",
                friendlyName: "Primary monitor",
                handle: 501),
            WindowToolTestData.CreateMonitor(
                monitorId: "display-source:0000000100000000:2",
                friendlyName: "Secondary monitor",
                isPrimary: false,
                handle: 502),
        ];
        WindowTools tools = CreateTools(windows: [], monitors: monitors);

        ListMonitorsResult result = tools.ListMonitors();

        Assert.Equal(2, result.Count);
        Assert.Equal("display-source:0000000100000000:1", result.Monitors[0].MonitorId);
        Assert.Equal("Secondary monitor", result.Monitors[1].FriendlyName);
    }

    [Fact]
    public async Task ActivateWindowUsesExplicitHwndAndReturnsServiceOutcome()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 350, title: "Activatable");
        FakeWindowActivationService activationService = new(
            target => new ActivateWindowResult("done", null, target, WasMinimized: true, IsForeground: true));
        WindowTools tools = CreateTools(
            windows: [targetWindow],
            activationService: activationService);

        CallToolResult result = await tools.ActivateWindow(hwnd: targetWindow.Hwnd);

        Assert.False(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("done", payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("wasMinimized").GetBoolean());
        Assert.True(payload.GetProperty("isForeground").GetBoolean());
        Assert.Equal(targetWindow.Hwnd, activationService.LastHwnd);
    }

    [Fact]
    public async Task ActivateWindowUsesAttachedWindowWhenHwndIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 351, title: "Attached");
        FakeWindowActivationService activationService = new(
            target => new ActivateWindowResult("done", null, target, WasMinimized: false, IsForeground: true));
        WindowTools tools = CreateTools(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            activationService: activationService);

        CallToolResult result = await tools.ActivateWindow();

        Assert.False(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("done", payload.GetProperty("status").GetString());
        Assert.Equal(attachedWindow.Hwnd, activationService.LastHwnd);
    }

    [Fact]
    public async Task ActivateWindowMarksAmbiguousAsToolError()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 352, title: "Ambiguous");
        FakeWindowActivationService activationService = new(
            target => new ActivateWindowResult(
                "ambiguous",
                "Окно восстановлено, но foreground focus не удалось подтвердить.",
                target,
                WasMinimized: true,
                IsForeground: false));
        WindowTools tools = CreateTools(
            windows: [targetWindow],
            activationService: activationService);

        CallToolResult result = await tools.ActivateWindow(hwnd: targetWindow.Hwnd);

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("ambiguous", payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("wasMinimized").GetBoolean());
        Assert.False(payload.GetProperty("isForeground").GetBoolean());
    }

    [Fact]
    public async Task ActivateWindowReturnsFailedWhenTargetIsMissing()
    {
        WindowTools tools = CreateTools(windows: []);

        CallToolResult result = await tools.ActivateWindow();

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains("сначала прикрепить окно", payload.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(payload.TryGetProperty("window", out _));
    }

    [Fact]
    public async Task ActivateWindowReturnsFailedWhenAttachedWindowIdentityIsReused()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 360, title: "Original", processId: 123, threadId: 900, className: "MainWindow");
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 360, title: "Different", processId: 123, threadId: 901, className: "MainWindow");
        FakeWindowActivationService activationService = new(
            _ => throw new InvalidOperationException("Activation service should not be called for reused attached HWND."));
        WindowTools tools = CreateTools(
            windows: [reusedLiveWindow],
            attachedWindow: attachedWindow,
            activationService: activationService);

        CallToolResult result = await tools.ActivateWindow();

        Assert.True(result.IsError);
        JsonElement payload = AssertStructuredPayload(result);
        Assert.Equal("failed", payload.GetProperty("status").GetString());
        Assert.Contains("не совпадает с live target", payload.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FocusWindowUsesExplicitHwndWhenAvailable()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 401, title: "Focus target");
        TestContext context = CreateContext(
            windows: [targetWindow],
            focusResults: new Dictionary<long, bool> { [targetWindow.Hwnd] = true });

        SessionSnapshot before = context.SessionManager.GetSnapshot();
        FocusWindowResult result = context.Tools.FocusWindow(hwnd: targetWindow.Hwnd);
        SessionSnapshot after = context.SessionManager.GetSnapshot();

        Assert.Equal("done", result.Status);
        Assert.Equal(targetWindow.Hwnd, result.Window?.Hwnd);
        Assert.Equal(before, after);
    }

    [Fact]
    public void FocusWindowUsesAttachedWindowWhenHwndIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 402, title: "Attached target");
        TestContext context = CreateContext(
            windows: [attachedWindow],
            attachedWindow: attachedWindow,
            focusResults: new Dictionary<long, bool> { [attachedWindow.Hwnd] = true });

        FocusWindowResult result = context.Tools.FocusWindow();

        Assert.Equal("done", result.Status);
        Assert.Equal(attachedWindow.Hwnd, result.Window?.Hwnd);
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenTargetIsMissing()
    {
        WindowTools tools = CreateTools(windows: []);

        FocusWindowResult result = tools.FocusWindow();

        Assert.Equal("failed", result.Status);
        Assert.Contains("сначала прикрепить окно", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Window);
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenAttachedTargetIsNoLongerLive()
    {
        WindowDescriptor staleWindow = CreateWindow(hwnd: 403, title: "Stale");
        WindowTools tools = CreateTools(windows: [], attachedWindow: staleWindow);

        FocusWindowResult result = tools.FocusWindow();

        Assert.Equal("failed", result.Status);
        Assert.Contains("больше не найдено", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.Window);
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenAttachedWindowIdentityIsReused()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 403, title: "Original", processId: 123, threadId: 900, className: "MainWindow");
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 403, title: "Different", processId: 123, threadId: 901, className: "MainWindow");
        WindowTools tools = CreateTools(windows: [reusedLiveWindow], attachedWindow: attachedWindow);

        FocusWindowResult result = tools.FocusWindow();

        Assert.Equal("failed", result.Status);
        Assert.Contains("не совпадает с live target", result.Reason, StringComparison.Ordinal);
        Assert.Null(result.Window);
    }

    [Fact]
    public void FocusWindowReturnsFailedWhenForegroundRequestIsRejected()
    {
        WindowDescriptor targetWindow = CreateWindow(hwnd: 404, title: "Rejected");
        WindowTools tools = CreateTools(
            windows: [targetWindow],
            focusResults: new Dictionary<long, bool> { [targetWindow.Hwnd] = false });

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
        string root = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        AuditLogOptions options = new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: "window-session-tests",
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", "window-session-tests"),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", "window-session-tests", "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", "window-session-tests", "summary.md"));
        AuditLog auditLog = new(options, TimeProvider.System);
        InMemorySessionManager sessionManager = new(TimeProvider.System, new SessionContext("window-session-tests"));

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
            new WindowTargetResolver(windowManager));

        return new TestContext(tools, sessionManager);
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

    private static JsonElement AssertStructuredPayload(CallToolResult result)
    {
        Assert.NotNull(result.StructuredContent);
        return result.StructuredContent!.Value;
    }

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
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();

            if (!string.IsNullOrWhiteSpace(selector.TitlePattern)
                && titlePatternsThatTimeout?.Contains(selector.TitlePattern) == true)
            {
                throw new RegexMatchTimeoutException(selector.TitlePattern, selector.TitlePattern, TimeSpan.FromMilliseconds(1));
            }

            IEnumerable<WindowDescriptor> query = windows;

            if (selector.Hwnd is long hwnd)
            {
                query = query.Where(window => window.Hwnd == hwnd);
            }

            if (!string.IsNullOrWhiteSpace(selector.ProcessName))
            {
                query = query.Where(
                    window => string.Equals(
                        window.ProcessName,
                        selector.ProcessName,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(selector.TitlePattern))
            {
                query = query.Where(
                    window => Regex.IsMatch(
                        window.Title,
                        selector.TitlePattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }

            WindowDescriptor[] matches = query.Take(2).ToArray();
            return matches.Length switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new InvalidOperationException(
                    "По указанному селектору найдено несколько окон; уточни hwnd, titlePattern или processName."),
            };
        }

        public bool TryFocus(long hwnd)
        {
            if (focusResults is not null && focusResults.TryGetValue(hwnd, out bool result))
            {
                return result;
            }

            return windows.Any(window => window.Hwnd == hwnd);
        }
    }
}
