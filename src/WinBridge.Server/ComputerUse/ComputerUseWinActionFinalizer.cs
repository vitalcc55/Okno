using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinActionFinalizer
{
    public static CallToolResult FinalizeResult(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        InputResult input)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(input);

        ComputerUseWinActionResult payload = CreatePayload(targetHwnd, elementIndex, input);
        string auditOutcome = payload.Status is ComputerUseWinStatusValues.Done or ComputerUseWinStatusValues.VerifyNeeded
            ? "done"
            : "failed";
        invocation.CompleteBestEffort(
            auditOutcome,
            payload.Reason ?? $"Computer Use action '{toolName}' завершён.",
            payload.TargetHwnd,
            ComputerUseWinAuditDataBuilder.CreateActionCompletionData(input));
        return CreateToolResult(payload, isError: payload.Status == ComputerUseWinStatusValues.Failed);
    }

    public static CallToolResult FinalizeUnexpectedFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        InputResult? factualFailure = null,
        bool preDispatchStateMutationPossible = false)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(exception);

        return factualFailure is null
            ? FinalizeUnexpectedInternalFailure(invocation, toolName, targetHwnd, elementIndex, exception, preDispatchStateMutationPossible)
            : FinalizeUnexpectedFactualFailure(invocation, toolName, targetHwnd, elementIndex, exception, factualFailure);
    }

    private static ComputerUseWinActionResult CreatePayload(
        long? targetHwnd,
        int? elementIndex,
        InputResult input)
    {
        string status = string.Equals(input.Status, InputStatusValues.VerifyNeeded, StringComparison.Ordinal)
            ? ComputerUseWinStatusValues.VerifyNeeded
            : string.Equals(input.Status, InputStatusValues.Done, StringComparison.Ordinal)
                ? ComputerUseWinStatusValues.Done
                : ComputerUseWinStatusValues.Failed;
        ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(input.FailureCode, input.Reason);

        return new(
            Status: status,
            RefreshStateRecommended: true,
            FailureCode: failure.FailureCode,
            Reason: status == ComputerUseWinStatusValues.Failed ? failure.Reason : input.Reason,
            TargetHwnd: input.TargetHwnd ?? targetHwnd,
            ElementIndex: elementIndex);
    }

    private static CallToolResult FinalizeUnexpectedInternalFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        bool preDispatchStateMutationPossible)
    {
        ComputerUseWinActionLifecyclePhase phase = preDispatchStateMutationPossible
            ? ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch
            : ComputerUseWinActionLifecyclePhase.BeforeActivation;
        ComputerUseWinActionResult payload = CreateStructuredFailurePayload(
            ComputerUseWinFailureCodeValues.UnexpectedInternalFailure,
            preDispatchStateMutationPossible
                ? "Computer Use for Windows столкнулся с unexpected internal failure до подтверждённого action dispatch; из-за возможной активации окна перед retry сначала обнови состояние через get_app_state."
                : "Computer Use for Windows столкнулся с unexpected internal failure до подтверждённого action dispatch; можно повторить запрос после устранения причины, refresh через get_app_state не обязателен.",
            targetHwnd,
            elementIndex,
            phase);

        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            payload.Reason ?? $"Computer Use action '{toolName}' завершился unexpected internal failure.",
            payload.FailureCode,
            payload.TargetHwnd,
            exception,
            bestEffort: true,
            data: ComputerUseWinAuditDataBuilder.CreateUnexpectedFailureData(phase));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult FinalizeUnexpectedFactualFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        InputResult factualFailure)
    {
        ComputerUseWinActionResult payload = CreatePayload(targetHwnd, elementIndex, factualFailure);

        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            payload.Reason ?? $"Computer Use action '{toolName}' завершился unexpected failure.",
            payload.FailureCode,
            payload.TargetHwnd,
            exception,
            bestEffort: true,
            data: ComputerUseWinAuditDataBuilder.CreateActionCompletionData(factualFailure, "post_dispatch_factual"));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult CreateToolResult(ComputerUseWinActionResult payload, bool isError)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, ComputerUseWinToolResultFactory.PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, ComputerUseWinToolResultFactory.PayloadJsonOptions),
                },
            ],
        };
    }

    private static bool ShouldRecommendRefresh(
        string? failureCode,
        ComputerUseWinActionLifecyclePhase phase)
    {
        if (phase is ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch
            or ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch
            or ComputerUseWinActionLifecyclePhase.PostDispatch)
        {
            return true;
        }

        string? publicFailureCode = ComputerUseWinFailureCodeMapper.ToPublicFailureCode(failureCode);
        return publicFailureCode is ComputerUseWinFailureCodeValues.StateRequired
            or ComputerUseWinFailureCodeValues.StaleState
            or ComputerUseWinFailureCodeValues.CaptureReferenceRequired;
    }

    internal static ComputerUseWinActionResult CreateStructuredFailurePayload(
        string failureCode,
        string reason,
        long? targetHwnd,
        int? elementIndex,
        ComputerUseWinActionLifecyclePhase phase) =>
        new(
            Status: ComputerUseWinStatusValues.Failed,
            RefreshStateRecommended: ShouldRecommendRefresh(failureCode, phase),
            FailureCode: failureCode,
            Reason: reason,
            TargetHwnd: targetHwnd,
            ElementIndex: elementIndex);

    internal static ComputerUseWinActionResult CreateStructuredApprovalRequiredPayload(
        string reason,
        long? targetHwnd,
        int? elementIndex,
        ComputerUseWinActionLifecyclePhase phase) =>
        new(
            Status: ComputerUseWinStatusValues.ApprovalRequired,
            RefreshStateRecommended: phase is not ComputerUseWinActionLifecyclePhase.BeforeActivation,
            FailureCode: ComputerUseWinFailureCodeValues.ApprovalRequired,
            Reason: reason,
            TargetHwnd: targetHwnd,
            ElementIndex: elementIndex);
}
