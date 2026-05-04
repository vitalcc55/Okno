// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinActionFinalizer
{
    public static CallToolResult FinalizeResult(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        InputResult input,
        ComputerUseWinActionObservabilityContext? observabilityContext = null,
        ComputerUseWinActionSuccessorObservation? successorObservation = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(input);

        ComputerUseWinActionResult payload = CreatePayload(targetHwnd, elementIndex, input, successorObservation);
        string auditOutcome = payload.Status is ComputerUseWinStatusValues.Done or ComputerUseWinStatusValues.VerifyNeeded
            ? "done"
            : "failed";
        invocation.CompleteBestEffort(
            auditOutcome,
            payload.Reason ?? $"Computer Use action '{toolName}' завершён.",
            payload.TargetHwnd,
            ComputerUseWinAuditDataBuilder.CreateActionCompletionData(toolName, input));
        ComputerUseWinActionObservability.RecordBestEffort(invocation, toolName, payload, observabilityContext);
        return CreateToolResult(payload, isError: payload.Status == ComputerUseWinStatusValues.Failed, successorObservation?.ImageContent);
    }

    public static CallToolResult FinalizeUnexpectedFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        InputResult? factualFailure = null,
        bool preDispatchStateMutationPossible = false,
        ComputerUseWinActionObservabilityContext? observabilityContext = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(exception);

        return factualFailure is null
            ? FinalizeUnexpectedInternalFailure(invocation, toolName, targetHwnd, elementIndex, exception, preDispatchStateMutationPossible, observabilityContext)
            : FinalizeUnexpectedFactualFailure(invocation, toolName, targetHwnd, elementIndex, exception, factualFailure, observabilityContext);
    }

    internal static ComputerUseWinActionResult CreatePayload(
        long? targetHwnd,
        int? elementIndex,
        InputResult input,
        ComputerUseWinActionSuccessorObservation? successorObservation = null)
    {
        string status = string.Equals(input.Status, InputStatusValues.VerifyNeeded, StringComparison.Ordinal)
            ? ComputerUseWinStatusValues.VerifyNeeded
            : string.Equals(input.Status, InputStatusValues.Done, StringComparison.Ordinal)
                ? ComputerUseWinStatusValues.Done
                : ComputerUseWinStatusValues.Failed;
        ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(input.FailureCode, input.Reason);
        ComputerUseWinGetAppStateResult? successorState = successorObservation?.SuccessorState;

        return new(
            Status: status,
            RefreshStateRecommended: status == ComputerUseWinStatusValues.Failed || successorState is null,
            FailureCode: failure.FailureCode,
            Reason: status == ComputerUseWinStatusValues.Failed ? failure.Reason : input.Reason,
            TargetHwnd: input.TargetHwnd ?? targetHwnd,
            ElementIndex: elementIndex,
            SuccessorState: successorState,
            SuccessorStateFailure: successorObservation?.Failure);
    }

    private static CallToolResult FinalizeUnexpectedInternalFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        bool preDispatchStateMutationPossible,
        ComputerUseWinActionObservabilityContext? observabilityContext)
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
        ComputerUseWinActionObservability.RecordBestEffort(
            invocation,
            toolName,
            payload,
            MergeObservabilityContext(
                toolName,
                payload,
                observabilityContext,
                failureStage: phase switch
                {
                    ComputerUseWinActionLifecyclePhase.BeforeActivation => "pre_dispatch_internal",
                    ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch => "pre_dispatch_after_activation",
                    ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch => "after_revalidation_before_dispatch",
                    _ => "post_dispatch",
                },
                exceptionType: exception.GetType().FullName));
        return CreateToolResult(payload, isError: true);
    }

    private static CallToolResult FinalizeUnexpectedFactualFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        InputResult factualFailure,
        ComputerUseWinActionObservabilityContext? observabilityContext)
    {
        ComputerUseWinActionResult payload = CreatePayload(targetHwnd, elementIndex, factualFailure);

        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            payload.Reason ?? $"Computer Use action '{toolName}' завершился unexpected failure.",
            payload.FailureCode,
            payload.TargetHwnd,
            exception,
            bestEffort: true,
            data: ComputerUseWinAuditDataBuilder.CreateActionCompletionData(toolName, factualFailure, "post_dispatch_factual"));
        ComputerUseWinActionObservability.RecordBestEffort(
            invocation,
            toolName,
            payload,
            MergeObservabilityContext(
                toolName,
                payload,
                observabilityContext,
                childArtifactPath: string.Equals(toolName, ToolNames.ComputerUseWinDrag, StringComparison.Ordinal)
                    ? null
                    : factualFailure.ArtifactPath,
                failureStage: "post_dispatch_factual",
                exceptionType: exception.GetType().FullName));
        return CreateToolResult(payload, isError: true);
    }

    private static ComputerUseWinActionObservabilityContext? MergeObservabilityContext(
        string toolName,
        ComputerUseWinActionResult payload,
        ComputerUseWinActionObservabilityContext? context,
        string? childArtifactPath = null,
        string? failureStage = null,
        string? exceptionType = null)
    {
        ComputerUseWinActionObservabilityContext effectiveContext = context ?? new(
            ActionName: toolName,
            RuntimeState: "observed",
            AppId: "unknown",
            WindowIdPresent: false,
            StateTokenPresent: false,
            TargetMode: payload.ElementIndex is null ? "unknown" : "element_index",
            ElementIndexPresent: payload.ElementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: false,
            Confirmed: false,
            RiskClass: null,
            DispatchPath: null);

        return effectiveContext with
        {
            ChildArtifactPath = childArtifactPath ?? effectiveContext.ChildArtifactPath,
            FailureStage = failureStage ?? effectiveContext.FailureStage,
            ExceptionType = exceptionType ?? effectiveContext.ExceptionType,
        };
    }

    private static CallToolResult CreateToolResult(
        ComputerUseWinActionResult payload,
        bool isError,
        ImageContentBlock? imageContent = null)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, ComputerUseWinToolResultFactory.PayloadJsonOptions);
        List<ContentBlock> content =
        [
            new TextContentBlock
            {
                Text = JsonSerializer.Serialize(payload, ComputerUseWinToolResultFactory.PayloadJsonOptions),
            },
        ];
        if (imageContent is not null)
        {
            content.Add(imageContent);
        }

        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = structuredContent,
            Content = content,
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
