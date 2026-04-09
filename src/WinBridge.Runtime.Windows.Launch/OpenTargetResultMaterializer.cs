using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Windows.Launch;

internal sealed class OpenTargetResultMaterializer
{
    private const string RuntimeCompletedEventName = "open_target.runtime.completed";

    private readonly OpenTargetArtifactWriter _artifactWriter;
    private readonly AuditLog _auditLog;
    private readonly TimeProvider _timeProvider;

    public OpenTargetResultMaterializer(
        AuditLog auditLog,
        AuditLogOptions auditLogOptions,
        TimeProvider timeProvider)
        : this(new OpenTargetArtifactWriter(auditLogOptions), auditLog, timeProvider)
    {
    }

    internal OpenTargetResultMaterializer(
        OpenTargetArtifactWriter artifactWriter,
        AuditLog auditLog,
        TimeProvider timeProvider)
    {
        _artifactWriter = artifactWriter;
        _auditLog = auditLog;
        _timeProvider = timeProvider;
    }

    public OpenTargetResult Materialize(OpenTargetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        DateTimeOffset capturedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            string artifactPath = _artifactWriter.Write(result, capturedAtUtc, failureDiagnostics: null);
            OpenTargetResult materialized = result with { ArtifactPath = artifactPath };
            TryRecordRuntimeEvent(materialized);
            return materialized;
        }
        catch (OpenTargetArtifactException exception)
        {
            OpenTargetFailureDiagnostics failureDiagnostics = CreateFailureDiagnostics("artifact_write", exception.InnerException ?? exception);
            OpenTargetResult materialized = result with { ArtifactPath = null };
            TryRecordRuntimeEvent(materialized, failureDiagnostics);
            return materialized;
        }
    }

    private void TryRecordRuntimeEvent(OpenTargetResult result, OpenTargetFailureDiagnostics? failureDiagnostics = null)
    {
        string severity = result.Status == OpenTargetStatusValues.Done && failureDiagnostics is null
            ? "info"
            : "warning";
        string message = failureDiagnostics is null
            ? (result.Status == OpenTargetStatusValues.Done
                ? "Runtime open_target завершён."
                : result.Reason ?? "Runtime open_target завершился без подтверждённого результата.")
            : "Runtime open_target завершён, но diagnostics artifact не materialized.";

        _auditLog.TryRecordRuntimeEvent(
            eventName: RuntimeCompletedEventName,
            severity: severity,
            messageHuman: message,
            toolName: ToolNames.WindowsOpenTarget,
            outcome: result.Status,
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["status"] = result.Status,
                ["decision"] = result.Decision,
                ["result_mode"] = result.ResultMode,
                ["failure_code"] = result.FailureCode,
                ["target_kind"] = result.TargetKind,
                ["target_identity"] = result.TargetIdentity,
                ["uri_scheme"] = result.UriScheme,
                ["accepted_at_utc"] = result.AcceptedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
                ["handler_process_id"] = result.HandlerProcessId?.ToString(CultureInfo.InvariantCulture),
                ["artifact_path"] = result.ArtifactPath,
                ["failure_stage"] = failureDiagnostics?.FailureStage,
                ["exception_type"] = failureDiagnostics?.ExceptionType,
            });
    }

    private static OpenTargetFailureDiagnostics CreateFailureDiagnostics(string failureStage, Exception exception) =>
        new(
            FailureStage: failureStage,
            ExceptionType: exception.GetType().FullName,
            ExceptionMessageSuppressed: true);
}
