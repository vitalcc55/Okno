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
    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (ComputerUseWinClickContract.TryClassifyBeforeActivation(state, request, out ComputerUseWinActionExecutionOutcome? preActivationOutcome))
        {
            return preActivationOutcome!;
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, "done", StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinClickTargetResolution resolution = await clickTargetResolver.ResolveAsync(resolvedState, request, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                resolution.FailureDetails!,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                resolution.RequiresConfirmation,
                DetermineRiskClass(request, resolution.RequiresConfirmation),
                DetermineDispatchPath(request));
        }

        if (resolution.RequiresConfirmation && !request.Confirm)
        {
            return ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                request.ElementIndex is null
                    ? ComputerUseWinClickContract.CoordinateApprovalReason
                    : "Клик по выбранному элементу требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                resolution.RequiresConfirmation,
                DetermineRiskClass(request, resolution.RequiresConfirmation),
                DetermineDispatchPath(request));
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
                        return ComputerUseWinActionExecutionOutcome.Failure(
                            retryResolution.FailureDetails!,
                            ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                            retryResolution.RequiresConfirmation,
                            DetermineRiskClass(request, retryResolution.RequiresConfirmation),
                            DetermineDispatchPath(request));
                    }

                    if (retryResolution.RequiresConfirmation && !request.Confirm)
                    {
                        return ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                            "Клик по выбранному элементу требует явного подтверждения.",
                            ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                            retryResolution.RequiresConfirmation,
                            DetermineRiskClass(request, retryResolution.RequiresConfirmation),
                            DetermineDispatchPath(request));
                    }

                    retryAction = retryResolution.Action!;
                }

                input = await ExecuteInputAsync(resolvedState, retryAction, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            input,
            confirmationRequired: resolution.RequiresConfirmation,
            riskClass: DetermineRiskClass(request, resolution.RequiresConfirmation),
            dispatchPath: DetermineDispatchPath(request));
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

    private static string DetermineDispatchPath(ComputerUseWinClickRequest request)
    {
        if (request.ElementIndex is not null)
        {
            return "fresh_uia_revalidation_to_input";
        }

        string coordinateSpace = request.CoordinateSpace ?? InputCoordinateSpaceValues.CapturePixels;
        return string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
            ? "capture_pixels_input"
            : "screen_input";
    }

    private static string DetermineRiskClass(ComputerUseWinClickRequest request, bool confirmationRequired)
    {
        if (request.ElementIndex is null)
        {
            return "coordinate_low_confidence";
        }

        return confirmationRequired ? "semantic_risky" : "semantic_target";
    }
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
            ActivationFailureKindValues.IdentityProofUnavailable => ComputerUseWinFailureCodeValues.IdentityProofUnavailable,
            ActivationFailureKindValues.RestoreFailedStillMinimized => ComputerUseWinFailureCodeValues.TargetMinimized,
            ActivationFailureKindValues.ForegroundNotConfirmed => ComputerUseWinFailureCodeValues.TargetNotForeground,
            ActivationFailureKindValues.PreflightFailed => ComputerUseWinFailureCodeValues.TargetPreflightFailed,
            _ => ComputerUseWinFailureCodeValues.TargetPreflightFailed,
        };
}

internal sealed record ComputerUseWinActionExecutionOutcome(
    InputResult? Input,
    ComputerUseWinFailureDetails? FailureDetails,
    string? ApprovalReason,
    ComputerUseWinActionLifecyclePhase Phase,
    bool ConfirmationRequired,
    string? RiskClass,
    string? DispatchPath,
    bool FallbackUsed = false)
{
    public bool IsSuccess => Input is not null;

    public bool IsApprovalRequired => ApprovalReason is not null;

    public static ComputerUseWinActionExecutionOutcome Success(
        InputResult input,
        bool confirmationRequired,
        string? riskClass,
        string? dispatchPath,
        bool fallbackUsed = false) =>
        new(input, null, null, ComputerUseWinActionLifecyclePhase.PostDispatch, confirmationRequired, riskClass, dispatchPath, fallbackUsed);

    public static ComputerUseWinActionExecutionOutcome Failure(
        ComputerUseWinFailureDetails failure,
        ComputerUseWinActionLifecyclePhase phase,
        bool confirmationRequired,
        string? riskClass,
        string? dispatchPath,
        bool fallbackUsed = false) =>
        new(null, failure, null, phase, confirmationRequired, riskClass, dispatchPath, fallbackUsed);

    public static ComputerUseWinActionExecutionOutcome ApprovalRequired(
        string reason,
        ComputerUseWinActionLifecyclePhase phase,
        bool confirmationRequired,
        string? riskClass,
        string? dispatchPath,
        bool fallbackUsed = false) =>
        new(null, null, reason, phase, confirmationRequired, riskClass, dispatchPath, fallbackUsed);
}
