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

    public static CallToolResult CreateStateApprovalRequired(AuditInvocationScope invocation, WindowDescriptor window, string appId, string windowId)
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
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation) =>
        CreateActionFailure(invocation, toolName, failure.FailureCode, failure.Reason, targetHwnd, elementIndex, failure.AuditException, phase);

    public static CallToolResult CreateActionFailure(
        AuditInvocationScope invocation,
        string toolName,
        string failureCode,
        string reason,
        long? targetHwnd = null,
        int? elementIndex = null,
        Exception? auditException = null,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation)
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
            data: ComputerUseWinActionFinalizer.CreateStructuredPhaseAuditData(phase));
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateActionApprovalRequired(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        string reason,
        ComputerUseWinActionLifecyclePhase phase = ComputerUseWinActionLifecyclePhase.BeforeActivation)
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
            data: ComputerUseWinActionFinalizer.CreateStructuredPhaseAuditData(phase));
        return CreateToolResult(payload, isError: true);
    }

    public static CallToolResult CreateActionToolResult(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        InputResult input) =>
        ComputerUseWinActionFinalizer.FinalizeResult(invocation, toolName, targetHwnd, elementIndex, input);
}
