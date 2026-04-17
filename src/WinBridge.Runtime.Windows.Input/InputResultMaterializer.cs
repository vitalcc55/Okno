using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class InputResultMaterializer
{
    private const string RuntimeCompletedEventName = "input.runtime.completed";
    private const string ToolName = "windows.input";

    private readonly InputArtifactWriter _artifactWriter;
    private readonly AuditLog _auditLog;
    private readonly TimeProvider _timeProvider;

    public InputResultMaterializer(
        AuditLog auditLog,
        AuditLogOptions auditLogOptions,
        TimeProvider timeProvider)
        : this(new InputArtifactWriter(auditLogOptions), auditLog, timeProvider)
    {
    }

    internal InputResultMaterializer(
        InputArtifactWriter artifactWriter,
        AuditLog auditLog,
        TimeProvider timeProvider)
    {
        _artifactWriter = artifactWriter;
        _auditLog = auditLog;
        _timeProvider = timeProvider;
    }

    public InputResult Materialize(
        InputRequest request,
        InputExecutionContext context,
        InputResult result,
        string? failureStage = null,
        Exception? failureException = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        DateTimeOffset capturedAtUtc = _timeProvider.GetUtcNow();
        InputFailureDiagnostics? failureDiagnostics = CreateFailureDiagnostics(failureStage, failureException);

        try
        {
            string artifactPath = _artifactWriter.Write(request, context, result, capturedAtUtc, failureDiagnostics);
            InputResult materialized = result with { ArtifactPath = artifactPath };
            TryRecordRuntimeEvent(request, materialized, failureDiagnostics);
            return materialized;
        }
        catch (InputArtifactException exception)
        {
            InputFailureDiagnostics artifactFailureDiagnostics =
                CreateFailureDiagnostics(InputFailureStageValues.ArtifactWrite, exception.InnerException ?? exception)!;
            InputResult materialized = result with { ArtifactPath = null };
            TryRecordRuntimeEvent(request, materialized, artifactFailureDiagnostics);
            return materialized;
        }
    }

    internal static string ResolveCommittedSideEffectEvidence(InputResult result, string? failureStage)
    {
        if (string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialCompensated, StringComparison.Ordinal))
        {
            return "partial_dispatch_compensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialUncompensated, StringComparison.Ordinal))
        {
            return "partial_dispatch_uncompensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.ClickDispatchCleanFailure, StringComparison.Ordinal))
        {
            return "cursor_move_committed_click_dispatch_clean_failure";
        }

        if (string.Equals(result.Status, InputStatusValues.VerifyNeeded, StringComparison.Ordinal)
            && result.CompletedActionCount > 0)
        {
            return "completed_actions_committed";
        }

        if (result.CompletedActionCount > 0 && result.FailedActionIndex is null)
        {
            return "previous_actions_committed";
        }

        if (TryGetFailedAction(result, out InputActionResult? failedAction)
            && failedAction.ResolvedScreenPoint is not null)
        {
            return string.Equals(failureStage, InputFailureStageValues.CursorMove, StringComparison.Ordinal)
                ? "cursor_move_committed_before_failure"
                : "action_side_effect_committed_before_failure";
        }

        return result.CompletedActionCount > 0
            ? "completed_actions_committed"
            : "no_committed_side_effect_observed";
    }

    private void TryRecordRuntimeEvent(
        InputRequest request,
        InputResult result,
        InputFailureDiagnostics? failureDiagnostics)
    {
        bool isSuccessfulDispatchStatus = result.Status is InputStatusValues.Done or InputStatusValues.VerifyNeeded;
        string severity = isSuccessfulDispatchStatus && failureDiagnostics is null
            ? "info"
            : "warning";
        string message = failureDiagnostics is not null && string.Equals(failureDiagnostics.FailureStage, InputFailureStageValues.ArtifactWrite, StringComparison.Ordinal)
            ? "Runtime windows.input завершён, но diagnostics artifact не materialized."
            : result.Status switch
            {
                InputStatusValues.VerifyNeeded => "Runtime windows.input dispatch завершён; требуется external verification.",
                InputStatusValues.Done => "Runtime windows.input завершён.",
                _ => "Runtime windows.input завершился без подтверждённого результата.",
            };

        IReadOnlyList<InputActionResult> resultActions = result.Actions ?? Array.Empty<InputActionResult>();
        IReadOnlyList<InputAction> requestActions = request.Actions ?? Array.Empty<InputAction>();
        string committedSideEffectEvidence = ResolveCommittedSideEffectEvidence(result, failureDiagnostics?.FailureStage);
        _auditLog.TryRecordRuntimeEvent(
            eventName: RuntimeCompletedEventName,
            severity: severity,
            messageHuman: message,
            toolName: ToolName,
            outcome: result.Status,
            windowHwnd: result.TargetHwnd,
            data: new Dictionary<string, string?>
            {
                ["status"] = result.Status,
                ["decision"] = result.Decision,
                ["result_mode"] = result.ResultMode,
                ["failure_code"] = result.FailureCode,
                ["target_hwnd"] = result.TargetHwnd?.ToString(CultureInfo.InvariantCulture),
                ["target_source"] = result.TargetSource,
                ["completed_action_count"] = result.CompletedActionCount.ToString(CultureInfo.InvariantCulture),
                ["failed_action_index"] = result.FailedActionIndex?.ToString(CultureInfo.InvariantCulture),
                ["action_types"] = JoinDistinct(resultActions.Count > 0
                    ? resultActions.Select(action => action.Type)
                    : requestActions.Select(action => action.Type)),
                ["coordinate_spaces"] = JoinDistinct(resultActions.Count > 0
                    ? resultActions.Select(action => action.CoordinateSpace)
                    : requestActions.Select(action => action.CoordinateSpace)),
                ["artifact_path"] = result.ArtifactPath,
                ["failure_stage"] = failureDiagnostics?.FailureStage,
                ["exception_type"] = failureDiagnostics?.ExceptionType,
                ["committed_side_effect_evidence"] = committedSideEffectEvidence,
            });
    }

    private static InputFailureDiagnostics? CreateFailureDiagnostics(string? failureStage, Exception? exception)
    {
        if (string.IsNullOrWhiteSpace(failureStage) && exception is null)
        {
            return null;
        }

        return new InputFailureDiagnostics(
            FailureStage: failureStage,
            ExceptionType: exception?.GetType().FullName,
            ExceptionMessageSuppressed: exception is not null);
    }

    private static bool TryGetFailedAction(InputResult result, out InputActionResult failedAction)
    {
        failedAction = null!;
        if (result.FailedActionIndex is not int failedIndex || result.Actions is null)
        {
            return false;
        }

        if (failedIndex < 0 || failedIndex >= result.Actions.Count)
        {
            return false;
        }

        failedAction = result.Actions[failedIndex];
        return true;
    }

    private static string? JoinDistinct(IEnumerable<string?> values)
    {
        string[] distinct = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return distinct.Length == 0 ? null : string.Join(",", distinct);
    }
}
