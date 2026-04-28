using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinToolResultFactory
{
    internal static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static CallToolResult CreateToolResult<T>(T payload, bool isError)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, PayloadJsonOptions),
                },
            ],
        };
    }

    public static CallToolResult CreateStateFailure(AuditInvocationScope invocation, ComputerUseWinFailureDetails failure, long? targetHwnd = null) =>
        CreateStateFailure(invocation, failure.FailureCode, failure.Reason, targetHwnd, failure.AuditException);

    public static CallToolResult CreateStateFailure(
        AuditInvocationScope invocation,
        string failureCode,
        string reason,
        long? targetHwnd = null,
        Exception? auditException = null)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateFailurePayload(failureCode, reason);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, reason, failureCode, targetHwnd, auditException);
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateStateBlocked(AuditInvocationScope invocation, WindowDescriptor window, string reason)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateBlockedPayload(reason);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, reason, ComputerUseWinFailureCodeValues.BlockedTarget, window.Hwnd);
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateStateApprovalRequired(AuditInvocationScope invocation, WindowDescriptor window, string appId, string? windowId)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateApprovalRequiredPayload(window, appId, windowId);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, payload.Reason!, ComputerUseWinFailureCodeValues.ApprovalRequired, window.Hwnd);
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateStateIdentityProofFailure(AuditInvocationScope invocation, WindowDescriptor window, string reason)
    {
        ComputerUseWinGetAppStateResult payload = ComputerUseWinGetAppStateFinalizer.CreateIdentityProofFailurePayload(reason);
        ComputerUseWinFailureCompletion.CompleteFailure(invocation, reason, ComputerUseWinFailureCodeValues.IdentityProofUnavailable, window.Hwnd);
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateActionFailure(
        AuditInvocationScope invocation,
        string toolName,
        ComputerUseWinFailureDetails failure,
        long? targetHwnd = null,
        int? elementIndex = null,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation,
        ComputerUseWinActionObservabilityContext? observabilityContext = null) =>
        CreateActionFailure(invocation, toolName, failure.FailureCode, failure.Reason, targetHwnd, elementIndex, failure.AuditException, phase, observabilityContext);

    public static CallToolResult CreateActionFailure(
        AuditInvocationScope invocation,
        string toolName,
        string failureCode,
        string reason,
        long? targetHwnd = null,
        int? elementIndex = null,
        Exception? auditException = null,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation,
        ComputerUseWinActionObservabilityContext? observabilityContext = null)
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredFailurePayload(
            failureCode,
            reason,
            targetHwnd,
            elementIndex,
            phase);
        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            reason,
            failureCode,
            targetHwnd,
            auditException,
            data: ComputerUseWinAuditDataBuilder.CreateStructuredPhaseData(phase));
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
                    ComputerUseWinActionLifecyclePhase.BeforeActivation => "pre_dispatch_reject",
                    ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch => "pre_dispatch_after_activation",
                    ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch => "after_revalidation_before_dispatch",
                    _ => "post_dispatch",
                },
                exceptionType: auditException?.GetType().FullName));
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateActionApprovalRequired(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        string reason,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation,
        ComputerUseWinActionObservabilityContext? observabilityContext = null)
    {
        ComputerUseWinActionResult payload = ComputerUseWinActionFinalizer.CreateStructuredApprovalRequiredPayload(
            reason,
            targetHwnd,
            elementIndex,
            phase);
        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            reason,
            ComputerUseWinFailureCodeValues.ApprovalRequired,
            targetHwnd,
            data: ComputerUseWinAuditDataBuilder.CreateStructuredPhaseData(phase));
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
                    ComputerUseWinActionLifecyclePhase.BeforeActivation => "pre_dispatch_reject",
                    ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch => "pre_dispatch_after_activation",
                    ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch => "after_revalidation_before_dispatch",
                    _ => "post_dispatch",
                }));
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateActionToolResult(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        InputResult input,
        ComputerUseWinActionObservabilityContext? observabilityContext = null) =>
        ComputerUseWinActionFinalizer.FinalizeResult(invocation, toolName, targetHwnd, elementIndex, input, observabilityContext);

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
}
