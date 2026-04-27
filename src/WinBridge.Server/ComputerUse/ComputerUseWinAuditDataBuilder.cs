using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinAuditDataBuilder
{
    public static Dictionary<string, string?> CreateObservedStateCompletionData(
        ComputerUseWinExecutionTarget target,
        ComputerUseWinGetAppStateResult payload)
    {
        Dictionary<string, string?> data = new(StringComparer.Ordinal)
        {
            ["runtime_state"] = ComputerUseWinRuntimeStateKind.Observed.ToString().ToLowerInvariant(),
            ["app_id"] = target.ApprovalKey.Value,
            ["execution_target_id"] = target.ExecutionTargetId.Value,
            ["state_token_present"] = (!string.IsNullOrWhiteSpace(payload.StateToken)).ToString().ToLowerInvariant(),
            ["element_count"] = payload.AccessibilityTree!.Count.ToString(CultureInfo.InvariantCulture),
            ["capture_artifact_path"] = payload.Capture!.ArtifactPath,
        };
        if (!string.IsNullOrWhiteSpace(target.PublicWindowId))
        {
            data["window_id"] = target.PublicWindowId;
        }

        return data;
    }

    public static Dictionary<string, string?> CreateActionCompletionData(InputResult input, string? failurePhase = null)
    {
        ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(input.FailureCode, input.Reason);
        Dictionary<string, string?> data = new(StringComparer.Ordinal)
        {
            ["status"] = input.Status,
            ["decision"] = input.Decision,
            ["result_mode"] = input.ResultMode,
            ["failure_code"] = failure.FailureCode,
            ["public_failure_code"] = failure.FailureCode,
            ["public_reason"] = failure.Reason,
            ["target_hwnd"] = input.TargetHwnd?.ToString(CultureInfo.InvariantCulture),
            ["target_source"] = input.TargetSource,
            ["completed_action_count"] = input.CompletedActionCount.ToString(CultureInfo.InvariantCulture),
            ["failed_action_index"] = input.FailedActionIndex?.ToString(CultureInfo.InvariantCulture),
            ["artifact_path"] = input.ArtifactPath,
        };
        if (!string.IsNullOrWhiteSpace(failurePhase))
        {
            data["failure_phase"] = failurePhase;
        }

        return data;
    }

    public static Dictionary<string, string?> CreateStructuredPhaseData(ComputerUseWinActionLifecyclePhase phase) =>
        new(StringComparer.Ordinal)
        {
            ["failure_phase"] = phase switch
            {
                ComputerUseWinActionLifecyclePhase.BeforeActivation => "pre_dispatch_reject",
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch => "pre_dispatch_after_activation",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch => "after_revalidation_before_dispatch",
                _ => "post_dispatch",
            },
        };

    public static Dictionary<string, string?> CreateUnexpectedFailureData(ComputerUseWinActionLifecyclePhase phase) =>
        new(StringComparer.Ordinal)
        {
            ["failure_phase"] = phase switch
            {
                ComputerUseWinActionLifecyclePhase.BeforeActivation => "pre_dispatch_internal",
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch => "pre_dispatch_after_activation",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch => "after_revalidation_before_dispatch",
                _ => "post_dispatch",
            },
        };
}
