// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinTypeTextExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IUiAutomationService uiAutomationService,
    IInputService inputService,
    IUiAutomationSemanticLookupService? semanticLookupService = null)
{
    private const string DispatchPath = ComputerUseWinExecutionExecutorValues.Win32SendInputUnicode;
    private const string RiskClass = "text_input";
    private const string FocusedFallbackRiskClass = "focused_text_fallback";
    private const string CoordinateConfirmedFallbackRiskClass = "coordinate_confirmed_text_fallback";
    private readonly ComputerUseWinSemanticTargetResolver? _semanticTargetResolver = semanticLookupService is null
        ? null
        : new ComputerUseWinSemanticTargetResolver(uiAutomationService, semanticLookupService);

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

        ComputerUseWinTypeTextPayload parsedPayload = payload!;
        if (parsedPayload.UsesCoordinateConfirmedFallback)
        {
            return await ExecuteCoordinateConfirmedFallbackAsync(
                state,
                parsedPayload,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.Selector is not null)
        {
            return await ExecuteSelectorTargetAsync(
                state,
                request,
                parsedPayload,
                cancellationToken).ConfigureAwait(false);
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

        ComputerUseWinStoredElement resolvedStoredTarget = storedTarget!;
        bool focusedFallbackUsed = request.AllowFocusedFallback
            && !ComputerUseWinActionability.IsTypeTextActionable(resolvedStoredTarget);
        string riskClass = focusedFallbackUsed ? FocusedFallbackRiskClass : RiskClass;

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, ActivateWindowStatusValues.Done, StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: focusedFallbackUsed,
                riskClass: riskClass,
                dispatchPath: DispatchPath,
                fallbackUsed: focusedFallbackUsed);
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        ComputerUseWinFailureDetails? revalidationFailure = await RevalidateFocusedTargetAsync(
            resolvedState,
            resolvedStoredTarget,
            focusedFallbackUsed,
            cancellationToken).ConfigureAwait(false);
        if (revalidationFailure is not null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                revalidationFailure,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: focusedFallbackUsed,
                riskClass: riskClass,
                dispatchPath: DispatchPath,
                fallbackUsed: focusedFallbackUsed);
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
                    focusedFallbackUsed,
                    cancellationToken).ConfigureAwait(false);
                if (revalidationFailure is not null)
                {
                    return ComputerUseWinActionExecutionOutcome.Failure(
                        revalidationFailure,
                        ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                        confirmationRequired: focusedFallbackUsed,
                        riskClass: riskClass,
                        dispatchPath: DispatchPath,
                        fallbackUsed: focusedFallbackUsed);
                }

                input = await ExecuteInputAsync(resolvedState, parsedPayload.Text, cancellationToken).ConfigureAwait(false);
            }
        }

        InputResult publicInput = focusedFallbackUsed
            ? NormalizeFocusedFallbackResult(input)
            : input;

        return ComputerUseWinActionExecutionOutcome.Success(
            publicInput,
            confirmationRequired: focusedFallbackUsed,
            riskClass: riskClass,
            dispatchPath: DispatchPath,
            fallbackUsed: focusedFallbackUsed,
            successorObservationWindow: resolvedState.Window);
    }

    private async Task<ComputerUseWinActionExecutionOutcome> ExecuteSelectorTargetAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinTypeTextRequest request,
        ComputerUseWinTypeTextPayload payload,
        CancellationToken cancellationToken)
    {
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

        ComputerUseWinFailureDetails? resolutionFailure = await ResolveSelectorTargetAsync(
            resolvedState,
            request.Selector!,
            cancellationToken).ConfigureAwait(false);
        if (resolutionFailure is not null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                resolutionFailure,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: RiskClass,
                dispatchPath: DispatchPath);
        }

        InputResult input = await ExecuteInputAsync(resolvedState, payload.Text, cancellationToken).ConfigureAwait(false);
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

                resolutionFailure = await ResolveSelectorTargetAsync(
                    resolvedState,
                    request.Selector!,
                    cancellationToken).ConfigureAwait(false);
                if (resolutionFailure is not null)
                {
                    return ComputerUseWinActionExecutionOutcome.Failure(
                        resolutionFailure,
                        ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                        confirmationRequired: false,
                        riskClass: RiskClass,
                        dispatchPath: DispatchPath);
                }

                input = await ExecuteInputAsync(resolvedState, payload.Text, cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            input,
            confirmationRequired: false,
            riskClass: RiskClass,
            dispatchPath: DispatchPath,
            successorObservationWindow: resolvedState.Window);
    }

    private async Task<ComputerUseWinFailureDetails?> ResolveSelectorTargetAsync(
        ComputerUseWinStoredState state,
        WaitElementSelector selector,
        CancellationToken cancellationToken)
    {
        if (_semanticTargetResolver is null)
        {
            return ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                "Computer Use for Windows не смог выполнить bounded semantic lookup для type_text.");
        }

        ComputerUseWinSemanticTargetResolution resolution = await _semanticTargetResolver.ResolveAsync(
            state,
            elementIndex: null,
            selector,
            CreateTypeTextPolicy(),
            cancellationToken).ConfigureAwait(false);
        return resolution.IsSuccess ? null : resolution.FailureDetails;
    }

    private async Task<ComputerUseWinActionExecutionOutcome> ExecuteCoordinateConfirmedFallbackAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinTypeTextPayload payload,
        CancellationToken cancellationToken)
    {
        InputPoint point = payload.Point!;
        string coordinateSpace = payload.CoordinateSpace ?? InputCoordinateSpaceValues.CapturePixels;
        const string dispatchPath = ComputerUseWinExecutionExecutorValues.CapturePixelsTextInput;

        if (state.CaptureReference is null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                    "Coordinate-confirmed type_text fallback требует актуальный get_app_state со свежим capture_pixels proof."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: CoordinateConfirmedFallbackRiskClass,
                dispatchPath: dispatchPath,
                fallbackUsed: true);
        }

        if (point.X < 0
            || point.Y < 0
            || point.X >= state.CaptureReference.PixelWidth
            || point.Y >= state.CaptureReference.PixelHeight)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.PointOutOfBounds,
                    "Указанная type_text capture_pixels point выходит за пределы capture raster из последнего get_app_state; скорректируй point перед retry."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: CoordinateConfirmedFallbackRiskClass,
                dispatchPath: dispatchPath,
                fallbackUsed: true);
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, ActivateWindowStatusValues.Done, StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: true,
                riskClass: CoordinateConfirmedFallbackRiskClass,
                dispatchPath: dispatchPath,
                fallbackUsed: true);
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        InputResult input = await ExecuteCoordinateConfirmedInputAsync(
            resolvedState,
            point,
            coordinateSpace,
            payload.Text,
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

                input = await ExecuteCoordinateConfirmedInputAsync(
                    resolvedState,
                    point,
                    coordinateSpace,
                    payload.Text,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            NormalizeCoordinateConfirmedFallbackResult(input),
            confirmationRequired: true,
            riskClass: CoordinateConfirmedFallbackRiskClass,
            dispatchPath: dispatchPath,
            fallbackUsed: true,
            successorObservationWindow: resolvedState.Window);
    }

    private async Task<ComputerUseWinFailureDetails?> RevalidateFocusedTargetAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinStoredElement storedTarget,
        bool focusedFallbackUsed,
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

        if (focusedFallbackUsed)
        {
            ComputerUseWinStoredElement[] focusedElements = freshElements.Values
                .Where(static item => item.HasKeyboardFocus)
                .ToArray();
            if (focusedElements.Length != 1
                || !string.Equals(focusedElements[0].ElementId, freshElement.ElementId, StringComparison.Ordinal)
                || !ComputerUseWinActionability.IsFocusedTypeTextFallbackCandidate(freshElement))
            {
                return ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.StaleState,
                    "Focused fallback proof из stateToken устарел; сначала заново получи get_app_state после click/focus.");
            }

            return null;
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

    private Task<InputResult> ExecuteCoordinateConfirmedInputAsync(
        ComputerUseWinStoredState state,
        InputPoint point,
        string coordinateSpace,
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
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = coordinateSpace,
                        Point = point,
                        Button = InputButtonValues.Left,
                        CaptureReference = state.CaptureReference,
                    },
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
                if (request.AllowFocusedFallback
                    && ComputerUseWinActionability.IsFocusedTypeTextFallbackCandidate(storedTarget))
                {
                    failureCode = null;
                    reason = null;
                    return true;
                }

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
            if (request.AllowFocusedFallback)
            {
                ComputerUseWinStoredElement[] focusedFallbackTargets = state.Elements.Values
                    .Where(ComputerUseWinActionability.IsFocusedTypeTextFallbackCandidate)
                    .ToArray();
                if (focusedFallbackTargets.Length == 1)
                {
                    storedTarget = focusedFallbackTargets[0];
                    failureCode = null;
                    reason = null;
                    return true;
                }

                failureCode = ComputerUseWinFailureCodeValues.UnsupportedAction;
                reason = "type_text allowFocusedFallback без elementIndex требует ровно один focused target-local element в последнем get_app_state.";
                return false;
            }

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

    private static InputResult NormalizeFocusedFallbackResult(InputResult input)
    {
        if (!string.Equals(input.Status, InputStatusValues.Done, StringComparison.Ordinal))
        {
            return input;
        }

        return input with
        {
            Status = InputStatusValues.VerifyNeeded,
            Decision = InputStatusValues.VerifyNeeded,
            ResultMode = InputResultModeValues.DispatchOnly,
            Reason = input.Reason ?? "Focused type_text fallback dispatched через SendInput; проверь обновлённое состояние UI.",
        };
    }

    private static InputResult NormalizeCoordinateConfirmedFallbackResult(InputResult input)
    {
        if (!string.Equals(input.Status, InputStatusValues.Done, StringComparison.Ordinal))
        {
            return input;
        }

        return input with
        {
            Status = InputStatusValues.VerifyNeeded,
            Decision = InputStatusValues.VerifyNeeded,
            ResultMode = InputResultModeValues.DispatchOnly,
            Reason = input.Reason ?? "Coordinate-confirmed type_text fallback dispatched через SendInput; проверь обновлённое состояние UI.",
        };
    }

    private static ComputerUseWinSemanticTargetPolicy CreateTypeTextPolicy() =>
        new(
            IsActionable: ComputerUseWinActionability.IsTypeTextActionable,
            MissingTargetFailureCode: ComputerUseWinFailureCodeValues.InvalidRequest,
            MissingTargetReason: "elementIndex {0} не существует в последнем get_app_state.",
            PreviewUnsupportedReason: "type_text требует elementIndex, который уже опубликован как focused editable target; сначала переведи focus через click и заново вызови get_app_state.",
            FreshObservationFailureReason: "Computer Use for Windows не смог пере-подтвердить focused editable target для type_text.",
            FreshStaleReason: "Focused editable element из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.",
            FreshUnsupportedReason: "Focused editable proof из stateToken устарел; сначала заново получи get_app_state после click/focus.",
            SelectorZeroMatchesReason: "selector не нашёл focused editable target в текущем live UI.",
            SelectorAmbiguousReason: "selector matched несколько focused editable candidates; уточни selector перед retry.",
            SelectorBudgetExceededReason: "selector lookup для type_text достиг bounded node budget; уточни selector или обнови state.",
            SelectorTimeoutReason: "selector lookup для type_text превысил bounded timeout; уточни selector или обнови state.",
            SelectorObservationFailureReason: "Computer Use for Windows не смог выполнить bounded semantic lookup для type_text.",
            SelectorUnsupportedReason: "selector нашёл element, но он не является focused editable target для type_text.");

}
