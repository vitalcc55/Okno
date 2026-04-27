using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Session;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinGetAppStateFinalizer
{
    internal static ComputerUseWinGetAppStateResult CreateFailurePayload(string failureCode, string reason) =>
        new(
            Status: ComputerUseWinStatusValues.Failed,
            FailureCode: failureCode,
            Reason: reason);

    internal static ComputerUseWinGetAppStateResult CreateBlockedPayload(string reason) =>
        new(
            Status: ComputerUseWinStatusValues.Blocked,
            FailureCode: ComputerUseWinFailureCodeValues.BlockedTarget,
            Reason: reason);

    internal static ComputerUseWinGetAppStateResult CreateApprovalRequiredPayload(WindowDescriptor window, string appId, string? windowId) =>
        new(
            Status: ComputerUseWinStatusValues.ApprovalRequired,
            Session: new ComputerUseWinAppSession(appId, windowId, window.Hwnd, window.Title, window.ProcessName, window.ProcessId),
            ApprovalRequired: true,
            FailureCode: ComputerUseWinFailureCodeValues.ApprovalRequired,
            Reason: $"App '{appId}' ещё не одобрена для Computer Use for Windows.");

    internal static ComputerUseWinGetAppStateResult CreateIdentityProofFailurePayload(string reason) =>
        new(
            Status: ComputerUseWinStatusValues.Failed,
            FailureCode: ComputerUseWinFailureCodeValues.IdentityProofUnavailable,
            Reason: reason);

    public static CallToolResult FinalizeSuccess(
        AuditInvocationScope invocation,
        ComputerUseWinExecutionTarget target,
        WindowDescriptor selectedWindow,
        ComputerUseWinPreparedAppState preparedState,
        ComputerUseWinStateStore stateStore,
        ISessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ApprovalKey.Value);
        ArgumentNullException.ThrowIfNull(selectedWindow);
        ArgumentNullException.ThrowIfNull(preparedState);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(sessionManager);

        string stateToken = ComputerUseWinStateStore.CreateToken();
        ComputerUseWinGetAppStateResult payload = preparedState.CreatePayload(stateToken);
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, ComputerUseWinToolResultFactory.PayloadJsonOptions);
        CallToolResult result = new()
        {
            IsError = false,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, ComputerUseWinToolResultFactory.PayloadJsonOptions),
                },
                new ImageContentBlock
                {
                    Data = Encoding.ASCII.GetBytes(Convert.ToBase64String(preparedState.PngBytes)),
                    MimeType = preparedState.MimeType,
                },
            ],
        };

        stateStore.Commit(stateToken, preparedState.StoredState);
        sessionManager.Attach(selectedWindow, "computer-use-win");

        invocation.CompleteBestEffort(
            "done",
            "Возвращено актуальное состояние приложения для Computer Use for Windows.",
            selectedWindow.Hwnd,
            ComputerUseWinAuditDataBuilder.CreateObservedStateCompletionData(target, payload));

        return result;
    }
}
