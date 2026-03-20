using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class ProcessIsolatedUiAutomationWaitProbe : IUiAutomationWaitProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IUiAutomationWorkerProcessRunner _workerRunner;

    public ProcessIsolatedUiAutomationWaitProbe(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(new UiAutomationWorkerProcessRunner(timeProvider, auditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationWaitProbe(
        TimeProvider timeProvider,
        UiAutomationExecutionOptions executionOptions,
        string workerExecutablePath,
        string? workerArguments,
        AuditLogOptions? diagnosticAuditLogOptions = null)
        : this(
            new UiAutomationWorkerProcessRunner(
                timeProvider,
                executionOptions,
                workerExecutablePath,
                workerArguments,
                diagnosticAuditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationWaitProbe(
        TimeProvider timeProvider,
        UiAutomationExecutionOptions executionOptions,
        Func<UiaWorkerLaunchSpec> workerLaunchSpecResolver,
        AuditLogOptions? diagnosticAuditLogOptions = null)
        : this(
            new UiAutomationWorkerProcessRunner(
                timeProvider,
                executionOptions,
                workerLaunchSpecResolver,
                diagnosticAuditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationWaitProbe(IUiAutomationWorkerProcessRunner workerRunner)
    {
        _workerRunner = workerRunner;
    }

    public async Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
        WindowDescriptor targetWindow,
        WaitRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);

        UiAutomationWorkerProcessResult execution = await _workerRunner
            .ExecuteAsync(
                new UiAutomationWorkerInvocation(
                    UiAutomationWorkerOperationValues.WaitProbe,
                    targetWindow,
                    SnapshotRequest: null,
                    WaitProbeRequest: request),
                targetWindow.Hwnd,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.Success)
        {
            return new UiAutomationWaitProbeExecutionResult(
                new UiAutomationWaitProbeResult
                {
                    Reason = execution.Reason,
                    FailureStage = execution.FailureStage,
                    DiagnosticArtifactPath = execution.DiagnosticArtifactPath,
                },
                execution.CompletedAtUtc,
                TimedOut: string.Equals(execution.FailureStage, UiaSnapshotFailureStageValues.Timeout, StringComparison.Ordinal),
                DiagnosticArtifactPath: execution.DiagnosticArtifactPath);
        }

        try
        {
            UiAutomationWaitProbeExecutionResult? result = JsonSerializer.Deserialize<UiAutomationWaitProbeExecutionResult>(execution.Stdout ?? string.Empty, JsonOptions);
            UiAutomationWaitProbeExecutionResult materialized = result ?? new UiAutomationWaitProbeExecutionResult(
                new UiAutomationWaitProbeResult
                {
                    Reason = "UIA worker process вернул пустой wait probe payload.",
                    FailureStage = UiaSnapshotFailureStageValues.WorkerProcess,
                    DiagnosticArtifactPath = execution.DiagnosticArtifactPath,
                },
                execution.CompletedAtUtc,
                TimedOut: false,
                DiagnosticArtifactPath: execution.DiagnosticArtifactPath);
            UiAutomationWaitProbeResult materializedResult = materialized.Result ?? new UiAutomationWaitProbeResult
            {
                Reason = "UIA worker process вернул пустой wait probe payload.",
                FailureStage = UiaSnapshotFailureStageValues.WorkerProcess,
                DiagnosticArtifactPath = execution.DiagnosticArtifactPath,
            };
            return new UiAutomationWaitProbeExecutionResult(
                materializedResult with { DiagnosticArtifactPath = materializedResult.DiagnosticArtifactPath ?? materialized.DiagnosticArtifactPath ?? execution.DiagnosticArtifactPath },
                execution.CompletedAtUtc,
                TimedOut: materialized.TimedOut,
                DiagnosticArtifactPath: materialized.DiagnosticArtifactPath ?? execution.DiagnosticArtifactPath,
                WorkerCompletedAtUtc: materialized.WorkerCompletedAtUtc ?? materialized.CompletedAtUtc);
        }
        catch (JsonException)
        {
            return new UiAutomationWaitProbeExecutionResult(
                new UiAutomationWaitProbeResult
                {
                    Reason = "UIA worker process вернул некорректный wait probe payload.",
                    FailureStage = UiaSnapshotFailureStageValues.WorkerProcess,
                    DiagnosticArtifactPath = execution.DiagnosticArtifactPath,
                },
                execution.CompletedAtUtc,
                TimedOut: false,
                DiagnosticArtifactPath: execution.DiagnosticArtifactPath);
        }
    }
}
