using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinPressKeyExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IInputService inputService)
{
    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinPressKeyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ComputerUseWinPressKeyContract.TryParse(request.Key, out ComputerUseWinPressKeyLiteral? literal, out string? failure))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    failure ?? "Неподдерживаемый key literal для press_key."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        ComputerUseWinPressKeyLiteral parsedLiteral = literal!;
        bool requiresConfirmation = ComputerUseWinPressKeyContract.RequiresConfirmation(parsedLiteral);
        if (requiresConfirmation && !request.Confirm)
        {
            return ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                "Опасная клавиша или shortcut требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: "dangerous_key",
                dispatchPath: "win32_sendinput_keypress");
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, "done", StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: requiresConfirmation,
                riskClass: requiresConfirmation ? "dangerous_key" : "keyboard_key",
                dispatchPath: "win32_sendinput_keypress");
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        InputResult input = await ExecuteInputAsync(resolvedState, parsedLiteral, request.Repeat ?? 1, cancellationToken).ConfigureAwait(false);
        if (NeedsActivationRetry(input))
        {
            ActivateWindowResult retryActivation = await windowActivationService.ActivateAsync(resolvedState.Window, cancellationToken).ConfigureAwait(false);
            if (string.Equals(retryActivation.Status, "done", StringComparison.Ordinal)
                || string.Equals(retryActivation.Status, "already_active", StringComparison.Ordinal))
            {
                resolvedState = resolvedState with
                {
                    Window = retryActivation.Window ?? resolvedState.Window,
                };

                input = await ExecuteInputAsync(resolvedState, parsedLiteral, request.Repeat ?? 1, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            input,
            confirmationRequired: requiresConfirmation,
            riskClass: requiresConfirmation ? "dangerous_key" : "keyboard_key",
            dispatchPath: "win32_sendinput_keypress",
            successorObservationWindow: resolvedState.Window);
    }

    private Task<InputResult> ExecuteInputAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinPressKeyLiteral literal,
        int repeat,
        CancellationToken cancellationToken) =>
        inputService.ExecuteAsync(
            new InputRequest
            {
                Hwnd = state.Session.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Keypress,
                        Key = literal.Normalized,
                        Repeat = repeat,
                    },
                ],
            },
            new InputExecutionContext(state.Window),
            InputExecutionProfileValues.ComputerUseCore,
            cancellationToken);

    private static bool NeedsActivationRetry(InputResult input) =>
        string.Equals(input.Status, InputStatusValues.Failed, StringComparison.Ordinal)
        && (string.Equals(input.FailureCode, InputFailureCodeValues.TargetNotForeground, StringComparison.Ordinal)
            || string.Equals(input.FailureCode, InputFailureCodeValues.TargetPreflightFailed, StringComparison.Ordinal));
}
