// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class ProcessIsolatedUiAutomationBackend : IUiaSnapshotBackend
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IUiAutomationWorkerProcessRunner _workerRunner;

    public ProcessIsolatedUiAutomationBackend(TimeProvider timeProvider, AuditLogOptions auditLogOptions)
        : this(new UiAutomationWorkerProcessRunner(timeProvider, auditLogOptions))
    {
    }

    internal ProcessIsolatedUiAutomationBackend(
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

    internal ProcessIsolatedUiAutomationBackend(
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

    internal ProcessIsolatedUiAutomationBackend(IUiAutomationWorkerProcessRunner workerRunner)
    {
        _workerRunner = workerRunner;
    }

    public async Task<UiaSnapshotBackendResult> CaptureAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        UiAutomationWorkerProcessResult execution = await _workerRunner
            .ExecuteAsync(
                new UiAutomationWorkerInvocation(
                    UiAutomationWorkerOperationValues.Snapshot,
                    targetWindow,
                    SnapshotRequest: request,
                    WaitProbeRequest: null),
                targetWindow.Hwnd,
                timeout: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.Success)
        {
            return Failed(
                execution.Reason!,
                execution.FailureStage!,
                execution.CapturedAtUtc,
                execution.DiagnosticArtifactPath);
        }

        try
        {
            UiaSnapshotBackendResult? result = JsonSerializer.Deserialize<UiaSnapshotBackendResult>(execution.Stdout ?? string.Empty, JsonOptions);
            return result ?? Failed(
                "UIA worker process вернул пустой result payload.",
                UiaSnapshotFailureStageValues.WorkerProcess,
                execution.CapturedAtUtc,
                execution.DiagnosticArtifactPath);
        }
        catch (JsonException)
        {
            return Failed(
                "UIA worker process вернул некорректный result payload.",
                UiaSnapshotFailureStageValues.WorkerProcess,
                execution.CapturedAtUtc,
                execution.DiagnosticArtifactPath);
        }
    }

    internal static ProcessStartInfo CreateWorkerStartInfo(UiaWorkerLaunchSpec workerLaunchSpec) =>
        UiAutomationWorkerProcessRunner.CreateWorkerStartInfo(workerLaunchSpec);

    internal static UiaWorkerLaunchSpec ResolveWorkerLaunchSpec()
    {
        return UiAutomationWorkerProcessRunner.ResolveWorkerLaunchSpec();
    }

    internal static UiaWorkerLaunchSpec ResolveWorkerLaunchSpec(string baseDirectory, string? currentHostPath)
    {
        return UiAutomationWorkerProcessRunner.ResolveWorkerLaunchSpec(baseDirectory, currentHostPath);
    }

    private static UiaSnapshotBackendResult Failed(
        string reason,
        string failureStage,
        DateTimeOffset capturedAtUtc,
        string? diagnosticArtifactPath) =>
        new(
            Success: false,
            Reason: reason,
            FailureStage: failureStage,
            CapturedAtUtc: capturedAtUtc,
            Root: null,
            RealizedDepth: 0,
            NodeCount: 0,
            Truncated: false,
            DepthBoundaryReached: false,
            NodeBudgetBoundaryReached: false,
            DiagnosticArtifactPath: diagnosticArtifactPath);
}

internal sealed record UiaWorkerLaunchSpec(string FileName, string Arguments);
