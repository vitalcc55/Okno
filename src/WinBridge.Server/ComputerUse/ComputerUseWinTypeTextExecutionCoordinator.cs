using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinTypeTextExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IUiAutomationService uiAutomationService,
    IInputService inputService)
{
    private const string DispatchPath = "win32_sendinput_unicode";
    private const string RiskClass = "text_input";

    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinTypeTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ComputerUseWinTypeTextContract.TryParse(request, out ComputerUseWinTypeTextPayload? payload, out string? failure))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    failure ?? "Запрос type_text не прошёл contract validation."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        if (!TryResolveStoredTarget(state, request, out ComputerUseWinStoredElement? storedTarget, out string? storedTargetFailureCode, out string? storedTargetReason))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    storedTargetFailureCode ?? ComputerUseWinFailureCodeValues.UnsupportedAction,
                    storedTargetReason ?? "type_text требует доказанный focused editable target."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: RiskClass,
                dispatchPath: null);
        }

        ComputerUseWinTypeTextPayload parsedPayload = payload!;
        ComputerUseWinStoredElement resolvedStoredTarget = storedTarget!;

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, ActivateWindowStatusValues.Done, StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: false,
                riskClass: RiskClass,
                dispatchPath: DispatchPath);
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinFailureDetails? revalidationFailure = await RevalidateFocusedTargetAsync(
            resolvedState,
            resolvedStoredTarget,
            cancellationToken).ConfigureAwait(false);
        if (revalidationFailure is not null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                revalidationFailure,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: RiskClass,
                dispatchPath: DispatchPath);
        }

        InputResult input = await ExecuteInputAsync(resolvedState, parsedPayload.Text, cancellationToken).ConfigureAwait(false);
        if (NeedsActivationRetry(input))
        {
            ActivateWindowResult retryActivation = await windowActivationService.ActivateAsync(resolvedState.Window, cancellationToken).ConfigureAwait(false);
            if (string.Equals(retryActivation.Status, ActivateWindowStatusValues.Done, StringComparison.Ordinal)
                || string.Equals(retryActivation.Status, "already_active", StringComparison.Ordinal))
            {
                resolvedState = resolvedState with
                {
                    Window = retryActivation.Window ?? resolvedState.Window,
                };

                revalidationFailure = await RevalidateFocusedTargetAsync(
                    resolvedState,
                    resolvedStoredTarget,
                    cancellationToken).ConfigureAwait(false);
                if (revalidationFailure is not null)
                {
                    return ComputerUseWinActionExecutionOutcome.Failure(
                        revalidationFailure,
                        ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                        confirmationRequired: false,
                        riskClass: RiskClass,
                        dispatchPath: DispatchPath);
                }

                input = await ExecuteInputAsync(resolvedState, parsedPayload.Text, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            input,
            confirmationRequired: false,
            riskClass: RiskClass,
            dispatchPath: DispatchPath);
    }

    private async Task<ComputerUseWinFailureDetails?> RevalidateFocusedTargetAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinStoredElement storedTarget,
        CancellationToken cancellationToken)
    {
        UiaSnapshotResult snapshot;
        try
        {
            snapshot = await uiAutomationService.SnapshotAsync(
                state.Window,
                new UiaSnapshotRequest
                {
                    Depth = state.Observation.RequestedDepth,
                    MaxNodes = state.Observation.RequestedMaxNodes,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                "Computer Use for Windows не смог пере-подтвердить focused editable target для type_text.");
        }

        if (!string.Equals(snapshot.Status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal)
            || snapshot.Root is null)
        {
            return ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить focused editable target для type_text.");
        }

        IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
        if (!ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedTarget, out ComputerUseWinStoredElement? freshElement)
            || freshElement is null)
        {
            return ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.StaleState,
                "Focused editable element из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.");
        }

        if (!ComputerUseWinActionability.IsTypeTextActionable(freshElement))
        {
            return ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.StaleState,
                "Focused editable proof из stateToken устарел; сначала заново получи get_app_state после click/focus.");
        }

        return null;
    }

    private Task<InputResult> ExecuteInputAsync(
        ComputerUseWinStoredState state,
        string text,
        CancellationToken cancellationToken) =>
        inputService.ExecuteAsync(
            new InputRequest
            {
                Hwnd = state.Session.Hwnd,
                Actions =
                [
                    new InputAction
                    {
                        Type = InputActionTypeValues.Type,
                        Text = text,
                    },
                ],
            },
            new InputExecutionContext(state.Window),
            InputExecutionProfileValues.ComputerUseCore,
            cancellationToken);

    private static bool TryResolveStoredTarget(
        ComputerUseWinStoredState state,
        ComputerUseWinTypeTextRequest request,
        out ComputerUseWinStoredElement? storedTarget,
        out string? failureCode,
        out string? reason)
    {
        storedTarget = null;

        if (request.ElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out storedTarget))
            {
                failureCode = ComputerUseWinFailureCodeValues.InvalidRequest;
                reason = $"elementIndex {elementIndex} не существует в последнем get_app_state.";
                return false;
            }

            if (!ComputerUseWinActionability.IsTypeTextActionable(storedTarget))
            {
                failureCode = ComputerUseWinFailureCodeValues.UnsupportedAction;
                reason = "type_text требует elementIndex, который уже опубликован как focused editable target; сначала переведи focus через click и заново вызови get_app_state.";
                return false;
            }

            failureCode = null;
            reason = null;
            return true;
        }

        ComputerUseWinStoredElement[] focusedEditableTargets = state.Elements.Values
            .Where(ComputerUseWinActionability.IsTypeTextActionable)
            .ToArray();
        if (focusedEditableTargets.Length != 1)
        {
            failureCode = ComputerUseWinFailureCodeValues.UnsupportedAction;
            reason = "type_text без elementIndex требует ровно один доказанный focused editable target в последнем get_app_state.";
            return false;
        }

        storedTarget = focusedEditableTargets[0];
        failureCode = null;
        reason = null;
        return true;
    }

    private static bool NeedsActivationRetry(InputResult input) =>
        string.Equals(input.Status, InputStatusValues.Failed, StringComparison.Ordinal)
        && (string.Equals(input.FailureCode, InputFailureCodeValues.TargetNotForeground, StringComparison.Ordinal)
            || string.Equals(input.FailureCode, InputFailureCodeValues.TargetPreflightFailed, StringComparison.Ordinal));
}
