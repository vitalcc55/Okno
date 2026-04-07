using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Waiting;

public sealed class WaitResultMaterializer(
    AuditLog auditLog,
    AuditLogOptions auditLogOptions,
    WaitOptions options)
{
    private const string RuntimeCompletedEventName = "wait.runtime.completed";
    private readonly WaitArtifactWriter _artifactWriter = new(auditLogOptions);

    internal WaitResult Materialize(
        WaitRequest request,
        WaitTargetResolution target,
        IReadOnlyList<WaitAttemptSummary> attempts,
        DateTimeOffset startedAtUtc,
        WaitResult result,
        string? failureStage = null,
        Exception? failureException = null)
    {
        WaitFailureDiagnostics? failureDiagnostics = CreateFailureDiagnostics(failureStage, failureException);
        try
        {
            string artifactPath = _artifactWriter.Write(request, target, options, attempts, result, startedAtUtc, failureDiagnostics);
            WaitResult materialized = result with { ArtifactPath = artifactPath };
            RecordRuntimeEvent(materialized, failureDiagnostics);
            return materialized;
        }
        catch (WaitArtifactException exception)
        {
            WaitResult artifactFailure = result with
            {
                Status = WaitStatusValues.Failed,
                Reason = exception.Message,
                ArtifactPath = null,
            };
            RecordRuntimeEvent(artifactFailure, CreateFailureDiagnostics("artifact_write", exception.InnerException ?? exception));
            return artifactFailure;
        }
    }

    public WaitResult MaterializeTerminalFailure(
        WaitRequest request,
        WaitTargetResolution target,
        DateTimeOffset startedAtUtc,
        WaitResult result,
        string? failureStage = null,
        Exception? failureException = null) =>
        Materialize(
            request,
            target,
            Array.Empty<WaitAttemptSummary>(),
            startedAtUtc,
            result,
            failureStage,
            failureException);

    private void RecordRuntimeEvent(WaitResult result, WaitFailureDiagnostics? failureDiagnostics)
    {
        string severity = result.Status == WaitStatusValues.Done ? "info" : "warning";
        string message = result.Status == WaitStatusValues.Done
            ? "Runtime wait condition подтверждено."
            : result.Reason ?? "Runtime wait завершился без подтверждения condition.";

        auditLog.TryRecordRuntimeEvent(
            eventName: RuntimeCompletedEventName,
            severity: severity,
            messageHuman: message,
            toolName: "windows.wait",
            outcome: result.Status,
            windowHwnd: result.Window?.Hwnd,
            data: new Dictionary<string, string?>
            {
                ["condition"] = result.Condition,
                ["target_source"] = result.TargetSource,
                ["target_failure_code"] = result.TargetFailureCode,
                ["attempt_count"] = result.AttemptCount.ToString(CultureInfo.InvariantCulture),
                ["elapsed_ms"] = result.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                ["artifact_path"] = result.ArtifactPath,
                ["failure_stage"] = failureDiagnostics?.FailureStage,
                ["exception_type"] = failureDiagnostics?.ExceptionType,
                ["exception_message"] = failureDiagnostics?.ExceptionMessage,
                ["matched_element_id"] = result.MatchedElement?.ElementId,
                ["matched_text_source"] = result.LastObserved?.MatchedTextSource,
                ["diagnostic_artifact_path"] = result.LastObserved?.DiagnosticArtifactPath,
                ["visual_evidence_status"] = result.LastObserved?.VisualEvidenceStatus,
                ["visual_baseline_artifact_path"] = result.LastObserved?.VisualBaselineArtifactPath,
                ["visual_current_artifact_path"] = result.LastObserved?.VisualCurrentArtifactPath,
            });
    }

    private static WaitFailureDiagnostics? CreateFailureDiagnostics(string? failureStage, Exception? failureException)
    {
        if (string.IsNullOrWhiteSpace(failureStage) && failureException is null)
        {
            return null;
        }

        return new WaitFailureDiagnostics(
            FailureStage: failureStage,
            ExceptionType: failureException?.GetType().FullName,
            ExceptionMessage: failureException?.Message);
    }
}
