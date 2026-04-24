using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinStoredStateResolver(
    ComputerUseWinStateStore stateStore,
    IWindowManager windowManager)
{
    public bool TryResolve(
        string? stateToken,
        AuditInvocationScope invocation,
        string toolName,
        out ComputerUseWinStoredState? state,
        out CallToolResult? failureResult)
    {
        if (string.IsNullOrWhiteSpace(stateToken))
        {
            state = null;
            failureResult = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                toolName,
                ComputerUseWinFailureCodeValues.StateRequired,
                "Сначала вызови get_app_state и передай stateToken.");
            return false;
        }

        if (!stateStore.TryGet(stateToken, out state) || state is null)
        {
            failureResult = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                toolName,
                ComputerUseWinFailureCodeValues.StaleState,
                "stateToken больше не найден; заново вызови get_app_state.");
            return false;
        }

        ComputerUseWinStoredState storedState = state;
        WindowDescriptor expectedWindow = storedState.Window;
        WindowDescriptor? liveWindow = windowManager.ListWindows().SingleOrDefault(item =>
            item.Hwnd == expectedWindow.Hwnd
            && WindowIdentityValidator.MatchesStableIdentity(item, expectedWindow));
        if (liveWindow is null)
        {
            failureResult = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                toolName,
                ComputerUseWinFailureCodeValues.StaleState,
                "Окно из stateToken больше не совпадает с текущим live target.");
            return false;
        }

        state = storedState with { Window = liveWindow };
        failureResult = null;
        return true;
    }
}
