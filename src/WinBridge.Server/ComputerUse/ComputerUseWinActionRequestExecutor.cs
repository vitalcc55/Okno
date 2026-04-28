using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinActionRequestExecutor(
    ComputerUseWinStoredStateResolver storedStateResolver)
{
    public async Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        string toolName,
        string? stateToken,
        int? elementIndex,
        ComputerUseWinStoredStateValidationMode validationMode,
        Func<ComputerUseWinStoredState, CancellationToken, Task<ComputerUseWinActionExecutionOutcome>> execute,
        Func<ComputerUseWinStoredState, ComputerUseWinActionExecutionOutcome, ComputerUseWinActionObservabilityContext>? createObservabilityContext,
        CancellationToken cancellationToken,
        bool preDispatchStateMutationPossible = true)
    {
        if (!storedStateResolver.TryResolve(
                stateToken,
                invocation,
                toolName,
                validationMode,
                out ComputerUseWinActionReadyState? state,
                out CallToolResult? failureResult))
        {
            return failureResult!;
        }

        ComputerUseWinStoredState resolvedState = state!.StoredState;

        try
        {
            ComputerUseWinActionExecutionOutcome outcome = await execute(resolvedState, cancellationToken).ConfigureAwait(false);
            ComputerUseWinActionObservabilityContext? observabilityContext = createObservabilityContext?.Invoke(resolvedState, outcome);

            if (outcome.IsApprovalRequired)
            {
                return ComputerUseWinToolResultFactory.CreateActionApprovalRequired(
                    invocation,
                    toolName,
                    resolvedState.Session.Hwnd,
                    elementIndex,
                    outcome.ApprovalReason!,
                    outcome.Phase,
                    observabilityContext);
            }

            if (!outcome.IsSuccess)
            {
                return ComputerUseWinToolResultFactory.CreateActionFailure(
                    invocation,
                    toolName,
                    outcome.FailureDetails!,
                    resolvedState.Session.Hwnd,
                    elementIndex,
                    outcome.Phase,
                    observabilityContext);
            }

            return ComputerUseWinToolResultFactory.CreateActionToolResult(
                invocation,
                toolName,
                resolvedState.Session.Hwnd,
                elementIndex,
                outcome.Input!,
                observabilityContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InputExecutionFailureException exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                toolName,
                resolvedState.Session.Hwnd,
                elementIndex,
                exception.InnerException ?? exception,
                exception.Result);
        }
        catch (Exception exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                toolName,
                resolvedState.Session.Hwnd,
                elementIndex,
                exception,
                preDispatchStateMutationPossible: preDispatchStateMutationPossible);
        }
    }
}
