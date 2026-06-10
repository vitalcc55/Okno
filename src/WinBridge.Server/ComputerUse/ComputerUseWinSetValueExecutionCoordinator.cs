// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinSetValueExecutionCoordinator(
    IWindowActivationService windowActivationService,
    IUiAutomationService uiAutomationService,
    IUiAutomationSemanticLookupService semanticLookupService,
    IUiAutomationSetValueService setValueService)
{
    private readonly ComputerUseWinSemanticTargetResolver _targetResolver = new(uiAutomationService, semanticLookupService);
    private static readonly ComputerUseWinSemanticTargetPolicy TargetPolicy = new(
        ComputerUseWinActionability.IsSetValueActionable,
        ComputerUseWinFailureCodeValues.InvalidRequest,
        "elementIndex {0} не существует в последнем get_app_state.",
        "elementIndex {0} не является settable semantic target в последнем get_app_state.",
        "Computer Use for Windows не смог пере-подтвердить set_value target по fresh observation path.",
        "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.",
        "Fresh live element больше не поддерживает semantic set path.",
        "Selector больше не находит set_value target в текущем live UI state.",
        "Selector сопоставился с несколькими set_value targets в текущем live UI state.",
        "Selector lookup достиг budget до доказательства уникального set_value target.",
        "Selector lookup превысил timeout до доказательства set_value target.",
        "Computer Use for Windows не смог выполнить bounded semantic lookup для set_value target.",
        "Selector target не поддерживает semantic set path.");

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

        if (ComputerUseWinSemanticTargetResolver.TryClassifyBeforeActivation(
                state,
                request.ElementIndex,
                request.Selector,
                TargetPolicy,
                out _,
                out ComputerUseWinFailureDetails? targetFailure))
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                targetFailure!,
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

        ComputerUseWinSemanticTargetResolution targetResolution = await _targetResolver.ResolveAsync(
            resolvedState,
            request.ElementIndex,
            request.Selector,
            TargetPolicy,
            cancellationToken).ConfigureAwait(false);
        if (!targetResolution.IsSuccess)
        {
            return ComputerUseWinActionExecutionOutcome.Failure(
                targetResolution.FailureDetails!,
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch,
                confirmationRequired: false,
                riskClass: "semantic_value",
                dispatchPath: null);
        }

        UiaSetValueResult setResult = await setValueService.SetValueAsync(
            resolvedState.Window,
            new UiaSetValueRequest(
                ElementId: targetResolution.EffectiveElement!.ElementId,
                ValueKind: payload!.ValueKind,
                TextValue: payload.TextValue,
                NumberValue: payload.NumberValue),
            cancellationToken).ConfigureAwait(false);

        string dispatchPath = ComputerUseWinExecutionExecutorValues.ResolveSetValue(setResult.ResolvedPattern);

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
