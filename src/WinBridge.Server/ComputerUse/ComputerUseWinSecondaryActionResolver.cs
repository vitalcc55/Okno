// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinSecondaryActionResolver(
    IUiAutomationService uiAutomationService,
    IUiAutomationSemanticLookupService semanticLookupService)
{
    internal static readonly ComputerUseWinSemanticTargetPolicy TargetPolicy = new(
        ComputerUseWinActionability.IsPerformSecondaryActionActionable,
        ComputerUseWinFailureCodeValues.UnsupportedAction,
        "elementIndex {0} не является supported secondary semantic target в последнем get_app_state.",
        "elementIndex {0} не является supported secondary semantic target в последнем get_app_state.",
        "Computer Use for Windows не смог пере-подтвердить target для secondary semantic path.",
        "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим secondary semantic target.",
        "Fresh live element больше не имеет supported secondary semantic affordance в текущем live UI state.",
        "Selector больше не находит secondary semantic target в текущем live UI state.",
        "Selector сопоставился с несколькими secondary semantic targets в текущем live UI state.",
        "Selector lookup достиг budget до доказательства уникального secondary semantic target.",
        "Selector lookup превысил timeout до доказательства secondary semantic target.",
        "Computer Use for Windows не смог выполнить bounded semantic lookup для secondary semantic target.",
        "Selector target не поддерживает secondary semantic action.");

    private readonly ComputerUseWinSemanticTargetResolver _targetResolver = new(uiAutomationService, semanticLookupService);

    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinPerformSecondaryActionRequest request,
        out ComputerUseWinStoredElement? storedElement,
        out ComputerUseWinFailureDetails? failure) =>
        ComputerUseWinSemanticTargetResolver.TryClassifyBeforeActivation(
            state,
            request.ElementIndex,
            request.Selector,
            TargetPolicy,
            out storedElement,
            out failure);

    public async Task<ComputerUseWinSecondaryActionResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinPerformSecondaryActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        ComputerUseWinSemanticTargetResolution targetResolution = await _targetResolver.ResolveAsync(
            state,
            request.ElementIndex,
            request.Selector,
            TargetPolicy,
            cancellationToken).ConfigureAwait(false);
        if (!targetResolution.IsSuccess)
        {
            return ComputerUseWinSecondaryActionResolution.Failure(
                targetResolution.FailureDetails!);
        }

        ComputerUseWinStoredElement effectiveElement = targetResolution.EffectiveElement!;
        if (!TryResolveActionKind(effectiveElement.Patterns, out string? actionKind))
        {
            return ComputerUseWinSecondaryActionResolution.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    "Fresh live element больше не имеет supported secondary semantic affordance в текущем live UI state."));
        }

        bool isRisky = ComputerUseWinTargetPolicy.RequiresRiskConfirmation(effectiveElement, ToolNames.ComputerUseWinPerformSecondaryAction);

        return ComputerUseWinSecondaryActionResolution.Success(effectiveElement, actionKind!, isRisky);
    }

    public static bool TryResolveActionKind(IReadOnlyList<string>? patterns, out string? actionKind)
    {
        actionKind = null;
        if (patterns is null)
        {
            return false;
        }

        if (patterns.Contains("toggle", StringComparer.Ordinal))
        {
            actionKind = UiaSecondaryActionKindValues.Toggle;
            return true;
        }

        return false;
    }
}

internal sealed record ComputerUseWinSecondaryActionResolution(
    bool IsSuccess,
    ComputerUseWinStoredElement? EffectiveElement,
    string? ActionKind,
    bool RequiresConfirmation,
    bool IsRisky,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public static ComputerUseWinSecondaryActionResolution Success(
        ComputerUseWinStoredElement effectiveElement,
        string actionKind,
        bool isRisky) =>
        new(true, effectiveElement, actionKind, isRisky, isRisky, null);

    public static ComputerUseWinSecondaryActionResolution Failure(ComputerUseWinFailureDetails failure) =>
        new(false, null, null, false, false, failure);
}
