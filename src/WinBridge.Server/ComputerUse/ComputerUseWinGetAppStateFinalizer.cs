using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Session;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinGetAppStateFinalizer
{
    public static CallToolResult FinalizeSuccess(
        AuditInvocationScope invocation,
        string appId,
        WindowDescriptor selectedWindow,
        ComputerUseWinPreparedAppState preparedState,
        ComputerUseWinStateStore stateStore,
        ISessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(selectedWindow);
        ArgumentNullException.ThrowIfNull(preparedState);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(sessionManager);

        string stateToken = ComputerUseWinStateStore.CreateToken();
        ComputerUseWinGetAppStateResult payload = preparedState.CreatePayload(stateToken);
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, ComputerUseWinTools.PayloadJsonOptions);
        CallToolResult result = new()
        {
            IsError = false,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, ComputerUseWinTools.PayloadJsonOptions),
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
            new Dictionary<string, string?>
            {
                ["app_id"] = appId,
                ["state_token"] = payload.StateToken,
                ["element_count"] = payload.AccessibilityTree!.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["capture_artifact_path"] = payload.Capture!.ArtifactPath,
            });

        return result;
    }
}
