using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinClickHandler(
    ComputerUseWinStoredStateResolver storedStateResolver,
    ComputerUseWinClickExecutionCoordinator clickExecutionCoordinator)
{
    public async Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinClickRequest request,
        CancellationToken cancellationToken)
    {
        if (!storedStateResolver.TryResolve(
                request.StateToken,
                invocation,
                ToolNames.ComputerUseWinClick,
                out ComputerUseWinActionReadyState? state,
                out CallToolResult? failureResult))
        {
            return failureResult!;
        }

        ComputerUseWinActionReadyState actionReadyState = state!;
        ComputerUseWinStoredState resolvedState = actionReadyState.StoredState;

        try
        {
            ComputerUseWinClickExecutionOutcome outcome = await clickExecutionCoordinator.ExecuteAsync(
                resolvedState,
                request,
                cancellationToken).ConfigureAwait(false);

            if (outcome.IsApprovalRequired)
            {
                return ComputerUseWinToolResultFactory.CreateActionApprovalRequired(
                    invocation,
                    ToolNames.ComputerUseWinClick,
                    resolvedState.Session.Hwnd,
                    request.ElementIndex,
                    outcome.ApprovalReason!,
                    outcome.Phase);
            }

            if (!outcome.IsSuccess)
            {
                return ComputerUseWinToolResultFactory.CreateActionFailure(
                    invocation,
                    ToolNames.ComputerUseWinClick,
                    outcome.FailureDetails!,
                    resolvedState.Session.Hwnd,
                    request.ElementIndex,
                    outcome.Phase);
            }

            return ComputerUseWinToolResultFactory.CreateActionToolResult(
                invocation,
                ToolNames.ComputerUseWinClick,
                resolvedState.Session.Hwnd,
                request.ElementIndex,
                outcome.Input!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InputExecutionFailureException exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                resolvedState.Session.Hwnd,
                request.ElementIndex,
                exception.InnerException ?? exception,
                exception.Result);
        }
        catch (Exception exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                ToolNames.ComputerUseWinClick,
                resolvedState.Session.Hwnd,
                request.ElementIndex,
                exception,
                preDispatchStateMutationPossible: true);
        }
    }
}
