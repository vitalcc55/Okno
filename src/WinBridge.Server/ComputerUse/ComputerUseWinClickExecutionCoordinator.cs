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

        if (ComputerUseWinClickContract.TryClassifyBeforeActivation(state, request, out ComputerUseWinClickExecutionOutcome? preActivationOutcome))
        {
            return preActivationOutcome!;
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, "done", StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinClickExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch);
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinClickTargetResolution resolution = await clickTargetResolver.ResolveAsync(resolvedState, request, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return ComputerUseWinClickExecutionOutcome.Failure(
                resolution.FailureDetails!,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);
        }

        if (resolution.RequiresConfirmation && !request.Confirm)
        {
            return ComputerUseWinClickExecutionOutcome.ApprovalRequired(
                request.ElementIndex is null
                    ? ComputerUseWinClickContract.CoordinateApprovalReason
                    : "Клик по выбранному элементу требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);
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
                        return ComputerUseWinClickExecutionOutcome.Failure(
                            retryResolution.FailureDetails!,
                            ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);
                    }

                    if (retryResolution.RequiresConfirmation && !request.Confirm)
                    {
                        return ComputerUseWinClickExecutionOutcome.ApprovalRequired(
                            "Клик по выбранному элементу требует явного подтверждения.",
                            ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch);
                    }

                    retryAction = retryResolution.Action!;
                }

                input = await ExecuteInputAsync(resolvedState, retryAction, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinClickExecutionOutcome.Success(input);
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
}

internal static class ComputerUseWinActivationFailureMapper
{
    public static ComputerUseWinFailureDetails Map(ActivateWindowResult activation)
    {
        string failureCode = ClassifyFailure(activation);
        ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(
            failureCode,
            activation.Reason);

        return ComputerUseWinFailureDetails.Expected(
            failure.FailureCode ?? ComputerUseWinFailureCodeValues.TargetPreflightFailed,
            failure.Reason ?? "Computer Use for Windows не смог подготовить окно к клику; заново вызови get_app_state перед retry.");
    }

    private static string ClassifyFailure(ActivateWindowResult activation)
        => activation.FailureKind switch
        {
            ActivationFailureKindValues.MissingTarget => ComputerUseWinFailureCodeValues.MissingTarget,
            ActivationFailureKindValues.IdentityChanged => ComputerUseWinFailureCodeValues.StaleState,
            ActivationFailureKindValues.RestoreFailedStillMinimized => ComputerUseWinFailureCodeValues.TargetMinimized,
            ActivationFailureKindValues.ForegroundNotConfirmed => ComputerUseWinFailureCodeValues.TargetNotForeground,
            ActivationFailureKindValues.PreflightFailed => ComputerUseWinFailureCodeValues.TargetPreflightFailed,
            _ => ClassifyUntypedFailure(activation),
        };

    private static string ClassifyUntypedFailure(ActivateWindowResult activation)
    {
        if (activation.Window is null)
        {
            return ComputerUseWinFailureCodeValues.MissingTarget;
        }

        return string.Equals(activation.Window.WindowState, WindowStateValues.Minimized, StringComparison.Ordinal)
            ? ComputerUseWinFailureCodeValues.TargetMinimized
            : !activation.IsForeground
                ? ComputerUseWinFailureCodeValues.TargetNotForeground
                : ComputerUseWinFailureCodeValues.TargetPreflightFailed;
    }
}

internal sealed record ComputerUseWinClickExecutionOutcome(
    InputResult? Input,
    ComputerUseWinFailureDetails? FailureDetails,
    string? ApprovalReason,
    ComputerUseWinActionLifecyclePhase Phase)
{
    public bool IsSuccess => Input is not null;

    public bool IsApprovalRequired => ApprovalReason is not null;

    public static ComputerUseWinClickExecutionOutcome Success(InputResult input) =>
        new(input, null, null, ComputerUseWinActionLifecyclePhase.PostDispatch);

    public static ComputerUseWinClickExecutionOutcome Failure(
        ComputerUseWinFailureDetails failure,
        ComputerUseWinActionLifecyclePhase phase) =>
        new(null, failure, null, phase);

    public static ComputerUseWinClickExecutionOutcome ApprovalRequired(
        string reason,
        ComputerUseWinActionLifecyclePhase phase) =>
        new(null, null, reason, phase);
}
