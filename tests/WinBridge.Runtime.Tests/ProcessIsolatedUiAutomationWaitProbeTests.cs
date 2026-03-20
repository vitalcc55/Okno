using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.UIA;
using System.Text.Json;

namespace WinBridge.Runtime.Tests;

public sealed class ProcessIsolatedUiAutomationWaitProbeTests
{
    [Fact]
    public async Task ProbeAsyncReturnsTimeoutFailureWhenWorkerProcessDoesNotFinishInTime()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-wait-worker-timeout");
        ProcessIsolatedUiAutomationWaitProbe probe = new(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromMilliseconds(100)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command Start-Sleep -Seconds 30",
            diagnosticAuditLogOptions: options);

        UiAutomationWaitProbeExecutionResult result = await probe.ProbeAsync(
            CreateWindow(),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 3000),
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Equal(UiaSnapshotFailureStageValues.Timeout, result.Result.FailureStage);
    }

    [Fact]
    public async Task ProbeAsyncForwardsExplicitTimeoutToWorkerBoundary()
    {
        RecordingWorkerProcessRunner runner = new(
            new UiAutomationWorkerProcessResult(
                Success: true,
                Reason: null,
                FailureStage: null,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Stdout: "{}",
                Stderr: null,
                DiagnosticArtifactPath: null));
        ProcessIsolatedUiAutomationWaitProbe probe = new(runner);

        _ = await probe.ProbeAsync(
            CreateWindow(),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 9000),
            TimeSpan.FromSeconds(9),
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(9), runner.LastTimeout);
    }

    [Fact]
    public async Task ProbeAsyncPreservesDiagnosticArtifactPathFromWorkerBoundary()
    {
        string diagnosticArtifactPath = @"C:\artifacts\wait-worker.json";
        RecordingWorkerProcessRunner runner = new(
            new UiAutomationWorkerProcessResult(
                Success: false,
                Reason: "UIA worker process завершился с ошибкой.",
                FailureStage: UiaSnapshotFailureStageValues.WorkerProcess,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Stdout: null,
                Stderr: null,
                DiagnosticArtifactPath: diagnosticArtifactPath));
        ProcessIsolatedUiAutomationWaitProbe probe = new(runner);

        UiAutomationWaitProbeExecutionResult result = await probe.ProbeAsync(
            CreateWindow(),
            new WaitRequest(
                WaitConditionValues.ElementExists,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 3000),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(diagnosticArtifactPath, result.DiagnosticArtifactPath);
        Assert.Equal(diagnosticArtifactPath, result.Result.DiagnosticArtifactPath);
    }

    [Fact]
    public async Task ProbeAsyncUsesHostCompletedAtUtcAndPreservesWorkerCompletedAtUtcSeparately()
    {
        DateTimeOffset workerCompletedAtUtc = new(2026, 3, 20, 12, 30, 45, TimeSpan.Zero);
        DateTimeOffset hostCompletedAtUtc = workerCompletedAtUtc.AddMilliseconds(250);
        UiAutomationWaitProbeExecutionResult executionPayload = new(
            new UiAutomationWaitProbeResult
            {
                Window = new ObservedWindowDescriptor(42, Title: "Calculator"),
                        Matches =
                [
                    new UiaElementSnapshot
                    {
                        ElementId = "rid:1.2;path:0/0",
                        Name = "SearchBox",
                        AutomationId = "SearchBox",
                        ControlType = "edit",
                        ControlTypeId = 50004,
                        IsControlElement = true,
                        IsContentElement = true,
                        IsEnabled = true,
                        HasKeyboardFocus = true,
                        Children = [],
                    },
                ],
            },
            workerCompletedAtUtc,
            TimedOut: false,
            DiagnosticArtifactPath: null);
        RecordingWorkerProcessRunner runner = new(
            new UiAutomationWorkerProcessResult(
                Success: true,
                Reason: null,
                FailureStage: null,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: hostCompletedAtUtc,
                Stdout: JsonSerializer.Serialize(executionPayload),
                Stderr: null,
                DiagnosticArtifactPath: null));
        ProcessIsolatedUiAutomationWaitProbe probe = new(runner);

        UiAutomationWaitProbeExecutionResult result = await probe.ProbeAsync(
            CreateWindow(),
            new WaitRequest(
                WaitConditionValues.FocusIs,
                new WaitElementSelector(AutomationId: "SearchBox"),
                TimeoutMs: 3000),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(hostCompletedAtUtc, result.CompletedAtUtc);
        Assert.Equal(workerCompletedAtUtc, result.WorkerCompletedAtUtc);
        Assert.False(result.TimedOut);
        Assert.Equal("rid:1.2;path:0/0", result.Result.Matches.Single().ElementId);
    }

    private static WindowDescriptor CreateWindow() =>
        new(
            Hwnd: 42,
            Title: "Calculator",
            ProcessName: "CalculatorApp",
            ProcessId: 42,
            ThreadId: 84,
            ClassName: "CalcWindow",
            Bounds: new Bounds(0, 0, 800, 600),
            IsForeground: true,
            IsVisible: true);

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

    private sealed class RecordingWorkerProcessRunner(UiAutomationWorkerProcessResult result) : IUiAutomationWorkerProcessRunner
    {
        public TimeSpan? LastTimeout { get; private set; }

        public Task<UiAutomationWorkerProcessResult> ExecuteAsync(
            object invocation,
            long? windowHwnd,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastTimeout = timeout;
            return Task.FromResult(result);
        }
    }
}
