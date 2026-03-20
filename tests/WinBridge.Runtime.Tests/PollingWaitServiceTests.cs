using System.Diagnostics;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Waiting;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class PollingWaitServiceTests
{
    [Fact]
    public async Task WaitAsyncReturnsFailedForInvalidRequestAndStillWritesArtifact()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-invalid");
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([CreateWindow(hwnd: 501, isForeground: false)]),
            new FakeWindowTargetResolver(window => window),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(CreateWindow(hwnd: 501, isForeground: false)),
            new WaitRequest(
                WaitConditionValues.TextAppears,
                new WaitElementSelector(AutomationId: "SearchBox")),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath));
        Assert.Equal("failed", artifact.RootElement.GetProperty("result").GetProperty("status").GetString());

        string[] eventLines = await File.ReadAllLinesAsync(options.EventsPath);
        Assert.Single(eventLines);
        Assert.Contains("\"event_name\":\"wait.runtime.completed\"", eventLines[0], StringComparison.Ordinal);
        Assert.Contains("\"failure_stage\":\"request_validation\"", eventLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForStableForegroundTarget()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-active-done");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 601, isForeground: true);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(window => targetWindow),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(WaitTargetSourceValues.Attached, result.TargetSource);
        Assert.NotNull(result.Window);
        Assert.True(result.LastObserved?.TargetIsForeground);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task WaitAsyncReturnsAmbiguousForMultipleForegroundWindows()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-active-ambiguous");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 602, isForeground: true);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager(
            [
                targetWindow,
                CreateWindow(hwnd: 603, isForeground: true, threadId: 999),
            ]),
            new FakeWindowTargetResolver(window => targetWindow),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Ambiguous, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(2, result.LastObserved?.MatchCount);
        Assert.NotNull(result.ArtifactPath);
    }

    [Fact]
    public async Task WaitAsyncReturnsFailedWhenResolvedTargetBecomesStale()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-stale");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 604, isForeground: false);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([]),
            new FakeWindowTargetResolver(_ => null),
            new SequenceWaitProbe());

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal(WaitTargetFailureValues.StaleAttachedTarget, result.TargetFailureCode);
        Assert.Equal(1, result.AttemptCount);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForElementGoneAfterStableRecheck()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-gone");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 605, isForeground: false);
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [],
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [],
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementGone,
                new WaitElementSelector(AutomationId: "LoadingSpinner"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Null(result.MatchedElement);
    }

    [Fact]
    public async Task WaitAsyncReturnsAmbiguousForMultipleUiMatches()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-uia-ambiguous");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 606, isForeground: false);
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches =
                [
                    CreateElement("rid:1"),
                    CreateElement("rid:2"),
                ],
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(Name: "Save"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Ambiguous, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(2, result.LastObserved?.MatchCount);
    }

    [Fact]
    public async Task WaitAsyncReturnsDoneForTextAppearsAndPreservesMatchedSource()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-text");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 607, isForeground: false);
        UiaElementSnapshot element = CreateElement("rid:7");
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready to submit",
                MatchedTextSource = "value_pattern",
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready to submit",
                MatchedTextSource = "value_pattern",
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.TextAppears,
                new WaitElementSelector(AutomationId: "SubmitButton"),
                ExpectedText: "Ready",
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Done, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal("value_pattern", result.LastObserved?.MatchedTextSource);
        Assert.Equal(element.ElementId, result.MatchedElement?.ElementId);
    }

    [Fact]
    public async Task WaitAsyncReturnsTimeoutWhenConditionDoesNotStabilize()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-timeout");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 608, isForeground: false);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(5)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.True(result.AttemptCount >= 1);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
    }

    [Fact]
    public async Task WaitAsyncBoundsUiAutomationProbeByRemainingTimeout()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-bounded-probe");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 609, isForeground: false);
        DelayedWaitProbe probe = new(TimeSpan.FromMilliseconds(250));
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe,
            new WaitOptions(TimeSpan.FromMilliseconds(1)));
        Stopwatch stopwatch = Stopwatch.StartNew();

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 20),
            CancellationToken.None);

        stopwatch.Stop();

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task WaitAsyncDoesNotReturnDoneForSiblingForegroundWindowWithSameStableIdentity()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-sibling-foreground");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 610, isForeground: false, processId: 100, threadId: 200, className: "SharedWindow");
        WindowDescriptor siblingForegroundWindow = CreateWindow(hwnd: 611, isForeground: true, processId: 100, threadId: 200, className: "SharedWindow");
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow, siblingForegroundWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            new SequenceWaitProbe(),
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(WaitConditionValues.ActiveWindowMatches, TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
        Assert.False(result.LastObserved?.TargetIsForeground);
    }

    [Fact]
    public async Task WaitAsyncRequiresSameTextSourceOnFinalRecheck()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-text-same-source");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 612, isForeground: false);
        UiaElementSnapshot element = CreateElement("rid:12");
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready",
                MatchedTextSource = "value_pattern",
            },
            new UiAutomationWaitProbeResult
            {
                Window = CreateObservedWindow(targetWindow),
                Matches = [element],
                MatchedText = "Ready",
                MatchedTextSource = "name",
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe,
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.TextAppears,
                new WaitElementSelector(AutomationId: "SubmitButton"),
                ExpectedText: "Ready",
                TimeoutMs: 15),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Timeout, result.Status);
    }

    private static PollingWaitService CreateService(
        AuditLogOptions options,
        IWindowManager windowManager,
        IWindowTargetResolver windowTargetResolver,
        IUiAutomationWaitProbe probe,
        WaitOptions? waitOptions = null)
    {
        AuditLog auditLog = new(options, TimeProvider.System);
        return new PollingWaitService(
            windowManager,
            windowTargetResolver,
            probe,
            auditLog,
            options,
            TimeProvider.System,
            waitOptions ?? new WaitOptions(TimeSpan.Zero));
    }

    private static WaitTargetResolution CreateTarget(
        WindowDescriptor? window,
        string? source = WaitTargetSourceValues.Attached,
        string? failureCode = null) =>
        new(window, source, failureCode);

    private static WindowDescriptor CreateWindow(
        long hwnd,
        bool isForeground,
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: "Window",
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

    private static UiaElementSnapshot CreateElement(string elementId) =>
        new()
        {
            ElementId = elementId,
            Depth = 0,
            Ordinal = 0,
            Name = "Element",
            AutomationId = "Element",
            ClassName = "Button",
            ControlType = "button",
            ControlTypeId = 50000,
            IsControlElement = true,
            IsContentElement = true,
            IsEnabled = true,
            Children = [],
        };

    private static AuditLogOptions CreateAuditLogOptions(string root, string runId) =>
        new(
            ContentRootPath: root,
            EnvironmentName: "Tests",
            RunId: runId,
            DiagnosticsRoot: Path.Combine(root, "artifacts", "diagnostics"),
            RunDirectory: Path.Combine(root, "artifacts", "diagnostics", runId),
            EventsPath: Path.Combine(root, "artifacts", "diagnostics", runId, "events.jsonl"),
            SummaryPath: Path.Combine(root, "artifacts", "diagnostics", runId, "summary.md"));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "winbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task WaitAsyncIncludesProbeDiagnosticArtifactPathInFailureEvidence()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-diagnostic-artifact");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 613, isForeground: false);
        string diagnosticArtifactPath = Path.Combine(root, "artifacts", "diagnostics", "worker.json");
        SequenceWaitProbe probe = new(
        [
            new UiAutomationWaitProbeResult
            {
                Reason = "worker timeout",
                FailureStage = "worker_process",
                DiagnosticArtifactPath = diagnosticArtifactPath,
            },
        ]);
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe);

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 50),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal(diagnosticArtifactPath, result.LastObserved?.DiagnosticArtifactPath);

        using JsonDocument artifact = JsonDocument.Parse(await File.ReadAllTextAsync(result.ArtifactPath!));
        Assert.Equal(
            diagnosticArtifactPath,
            artifact.RootElement.GetProperty("result").GetProperty("last_observed").GetProperty("diagnostic_artifact_path").GetString());
    }

    [Fact]
    public async Task WaitAsyncDoesNotConvertDelayedProbeFailureIntoTimeout()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-delayed-failure");
        WindowDescriptor targetWindow = CreateWindow(hwnd: 614, isForeground: false);
        LateFailureWaitProbe probe = new();
        PollingWaitService service = CreateService(
            options,
            new FakeWindowManager([targetWindow]),
            new FakeWindowTargetResolver(_ => targetWindow),
            probe,
            new WaitOptions(TimeSpan.FromMilliseconds(1)));

        WaitResult result = await service.WaitAsync(
            CreateTarget(targetWindow, WaitTargetSourceValues.Attached),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 10),
            CancellationToken.None);

        Assert.Equal(WaitStatusValues.Failed, result.Status);
        Assert.Equal("worker failure", result.Reason);
        Assert.Equal("worker_process", result.LastObserved?.Detail is null ? null : "worker_process");
    }

    private sealed class SequenceWaitProbe(IReadOnlyList<UiAutomationWaitProbeResult>? results = null) : IUiAutomationWaitProbe
    {
        private readonly Queue<UiAutomationWaitProbeResult> _results = results is null
            ? new Queue<UiAutomationWaitProbeResult>()
            : new Queue<UiAutomationWaitProbeResult>(results);

        public Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (_results.Count == 0)
            {
                return Task.FromResult(
                    new UiAutomationWaitProbeExecutionResult(
                        new UiAutomationWaitProbeResult
                        {
                            Window = CreateObservedWindow(targetWindow),
                            Matches = [],
                        },
                        DateTimeOffset.UtcNow));
            }

            return Task.FromResult(new UiAutomationWaitProbeExecutionResult(_results.Dequeue(), DateTimeOffset.UtcNow));
        }
    }

    private sealed class DelayedWaitProbe(TimeSpan delay) : IUiAutomationWaitProbe
    {
        public async Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, CancellationToken.None);

            return new UiAutomationWaitProbeExecutionResult(
                new UiAutomationWaitProbeResult
                {
                    Window = CreateObservedWindow(targetWindow),
                    Matches = [],
                },
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class LateFailureWaitProbe : IUiAutomationWaitProbe
    {
        public Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
            WindowDescriptor targetWindow,
            WaitRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new UiAutomationWaitProbeExecutionResult(
                    new UiAutomationWaitProbeResult
                    {
                        FailureStage = "worker_process",
                        Reason = "worker failure",
                    },
                    DateTimeOffset.UtcNow.AddSeconds(1),
                    TimedOut: false,
                    DiagnosticArtifactPath: null));
    }

    private sealed class FakeWindowTargetResolver(Func<WindowDescriptor, WindowDescriptor?> resolver) : IWindowTargetResolver
    {
        public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();

        public WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow) => resolver(expectedWindow);

        public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();

        public WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow) =>
            throw new NotSupportedException();
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
}
