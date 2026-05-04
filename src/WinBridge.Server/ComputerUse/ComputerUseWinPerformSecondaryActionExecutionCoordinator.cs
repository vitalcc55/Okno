// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinPerformSecondaryActionExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IUiAutomationService uiAutomationService,
    IUiAutomationSecondaryActionService secondaryActionService)
{
    private readonly ComputerUseWinSecondaryActionResolver _resolver = new(uiAutomationService);

    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinPerformSecondaryActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? validationFailure = ComputerUseWinPerformSecondaryActionContract.ValidateRequest(request);
        if (validationFailure is not null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    validationFailure),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        int elementIndex = request.ElementIndex!.Value;
        if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
            || !ComputerUseWinActionability.IsPerformSecondaryActionActionable(storedElement))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"elementIndex {elementIndex} не является supported secondary semantic target в последнем get_app_state."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: "secondary_semantic",
                dispatchPath: null);
        }

        bool storedTargetIsRisky = ComputerUseWinTargetPolicy.RequiresRiskConfirmation(storedElement, ToolNames.ComputerUseWinPerformSecondaryAction);
        if (storedTargetIsRisky && !request.Confirm)
        {
            return ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                "Выбранный secondary semantic target требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: "secondary_semantic_risky",
                dispatchPath: "uia_toggle");
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, ActivateWindowStatusValues.Done, StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: storedTargetIsRisky,
                riskClass: storedTargetIsRisky ? "secondary_semantic_risky" : "secondary_semantic",
                dispatchPath: "uia_toggle");
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinSecondaryActionResolution resolution = await _resolver.ResolveAsync(
            resolvedState,
            request,
            cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                resolution.FailureDetails!,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: storedTargetIsRisky,
                riskClass: storedTargetIsRisky ? "secondary_semantic_risky" : "secondary_semantic",
                dispatchPath: null);
        }

        if (resolution.RequiresConfirmation && !request.Confirm)
        {
            return ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                "Выбранный secondary semantic target требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: true,
                riskClass: "secondary_semantic_risky",
                dispatchPath: $"uia_{resolution.ActionKind}");
        }

        UiaSecondaryActionResult semanticAction;
        try
        {
            semanticAction = await secondaryActionService.ExecuteAsync(
                resolvedState.Window,
                new UiaSecondaryActionRequest(
                    resolution.EffectiveElement!.ElementId,
                    resolution.ActionKind!),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            semanticAction = UiaSecondaryActionResult.FailureResult(
                resolution.ActionKind!,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message);
        }

        if (!semanticAction.Success)
        {
            return ComputerUseWinActionExecutionOutcome.Success(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: MapFailureCode(semanticAction.FailureKind),
                    Reason: semanticAction.Reason,
                    TargetHwnd: resolvedState.Window.Hwnd,
                    CompletedActionCount: 0,
                    FailedActionIndex: 0),
                confirmationRequired: resolution.RequiresConfirmation,
                riskClass: resolution.IsRisky ? "secondary_semantic_risky" : "secondary_semantic",
                dispatchPath: semanticAction.ResolvedPattern is null ? $"uia_{resolution.ActionKind}" : $"uia_{semanticAction.ResolvedPattern}");
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done,
                ResultMode: InputResultModeValues.PostconditionVerified,
                TargetHwnd: resolvedState.Window.Hwnd,
                CompletedActionCount: 1),
            confirmationRequired: resolution.RequiresConfirmation,
            riskClass: resolution.IsRisky ? "secondary_semantic_risky" : "secondary_semantic",
            dispatchPath: semanticAction.ResolvedPattern is null ? $"uia_{resolution.ActionKind}" : $"uia_{semanticAction.ResolvedPattern}");
    }

    private static string MapFailureCode(string? failureKind) =>
        failureKind switch
        {
            UiaSecondaryActionFailureKindValues.MissingElement => ComputerUseWinFailureCodeValues.StaleState,
            UiaSecondaryActionFailureKindValues.UnsupportedPattern => ComputerUseWinFailureCodeValues.UnsupportedAction,
            UiaSecondaryActionFailureKindValues.NoStateChange => ComputerUseWinFailureCodeValues.InputDispatchFailed,
            _ => ComputerUseWinFailureCodeValues.InputDispatchFailed,
        };
}
