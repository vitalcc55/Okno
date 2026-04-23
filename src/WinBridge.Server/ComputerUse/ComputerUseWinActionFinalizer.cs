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
            CreateActionAuditData(input));
        return CreateToolResult(payload, isError: payload.Status == ComputerUseWinStatusValues.Failed);
    }

    public static CallToolResult FinalizeUnexpectedFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception,
        InputResult? factualFailure = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(exception);

        return factualFailure is null
            ? FinalizeUnexpectedInternalFailure(invocation, toolName, targetHwnd, elementIndex, exception)
            : FinalizeUnexpectedFactualFailure(invocation, toolName, targetHwnd, elementIndex, exception, factualFailure);
    }

    private static ComputerUseWinActionResult CreatePayload(
        long? targetHwnd,
        int? elementIndex,
        InputResult input) =>
        new(
            Status: string.Equals(input.Status, InputStatusValues.VerifyNeeded, StringComparison.Ordinal)
                ? ComputerUseWinStatusValues.VerifyNeeded
                : string.Equals(input.Status, InputStatusValues.Done, StringComparison.Ordinal)
                    ? ComputerUseWinStatusValues.Done
                    : ComputerUseWinStatusValues.Failed,
            RefreshStateRecommended: true,
            FailureCode: ComputerUseWinFailureCodeMapper.ToPublicFailureCode(input.FailureCode),
            Reason: input.Reason,
            TargetHwnd: input.TargetHwnd ?? targetHwnd,
            ElementIndex: elementIndex);

    private static CallToolResult FinalizeUnexpectedInternalFailure(
        AuditInvocationScope invocation,
        string toolName,
        long? targetHwnd,
        int? elementIndex,
        Exception exception)
    {
        ComputerUseWinActionResult payload = new(
            Status: ComputerUseWinStatusValues.Failed,
            RefreshStateRecommended: false,
            FailureCode: null,
            Reason: "Computer Use for Windows столкнулся с unexpected internal failure до подтверждённого action dispatch; можно повторить запрос после устранения причины, refresh через get_app_state не обязателен.",
            TargetHwnd: targetHwnd,
            ElementIndex: elementIndex);

        ComputerUseWinFailureCompletion.CompleteFailure(
            invocation,
            payload.Reason ?? $"Computer Use action '{toolName}' завершился unexpected internal failure.",
            payload.FailureCode,
            payload.TargetHwnd,
            exception,
            bestEffort: true,
            data: CreateUnexpectedFailureAuditData("pre_dispatch_internal"));
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
            data: CreateActionAuditData(factualFailure, "post_dispatch_factual"));
        return CreateToolResult(payload, isError: true);
    }

    private static Dictionary<string, string?> CreateActionAuditData(InputResult input, string? failurePhase = null)
    {
        Dictionary<string, string?> data = new(StringComparer.Ordinal)
        {
            ["status"] = input.Status,
            ["decision"] = input.Decision,
            ["result_mode"] = input.ResultMode,
            ["failure_code"] = input.FailureCode,
            ["public_failure_code"] = ComputerUseWinFailureCodeMapper.ToPublicFailureCode(input.FailureCode),
            ["target_hwnd"] = input.TargetHwnd?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["target_source"] = input.TargetSource,
            ["completed_action_count"] = input.CompletedActionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["failed_action_index"] = input.FailedActionIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["artifact_path"] = input.ArtifactPath,
        };
        if (!string.IsNullOrWhiteSpace(failurePhase))
        {
            data["failure_phase"] = failurePhase;
        }

        return data;
    }

    private static Dictionary<string, string?> CreateUnexpectedFailureAuditData(string failurePhase) =>
        new(StringComparer.Ordinal)
        {
            ["failure_phase"] = failurePhase,
        };
    private static CallToolResult CreateToolResult(ComputerUseWinActionResult payload, bool isError)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, ComputerUseWinTools.PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, ComputerUseWinTools.PayloadJsonOptions),
                },
            ],
        };
    }
}
