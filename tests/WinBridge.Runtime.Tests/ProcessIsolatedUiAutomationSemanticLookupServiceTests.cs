// SPDX-FileCopyrightText: 2025-2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class ProcessIsolatedUiAutomationSemanticLookupServiceTests
{
    [Fact]
    public async Task LookupAsyncForwardsRequestTimeoutToWorkerBoundary()
    {
        UiaSemanticLookupResult workerPayload = new(
            Status: UiaSemanticLookupStatusValues.ZeroMatches,
            MaxDepth: 6,
            MaxNodes: 512);
        RecordingWorkerProcessRunner runner = new(
            new UiAutomationWorkerProcessResult(
                Success: true,
                Reason: null,
                FailureStage: null,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Stdout: JsonSerializer.Serialize(workerPayload),
                Stderr: null,
                DiagnosticArtifactPath: null));
        ProcessIsolatedUiAutomationSemanticLookupService service = new(runner);
        UiaSemanticLookupRequest request = new()
        {
            Selector = new WaitElementSelector(AutomationId: "SearchBox"),
            MaxDepth = 6,
            MaxNodes = 512,
            TimeoutMs = 2500,
        };

        _ = await service.LookupAsync(CreateWindow(), request, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(2500), runner.LastTimeout);
        UiAutomationWorkerInvocation invocation = Assert.IsType<UiAutomationWorkerInvocation>(runner.LastInvocation);
        Assert.Equal(UiAutomationWorkerOperationValues.SemanticLookup, invocation.Operation);
        Assert.NotNull(invocation.SemanticLookupRequest);
        Assert.Equal("SearchBox", invocation.SemanticLookupRequest!.Selector?.AutomationId);
        Assert.Equal(2500, invocation.SemanticLookupRequest.TimeoutMs);
        Assert.Null(invocation.SnapshotRequest);
        Assert.Null(invocation.WaitProbeRequest);
    }

    [Fact]
    public async Task LookupAsyncReturnsTimeoutWhenWorkerBoundaryTimesOut()
    {
        RecordingWorkerProcessRunner runner = new(
            new UiAutomationWorkerProcessResult(
                Success: false,
                Reason: "UI Automation worker process не уложился в допустимый timeout.",
                FailureStage: UiaSnapshotFailureStageValues.Timeout,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Stdout: null,
                Stderr: null,
                DiagnosticArtifactPath: null));
        ProcessIsolatedUiAutomationSemanticLookupService service = new(runner);
        UiaSemanticLookupRequest request = CreateLookupRequest() with { TimeoutMs = 100 };

        UiaSemanticLookupResult result = await service.LookupAsync(CreateWindow(), request, CancellationToken.None);

        Assert.Equal(UiaSemanticLookupStatusValues.Timeout, result.Status);
        Assert.Equal(3, result.MaxDepth);
        Assert.Equal(128, result.MaxNodes);
        Assert.Equal("UI Automation worker process не уложился в допустимый timeout.", result.Reason);
    }

    [Fact]
    public async Task LookupAsyncFailsEarlyForInvalidRequestWithoutStartingWorker()
    {
        CountingWorkerProcessRunner runner = new();
        ProcessIsolatedUiAutomationSemanticLookupService service = new(runner);

        UiaSemanticLookupResult result = await service.LookupAsync(
            CreateWindow(),
            new UiaSemanticLookupRequest { Selector = null },
            CancellationToken.None);

        Assert.Equal(UiaSemanticLookupStatusValues.Failed, result.Status);
        Assert.Equal(UiaSemanticLookupFailureKindValues.InvalidRequest, result.FailureKind);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task LookupAsyncKillsWorkerProcessWhenProviderCallOutlivesRequestTimeout()
    {
        string root = CreateTempDirectory();
        AuditLogOptions options = CreateAuditLogOptions(root, "run-semantic-lookup-worker-timeout");
        ProcessIsolatedUiAutomationSemanticLookupService service = new(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromSeconds(30)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command Start-Sleep -Seconds 30",
            diagnosticAuditLogOptions: options);

        UiaSemanticLookupResult result = await service.LookupAsync(
            CreateWindow(),
            CreateLookupRequest() with { TimeoutMs = 100 },
            CancellationToken.None);

        Assert.Equal(UiaSemanticLookupStatusValues.Timeout, result.Status);
    }

    private static UiaSemanticLookupRequest CreateLookupRequest() =>
        new()
        {
            Selector = new WaitElementSelector(AutomationId: "SearchBox"),
            MaxDepth = 3,
            MaxNodes = 128,
            TimeoutMs = 3000,
        };

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
        public object? LastInvocation { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public Task<UiAutomationWorkerProcessResult> ExecuteAsync(
            object invocation,
            long? windowHwnd,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastInvocation = invocation;
            LastTimeout = timeout;
            return Task.FromResult(result);
        }
    }

    private sealed class CountingWorkerProcessRunner : IUiAutomationWorkerProcessRunner
    {
        public int Calls { get; private set; }

        public Task<UiAutomationWorkerProcessResult> ExecuteAsync(
            object invocation,
            long? windowHwnd,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Worker should not be called for invalid semantic lookup request.");
        }
    }
}
