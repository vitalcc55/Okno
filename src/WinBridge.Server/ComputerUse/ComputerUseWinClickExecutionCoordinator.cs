using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinClickExecutionCoordinator(
    IWindowActivationService windowActivationService,
    ComputerUseWinClickTargetResolver clickTargetResolver,
    IInputService inputService)
{
    public async Task<ComputerUseWinClickExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (TryClassifyBeforeActivation(state, request, out ComputerUseWinClickExecutionOutcome? preActivationOutcome))
        {
            return preActivationOutcome!;
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, "done", StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinClickExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.BlockedTarget,
                    activation.Reason ?? "Computer Use for Windows не смог подготовить окно к клику."));
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinClickTargetResolution resolution = await clickTargetResolver.ResolveAsync(resolvedState, request, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return ComputerUseWinClickExecutionOutcome.Failure(resolution.FailureDetails!);
        }

        if (resolution.RequiresConfirmation && !request.Confirm)
        {
            return ComputerUseWinClickExecutionOutcome.ApprovalRequired(
                request.ElementIndex is null
                    ? CoordinateApprovalReason
                    : "Клик по выбранному элементу требует явного подтверждения.");
        }

        InputResult input = await ExecuteInputAsync(resolvedState, resolution.Action!, cancellationToken).ConfigureAwait(false);
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

                InputAction retryAction = resolution.Action!;
                if (request.ElementIndex is not null)
                {
                    ComputerUseWinClickTargetResolution retryResolution = await clickTargetResolver.ResolveAsync(resolvedState, request, cancellationToken).ConfigureAwait(false);
                    if (!retryResolution.IsSuccess)
                    {
                        return ComputerUseWinClickExecutionOutcome.Failure(retryResolution.FailureDetails!);
                    }

                    if (retryResolution.RequiresConfirmation && !request.Confirm)
                    {
                        return ComputerUseWinClickExecutionOutcome.ApprovalRequired("Клик по выбранному элементу требует явного подтверждения.");
                    }

                    retryAction = retryResolution.Action!;
                }

                input = await ExecuteInputAsync(resolvedState, retryAction, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinClickExecutionOutcome.Success(input);
    }

    private static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        out ComputerUseWinClickExecutionOutcome? outcome)
    {
        if (request.ElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || storedElement.Bounds is null)
            {
                outcome = ComputerUseWinClickExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.InvalidRequest,
                        $"elementIndex {elementIndex} не существует или не даёт кликабельных bounds."));
                return true;
            }

            if (!request.Confirm
                && ComputerUseWinTargetPolicy.RequiresRiskConfirmation(storedElement, ToolNames.ComputerUseWinClick))
            {
                outcome = ComputerUseWinClickExecutionOutcome.ApprovalRequired("Клик по выбранному элементу требует явного подтверждения.");
                return true;
            }

            outcome = null;
            return false;
        }

        if (request.Point is not null)
        {
            if (!request.Confirm)
            {
                outcome = ComputerUseWinClickExecutionOutcome.ApprovalRequired(CoordinateApprovalReason);
                return true;
            }

            outcome = null;
            return false;
        }

        outcome = ComputerUseWinClickExecutionOutcome.Failure(
            ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                "Для click требуется elementIndex или point."));
        return true;
    }

    private Task<InputResult> ExecuteInputAsync(
        ComputerUseWinStoredState state,
        InputAction action,
        CancellationToken cancellationToken) =>
        inputService.ExecuteAsync(
            new InputRequest
            {
                Hwnd = state.Session.Hwnd,
                Actions = [action],
            },
            new InputExecutionContext(state.Window),
            InputExecutionProfileValues.ComputerUseCore,
            cancellationToken);

    private static bool NeedsActivationRetry(InputResult input) =>
        string.Equals(input.Status, InputStatusValues.Failed, StringComparison.Ordinal)
        && (string.Equals(input.FailureCode, InputFailureCodeValues.TargetNotForeground, StringComparison.Ordinal)
            || string.Equals(input.FailureCode, InputFailureCodeValues.TargetPreflightFailed, StringComparison.Ordinal));

    private const string CoordinateApprovalReason =
        "Coordinate click требует явного подтверждения, потому что target не доказан через semantic element из последнего get_app_state.";
}

internal sealed record ComputerUseWinClickExecutionOutcome(
    InputResult? Input,
    ComputerUseWinFailureDetails? FailureDetails,
    string? ApprovalReason)
{
    public bool IsSuccess => Input is not null;

    public bool IsApprovalRequired => ApprovalReason is not null;

    public static ComputerUseWinClickExecutionOutcome Success(InputResult input) =>
        new(input, null, null);

    public static ComputerUseWinClickExecutionOutcome Failure(ComputerUseWinFailureDetails failure) =>
        new(null, failure, null);

    public static ComputerUseWinClickExecutionOutcome ApprovalRequired(string reason) =>
        new(null, null, reason);
}
