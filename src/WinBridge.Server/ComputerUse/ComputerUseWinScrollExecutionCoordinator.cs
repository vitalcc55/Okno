// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinScrollExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IUiAutomationService uiAutomationService,
    IUiAutomationScrollService uiAutomationScrollService,
    IInputService inputService)
{
    private readonly ComputerUseWinScrollTargetResolver _targetResolver = new(uiAutomationService);

    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ComputerUseWinScrollContract.TryParse(request, out ComputerUseWinScrollPayload? payload, out string? failure))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    failure ?? "Запрос scroll не прошёл contract validation."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        ComputerUseWinScrollPayload typedPayload = payload!;
        if (ComputerUseWinScrollContract.TryClassifyBeforeActivation(state, request, typedPayload, out ComputerUseWinActionExecutionOutcome? preActivationOutcome))
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
                confirmationRequired: string.Equals(typedPayload.TargetMode, "point", StringComparison.Ordinal),
                riskClass: string.Equals(typedPayload.TargetMode, "point", StringComparison.Ordinal) ? "coordinate_scroll" : "semantic_scroll",
                dispatchPath: string.Equals(typedPayload.TargetMode, "point", StringComparison.Ordinal) ? "win32_sendinput_wheel" : "uia_scroll_pattern");
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinScrollTargetResolution targetResolution = await _targetResolver.ResolveAsync(
            resolvedState,
            request,
            typedPayload,
            cancellationToken).ConfigureAwait(false);
        if (!targetResolution.IsSuccess)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                targetResolution.FailureDetails!,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: targetResolution.UsesPointFallback ? "coordinate_scroll" : "semantic_scroll",
                dispatchPath: null);
        }

        if (!targetResolution.UsesPointFallback)
        {
            UiaScrollResult semanticScroll;
            try
            {
                semanticScroll = await uiAutomationScrollService.ScrollAsync(
                    resolvedState.Window,
                    new UiaScrollRequest(
                        targetResolution.EffectiveElement!.ElementId,
                        typedPayload.Direction,
                        typedPayload.Pages),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException exception)
            {
                semanticScroll = UiaScrollResult.FailureResult(
                    UiaScrollFailureKindValues.DispatchFailed,
                    exception.Message,
                    resolvedPattern: "scroll_pattern");
            }

            if (!semanticScroll.Success)
            {
                return ComputerUseWinActionExecutionOutcome.Success(
                    new InputResult(
                        Status: InputStatusValues.Failed,
                        Decision: InputStatusValues.Failed,
                        FailureCode: MapFailureCode(semanticScroll.FailureKind),
                        Reason: semanticScroll.Reason,
                        TargetHwnd: resolvedState.Window.Hwnd,
                        CompletedActionCount: 0,
                        FailedActionIndex: 0),
                    confirmationRequired: false,
                    riskClass: "semantic_scroll",
                    dispatchPath: semanticScroll.ResolvedPattern is null ? "uia_scroll_pattern" : $"uia_{semanticScroll.ResolvedPattern}",
                    successorObservationWindow: resolvedState.Window);
            }

            return ComputerUseWinActionExecutionOutcome.Success(
                new InputResult(
                    Status: InputStatusValues.Done,
                    Decision: InputStatusValues.Done,
                    ResultMode: InputResultModeValues.PostconditionVerified,
                    TargetHwnd: resolvedState.Window.Hwnd,
                    CompletedActionCount: 1),
                confirmationRequired: false,
                riskClass: "semantic_scroll",
                dispatchPath: semanticScroll.ResolvedPattern is null ? "uia_scroll_pattern" : $"uia_{semanticScroll.ResolvedPattern}",
                successorObservationWindow: resolvedState.Window);
        }

        InputResult input = await inputService.ExecuteAsync(
            new InputRequest
            {
                Hwnd = resolvedState.Session.Hwnd,
                Actions = [targetResolution.InputAction!],
            },
            new InputExecutionContext(resolvedState.Window),
            InputExecutionProfileValues.ComputerUseCore,
            cancellationToken).ConfigureAwait(false);

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

                input = await inputService.ExecuteAsync(
                    new InputRequest
                    {
                        Hwnd = resolvedState.Session.Hwnd,
                        Actions = [targetResolution.InputAction!],
                    },
                    new InputExecutionContext(resolvedState.Window),
                    InputExecutionProfileValues.ComputerUseCore,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            input,
            confirmationRequired: targetResolution.RequiresConfirmation,
            riskClass: "coordinate_scroll",
            dispatchPath: "win32_sendinput_wheel",
            successorObservationWindow: resolvedState.Window);
    }

    private static string MapFailureCode(string? failureKind) =>
        failureKind switch
        {
            UiaScrollFailureKindValues.MissingElement => ComputerUseWinFailureCodeValues.StaleState,
            UiaScrollFailureKindValues.UnsupportedPattern => ComputerUseWinFailureCodeValues.UnsupportedAction,
            UiaScrollFailureKindValues.NoMovement => ComputerUseWinFailureCodeValues.InputDispatchFailed,
            _ => ComputerUseWinFailureCodeValues.InputDispatchFailed,
        };

    private static bool NeedsActivationRetry(InputResult input) =>
        string.Equals(input.Status, InputStatusValues.Failed, StringComparison.Ordinal)
        && (string.Equals(input.FailureCode, InputFailureCodeValues.TargetNotForeground, StringComparison.Ordinal)
            || string.Equals(input.FailureCode, InputFailureCodeValues.TargetPreflightFailed, StringComparison.Ordinal));
}
