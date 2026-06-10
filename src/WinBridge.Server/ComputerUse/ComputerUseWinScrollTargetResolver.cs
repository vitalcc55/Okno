// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinScrollTargetResolver(
    IUiAutomationService uiAutomationService,
    IUiAutomationSemanticLookupService semanticLookupService)
{
    private readonly ComputerUseWinSemanticTargetResolver _semanticTargetResolver = new(uiAutomationService, semanticLookupService);
    private static readonly ComputerUseWinSemanticTargetPolicy TargetPolicy = new(
        ComputerUseWinActionability.IsScrollActionable,
        ComputerUseWinFailureCodeValues.UnsupportedAction,
        "elementIndex {0} не является scrollable target в последнем get_app_state.",
        "elementIndex {0} не является scrollable target в последнем get_app_state.",
        "Computer Use for Windows не смог пере-подтвердить scroll target по fresh observation path.",
        "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим scrollable live UI element.",
        "Fresh live element больше не поддерживает semantic scroll path.",
        "Selector больше не находит scroll target в текущем live UI state.",
        "Selector сопоставился с несколькими scroll targets в текущем live UI state.",
        "Selector lookup достиг budget до доказательства уникального scroll target.",
        "Selector lookup превысил timeout до доказательства scroll target.",
        "Computer Use for Windows не смог выполнить bounded semantic lookup для scroll target.",
        "Selector target не поддерживает semantic scroll path.");

    public async Task<ComputerUseWinScrollTargetResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        ComputerUseWinScrollPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.Equals(payload.TargetMode, ComputerUseWinSemanticTargetModeValues.ElementIndex, StringComparison.Ordinal)
            || string.Equals(payload.TargetMode, ComputerUseWinSemanticTargetModeValues.Selector, StringComparison.Ordinal))
        {
            return await ResolveSemanticTargetAsync(state, request, payload, cancellationToken).ConfigureAwait(false);
        }

        return ResolvePointTarget(state, request, payload);
    }

    private async Task<ComputerUseWinScrollTargetResolution> ResolveSemanticTargetAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        ComputerUseWinScrollPayload payload,
        CancellationToken cancellationToken)
    {
        ComputerUseWinSemanticTargetResolution targetResolution = await _semanticTargetResolver.ResolveAsync(
            state,
            request.ElementIndex,
            request.Selector,
            TargetPolicy,
            cancellationToken).ConfigureAwait(false);
        if (!targetResolution.IsSuccess)
        {
            return ComputerUseWinScrollTargetResolution.Failure(targetResolution.FailureDetails!);
        }

        return ComputerUseWinScrollTargetResolution.SemanticSuccess(targetResolution.EffectiveElement!, payload);
    }

    private static ComputerUseWinScrollTargetResolution ResolvePointTarget(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        ComputerUseWinScrollPayload payload)
    {
        InputAction action = new()
        {
            Type = InputActionTypeValues.Scroll,
            CoordinateSpace = payload.CoordinateSpace,
            Point = request.Point,
            Direction = payload.Direction,
            Delta = payload.Delta,
            CaptureReference = string.Equals(payload.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
                ? state.CaptureReference
                : null,
        };

        return ComputerUseWinScrollTargetResolution.PointSuccess(action, requiresConfirmation: true, payload);
    }
}

internal sealed record ComputerUseWinScrollTargetResolution(
    bool IsSuccess,
    bool RequiresConfirmation,
    bool UsesPointFallback,
    InputAction? InputAction,
    ComputerUseWinStoredElement? EffectiveElement,
    ComputerUseWinScrollPayload? Payload,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public static ComputerUseWinScrollTargetResolution SemanticSuccess(
        ComputerUseWinStoredElement effectiveElement,
        ComputerUseWinScrollPayload payload) =>
        new(true, false, false, null, effectiveElement, payload, null);

    public static ComputerUseWinScrollTargetResolution PointSuccess(
        InputAction inputAction,
        bool requiresConfirmation,
        ComputerUseWinScrollPayload payload) =>
        new(true, requiresConfirmation, true, inputAction, null, payload, null);

    public static ComputerUseWinScrollTargetResolution Failure(ComputerUseWinFailureDetails failure) =>
        new(false, false, false, null, null, null, failure);
}
