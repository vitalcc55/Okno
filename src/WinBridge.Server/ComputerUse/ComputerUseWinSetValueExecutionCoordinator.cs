// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinSetValueExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IUiAutomationService uiAutomationService,
    IUiAutomationSetValueService setValueService)
{
    public async Task<ComputerUseWinActionExecutionOutcome> ExecuteAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinSetValueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!ComputerUseWinSetValueContract.TryParse(request, out ComputerUseWinSetValuePayload? payload, out string? failure))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    failure ?? "Запрос set_value не прошёл contract validation."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        if (!state.Elements.TryGetValue(request.ElementIndex!.Value, out ComputerUseWinStoredElement? storedElement))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    $"elementIndex {request.ElementIndex.Value} не существует в последнем get_app_state."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
        }

        if (!ComputerUseWinActionability.IsSetValueActionable(storedElement))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"elementIndex {request.ElementIndex.Value} не является settable semantic target в последнем get_app_state."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        ActivateWindowResult activation = await windowActivationService.ActivateAsync(state.Window, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(activation.Status, "done", StringComparison.Ordinal)
            && !string.Equals(activation.Status, "already_active", StringComparison.Ordinal))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinActivationFailureMapper.Map(activation),
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        ComputerUseWinStoredState resolvedState = state with
        {
            Window = activation.Window ?? state.Window,
        };

        UiaSnapshotResult snapshot;
        try
        {
            snapshot = await uiAutomationService.SnapshotAsync(
                resolvedState.Window,
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
            ComputerUseWinFailureDetails failureDetails = ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                "Computer Use for Windows не смог пере-подтвердить set_value target по fresh observation path.");
            return ComputerUseWinActionExecutionOutcome.Failure(
                failureDetails,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        if (!string.Equals(snapshot.Status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal)
            || snapshot.Root is null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.ObservationFailed,
                    snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить set_value target по fresh observation path."),
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
        if (!ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement, out ComputerUseWinStoredElement? freshElement)
            || freshElement is null)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.StaleState,
                    "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element."),
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        if (!ComputerUseWinActionability.IsSetValueActionable(freshElement))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    "Fresh live element больше не поддерживает semantic set path."),
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        UiaSetValueResult setResult = await setValueService.SetValueAsync(
            resolvedState.Window,
            new UiaSetValueRequest(
                ElementId: freshElement.ElementId,
                ValueKind: payload!.ValueKind,
                TextValue: payload.TextValue,
                NumberValue: payload.NumberValue),
            cancellationToken).ConfigureAwait(false);

        string dispatchPath = setResult.ResolvedPattern switch
        {
            "value_pattern" => "uia_value_pattern",
            "range_value_pattern" => "uia_range_value_pattern",
            _ => "uia_semantic_set",
        };

        if (!setResult.Success)
        {
            return ComputerUseWinActionExecutionOutcome.Success(
                new InputResult(
                    Status: InputStatusValues.Failed,
                    Decision: InputStatusValues.Failed,
                    FailureCode: MapFailureCode(setResult.FailureKind),
                    Reason: setResult.Reason,
                    TargetHwnd: resolvedState.Window.Hwnd,
                    CompletedActionCount: 0,
                    FailedActionIndex: 0),
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: dispatchPath);
        }

        return ComputerUseWinActionExecutionOutcome.Success(
            new InputResult(
                Status: InputStatusValues.Done,
                Decision: InputStatusValues.Done,
                ResultMode: InputResultModeValues.PostconditionVerified,
                TargetHwnd: resolvedState.Window.Hwnd,
                CompletedActionCount: 1),
            confirmationRequired: false,
            riskClass: "semantic_value",
            dispatchPath: dispatchPath);
    }

    private static string MapFailureCode(string? failureKind) =>
        failureKind switch
        {
            UiaSetValueFailureKindValues.MissingElement => ComputerUseWinFailureCodeValues.StaleState,
            UiaSetValueFailureKindValues.UnsupportedPattern => ComputerUseWinFailureCodeValues.UnsupportedAction,
            UiaSetValueFailureKindValues.ReadOnly => ComputerUseWinFailureCodeValues.UnsupportedAction,
            UiaSetValueFailureKindValues.ValueOutOfRange => ComputerUseWinFailureCodeValues.InvalidRequest,
            UiaSetValueFailureKindValues.InvalidValue => ComputerUseWinFailureCodeValues.InvalidRequest,
            _ => ComputerUseWinFailureCodeValues.InputDispatchFailed,
        };
}
