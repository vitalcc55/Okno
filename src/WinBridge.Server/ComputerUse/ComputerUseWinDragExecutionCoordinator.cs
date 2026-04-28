using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinDragExecutionCoordinator(
    IWindowActivationService windowActivationService,
    ComputerUseWinDragTargetResolver dragTargetResolver,
    IInputService inputService)
{
    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinDragRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ComputerUseWinDragContract.TryParse(request, out ComputerUseWinDragPayload? payload, out string? failure))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    failure ?? "Запрос drag не прошёл contract validation."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        ComputerUseWinDragPayload typedPayload = payload!;
        if (ComputerUseWinDragContract.TryClassifyBeforeActivation(state, request, typedPayload, out ComputerUseWinActionExecutionOutcome? preActivationOutcome))
        {
            return preActivationOutcome!;
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, ActivateWindowStatusValues.Done, StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: typedPayload.UsesCoordinateEndpoint,
                riskClass: DetermineRiskClass(typedPayload, sourceElement: null, destinationElement: null),
                dispatchPath: ComputerUseWinDragContract.DetermineDispatchPath(typedPayload));
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinDragTargetResolution resolution = await dragTargetResolver.ResolveAsync(
            resolvedState,
            request,
            typedPayload,
            cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                resolution.FailureDetails!,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: typedPayload.UsesCoordinateEndpoint,
                riskClass: DetermineRiskClass(typedPayload, resolution.SourceElement, resolution.DestinationElement),
                dispatchPath: ComputerUseWinDragContract.DetermineDispatchPath(typedPayload));
        }

        bool requiresConfirmation = RequiresConfirmation(typedPayload, resolution.SourceElement, resolution.DestinationElement);
        if (requiresConfirmation && !request.Confirm)
        {
            return ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                typedPayload.UsesCoordinateEndpoint
                    ? "Coordinate drag требует явного подтверждения."
                    : "Drag между выбранными элементами требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: true,
                riskClass: DetermineRiskClass(typedPayload, resolution.SourceElement, resolution.DestinationElement),
                dispatchPath: ComputerUseWinDragContract.DetermineDispatchPath(typedPayload));
        }

        InputResult input = await ExecuteInputAsync(resolvedState, resolution.Action!, cancellationToken).ConfigureAwait(false);
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

                InputAction retryAction = resolution.Action!;
                if (request.FromElementIndex is not null || request.ToElementIndex is not null)
                {
                    ComputerUseWinDragTargetResolution retryResolution = await dragTargetResolver.ResolveAsync(
                        resolvedState,
                        request,
                        typedPayload,
                        cancellationToken).ConfigureAwait(false);
                    if (!retryResolution.IsSuccess)
                    {
                        return ComputerUseWinActionExecutionOutcome.Failure(
                            retryResolution.FailureDetails!,
                            ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                            confirmationRequired: requiresConfirmation,
                            riskClass: DetermineRiskClass(typedPayload, retryResolution.SourceElement, retryResolution.DestinationElement),
                            dispatchPath: ComputerUseWinDragContract.DetermineDispatchPath(typedPayload));
                    }

                    retryAction = retryResolution.Action!;
                }

                input = await ExecuteInputAsync(resolvedState, retryAction, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            input,
            confirmationRequired: requiresConfirmation,
            riskClass: DetermineRiskClass(typedPayload, resolution.SourceElement, resolution.DestinationElement),
            dispatchPath: ComputerUseWinDragContract.DetermineDispatchPath(typedPayload));
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

    private static bool RequiresConfirmation(
        ComputerUseWinDragPayload payload,
        ComputerUseWinStoredElement? sourceElement,
        ComputerUseWinStoredElement? destinationElement)
    {
        if (payload.UsesCoordinateEndpoint)
        {
            return true;
        }

        return ComputerUseWinTargetPolicy.RequiresRiskConfirmation(sourceElement, ToolNames.ComputerUseWinDrag)
            || ComputerUseWinTargetPolicy.RequiresRiskConfirmation(destinationElement, ToolNames.ComputerUseWinDrag);
    }

    private static bool NeedsActivationRetry(InputResult input) =>
        string.Equals(input.Status, InputStatusValues.Failed, StringComparison.Ordinal)
        && (string.Equals(input.FailureCode, InputFailureCodeValues.TargetNotForeground, StringComparison.Ordinal)
            || string.Equals(input.FailureCode, InputFailureCodeValues.TargetPreflightFailed, StringComparison.Ordinal));

    private static string DetermineRiskClass(
        ComputerUseWinDragPayload payload,
        ComputerUseWinStoredElement? sourceElement,
        ComputerUseWinStoredElement? destinationElement)
    {
        if (payload.UsesCoordinateEndpoint)
        {
            return "coordinate_drag";
        }

        return ComputerUseWinTargetPolicy.RequiresRiskConfirmation(sourceElement, ToolNames.ComputerUseWinDrag)
            || ComputerUseWinTargetPolicy.RequiresRiskConfirmation(destinationElement, ToolNames.ComputerUseWinDrag)
            ? "semantic_risky_drag"
            : "semantic_drag";
    }
}
