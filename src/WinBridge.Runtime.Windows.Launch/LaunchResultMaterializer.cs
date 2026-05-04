// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class LaunchResultMaterializer
{
    private const string RuntimeCompletedEventName = "launch.runtime.completed";

    private readonly LaunchArtifactWriter _artifactWriter;
    private readonly AuditLog _auditLog;
    private readonly TimeProvider _timeProvider;

    public LaunchResultMaterializer(
        AuditLog auditLog,
        AuditLogOptions auditLogOptions,
        TimeProvider timeProvider)
        : this(new LaunchArtifactWriter(auditLogOptions), auditLog, timeProvider)
    {
    }

    internal LaunchResultMaterializer(
        LaunchArtifactWriter artifactWriter,
        AuditLog auditLog,
        TimeProvider timeProvider)
    {
        _artifactWriter = artifactWriter;
        _auditLog = auditLog;
        _timeProvider = timeProvider;
    }

    public LaunchProcessResult Materialize(
        LaunchProcessResult result,
        LaunchProcessPreview? requestPreview = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        DateTimeOffset capturedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            string artifactPath = _artifactWriter.Write(
                result,
                capturedAtUtc,
                failureDiagnostics: null,
                requestPreview: requestPreview);
            LaunchProcessResult materialized = result with { ArtifactPath = artifactPath };
            TryRecordRuntimeEvent(materialized);
            return materialized;
        }
        catch (LaunchArtifactException exception)
        {
            LaunchFailureDiagnostics failureDiagnostics = CreateFailureDiagnostics("artifact_write", exception.InnerException ?? exception);
            LaunchProcessResult materialized = result with { ArtifactPath = null };
            TryRecordRuntimeEvent(materialized, failureDiagnostics);
            return materialized;
        }
    }

    private void TryRecordRuntimeEvent(LaunchProcessResult result, LaunchFailureDiagnostics? failureDiagnostics = null)
    {
        string severity = result.Status == LaunchProcessStatusValues.Done && failureDiagnostics is null
            ? "info"
            : "warning";
        string message = failureDiagnostics is null
            ? (result.Status == LaunchProcessStatusValues.Done
                ? "Runtime launch завершён."
                : result.Reason ?? "Runtime launch завершился без подтверждённого результата.")
            : "Runtime launch завершён, но diagnostics artifact не materialized.";

        _auditLog.TryRecordRuntimeEvent(
            eventName: RuntimeCompletedEventName,
            severity: severity,
            messageHuman: message,
            toolName: "windows.launch_process",
            outcome: result.Status,
            windowHwnd: result.MainWindowObserved ? result.MainWindowHandle : null,
            data: new Dictionary<string, string?>
            {
                ["status"] = result.Status,
                ["decision"] = result.Decision,
                ["result_mode"] = result.ResultMode,
                ["failure_code"] = result.FailureCode,
                ["executable_identity"] = result.ExecutableIdentity,
                ["process_id"] = result.ProcessId?.ToString(CultureInfo.InvariantCulture),
                ["started_at_utc"] = result.StartedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
                ["has_exited"] = ToInvariantBoolean(result.HasExited),
                ["exit_code"] = result.ExitCode?.ToString(CultureInfo.InvariantCulture),
                ["main_window_observed"] = ToInvariantBoolean(result.MainWindowObserved),
                ["main_window_handle"] = result.MainWindowHandle?.ToString(CultureInfo.InvariantCulture),
                ["main_window_observation_status"] = result.MainWindowObservationStatus,
                ["artifact_path"] = result.ArtifactPath,
                ["failure_stage"] = failureDiagnostics?.FailureStage,
                ["exception_type"] = failureDiagnostics?.ExceptionType,
            });
    }

    private static LaunchFailureDiagnostics CreateFailureDiagnostics(string failureStage, Exception exception) =>
        new(
            FailureStage: failureStage,
            ExceptionType: exception.GetType().FullName,
            ExceptionMessageSuppressed: true);

    private static string? ToInvariantBoolean(bool? value) =>
        value is null ? null : value.Value ? "true" : "false";
}
