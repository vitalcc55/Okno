// SPDX-FileCopyrightText: 2025-2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class ProcessIsolatedUiAutomationActionServiceTests
{
    [Fact]
    public async Task SetValueAsyncForwardsRequestToWorkerBoundary()
    {
        RecordingWorkerProcessRunner runner = new(JsonSerializer.Serialize(UiaSetValueResult.SuccessResult("value_pattern")));
        ProcessIsolatedUiAutomationSetValueService service = new(runner);

        UiaSetValueResult result = await service.SetValueAsync(
            CreateWindow(),
            new UiaSetValueRequest("rid:1.2;raw:0/4", UiaSetValueKindValues.Text, TextValue: "hello"),
            CancellationToken.None);

        Assert.True(result.Success);
        UiAutomationWorkerInvocation invocation = Assert.IsType<UiAutomationWorkerInvocation>(runner.LastInvocation);
        Assert.Equal(UiAutomationWorkerOperationValues.SetValue, invocation.Operation);
        Assert.Equal("rid:1.2;raw:0/4", invocation.SetValueRequest?.ElementId);
        Assert.Equal("hello", invocation.SetValueRequest?.TextValue);
        Assert.Null(invocation.SnapshotRequest);
        Assert.Null(invocation.WaitProbeRequest);
        Assert.Null(invocation.SemanticLookupRequest);
    }

    [Fact]
    public async Task ScrollAsyncForwardsRequestToWorkerBoundary()
    {
        RecordingWorkerProcessRunner runner = new(JsonSerializer.Serialize(UiaScrollResult.SuccessResult("scroll_pattern", movementObserved: true)));
        ProcessIsolatedUiAutomationScrollService service = new(runner);

        UiaScrollResult result = await service.ScrollAsync(
            CreateWindow(),
            new UiaScrollRequest("rid:1.2;raw:0/5", UiaScrollDirectionValues.Down, Pages: 2),
            CancellationToken.None);

        Assert.True(result.Success);
        UiAutomationWorkerInvocation invocation = Assert.IsType<UiAutomationWorkerInvocation>(runner.LastInvocation);
        Assert.Equal(UiAutomationWorkerOperationValues.Scroll, invocation.Operation);
        Assert.Equal("rid:1.2;raw:0/5", invocation.ScrollRequest?.ElementId);
        Assert.Equal(UiaScrollDirectionValues.Down, invocation.ScrollRequest?.Direction);
        Assert.Equal(2, invocation.ScrollRequest?.Pages);
    }

    [Fact]
    public async Task SecondaryActionAsyncForwardsRequestToWorkerBoundary()
    {
        RecordingWorkerProcessRunner runner = new(JsonSerializer.Serialize(UiaSecondaryActionResult.SuccessResult(UiaSecondaryActionKindValues.Toggle, "toggle_pattern")));
        ProcessIsolatedUiAutomationSecondaryActionService service = new(runner);

        UiaSecondaryActionResult result = await service.ExecuteAsync(
            CreateWindow(),
            new UiaSecondaryActionRequest("rid:1.2;raw:0/6", UiaSecondaryActionKindValues.Toggle),
            CancellationToken.None);

        Assert.True(result.Success);
        UiAutomationWorkerInvocation invocation = Assert.IsType<UiAutomationWorkerInvocation>(runner.LastInvocation);
        Assert.Equal(UiAutomationWorkerOperationValues.SecondaryAction, invocation.Operation);
        Assert.Equal("rid:1.2;raw:0/6", invocation.SecondaryActionRequest?.ElementId);
        Assert.Equal(UiaSecondaryActionKindValues.Toggle, invocation.SecondaryActionRequest?.ActionKind);
    }

    [Fact]
    public async Task SetValueAsyncReturnsDispatchFailureWhenWorkerBoundaryTimesOut()
    {
        ProcessIsolatedUiAutomationSetValueService service = CreateSlowSetValueService("run-set-value-worker-timeout");

        UiaSetValueResult result = await service.SetValueAsync(
            CreateWindow(),
            new UiaSetValueRequest("rid:1.2;raw:0/4", UiaSetValueKindValues.Text, TextValue: "hello"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UiaSetValueFailureKindValues.DispatchFailed, result.FailureKind);
    }

    [Fact]
    public async Task ScrollAsyncReturnsDispatchFailureWhenWorkerBoundaryTimesOut()
    {
        ProcessIsolatedUiAutomationScrollService service = CreateSlowScrollService("run-scroll-worker-timeout");

        UiaScrollResult result = await service.ScrollAsync(
            CreateWindow(),
            new UiaScrollRequest("rid:1.2;raw:0/5", UiaScrollDirectionValues.Down, Pages: 2),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UiaScrollFailureKindValues.DispatchFailed, result.FailureKind);
    }

    [Fact]
    public async Task SecondaryActionAsyncReturnsDispatchFailureWhenWorkerBoundaryTimesOut()
    {
        ProcessIsolatedUiAutomationSecondaryActionService service = CreateSlowSecondaryActionService("run-secondary-action-worker-timeout");

        UiaSecondaryActionResult result = await service.ExecuteAsync(
            CreateWindow(),
            new UiaSecondaryActionRequest("rid:1.2;raw:0/6", UiaSecondaryActionKindValues.Toggle),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(UiaSecondaryActionKindValues.Toggle, result.ActionKind);
        Assert.Equal(UiaSecondaryActionFailureKindValues.DispatchFailed, result.FailureKind);
    }

    private static ProcessIsolatedUiAutomationSetValueService CreateSlowSetValueService(string runId)
    {
        string root = CreateTempDirectory();
        return new ProcessIsolatedUiAutomationSetValueService(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromMilliseconds(100)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command Start-Sleep -Seconds 30",
            diagnosticAuditLogOptions: CreateAuditLogOptions(root, runId));
    }

    private static ProcessIsolatedUiAutomationScrollService CreateSlowScrollService(string runId)
    {
        string root = CreateTempDirectory();
        return new ProcessIsolatedUiAutomationScrollService(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromMilliseconds(100)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command Start-Sleep -Seconds 30",
            diagnosticAuditLogOptions: CreateAuditLogOptions(root, runId));
    }

    private static ProcessIsolatedUiAutomationSecondaryActionService CreateSlowSecondaryActionService(string runId)
    {
        string root = CreateTempDirectory();
        return new ProcessIsolatedUiAutomationSecondaryActionService(
            TimeProvider.System,
            new UiAutomationExecutionOptions(TimeSpan.FromMilliseconds(100)),
            workerExecutablePath: "powershell.exe",
            workerArguments: "-NoLogo -NoProfile -Command Start-Sleep -Seconds 30",
            diagnosticAuditLogOptions: CreateAuditLogOptions(root, runId));
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

    private sealed class RecordingWorkerProcessRunner(string stdout) : IUiAutomationWorkerProcessRunner
    {
        public object? LastInvocation { get; private set; }

        public Task<UiAutomationWorkerProcessResult> ExecuteAsync(
            object invocation,
            long? windowHwnd,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastInvocation = invocation;
            return Task.FromResult(new UiAutomationWorkerProcessResult(
                Success: true,
                Reason: null,
                FailureStage: null,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Stdout: stdout,
                Stderr: null,
                DiagnosticArtifactPath: null));
        }
    }
}
