// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinClickTargetResolver(
    IUiAutomationService uiAutomationService,
    IUiAutomationSemanticLookupService? semanticLookupService = null)
{
    private readonly ComputerUseWinSemanticTargetResolver? _semanticTargetResolver = semanticLookupService is null
        ? null
        : new ComputerUseWinSemanticTargetResolver(uiAutomationService, semanticLookupService);

    public async Task<ComputerUseWinClickTargetResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (request.ElementIndex is int elementIndex)
        {
            try
            {
                if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                    || !ComputerUseWinActionability.IsClickActionable(storedElement))
                {
                    return ComputerUseWinClickTargetResolution.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.InvalidRequest,
                            $"elementIndex {elementIndex} не существует или больше не является clickable target в последнем get_app_state."));
                }

                UiaSnapshotResult snapshot = await uiAutomationService.SnapshotAsync(
                    state.Window,
                    new UiaSnapshotRequest
                    {
                        Depth = state.Observation.RequestedDepth,
                        MaxNodes = state.Observation.RequestedMaxNodes,
                    },
                    cancellationToken).ConfigureAwait(false);

                if (!string.Equals(snapshot.Status, UiaSnapshotStatusValues.Done, StringComparison.Ordinal)
                    || snapshot.Root is null)
                {
                    return ComputerUseWinClickTargetResolution.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.ObservationFailed,
                            snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить target по fresh observation path."));
                }

                IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
                if (!ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement, out ComputerUseWinStoredElement? effectiveElement)
                    || effectiveElement is null
                    || !ComputerUseWinActionability.IsClickActionable(effectiveElement)
                    || effectiveElement.Bounds is not Bounds freshBounds)
                {
                    return ComputerUseWinClickTargetResolution.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.StaleState,
                            "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element."));
                }

                return ComputerUseWinClickTargetResolution.Success(
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.Screen,
                        Point = new InputPoint((freshBounds.Left + freshBounds.Right) / 2, (freshBounds.Top + freshBounds.Bottom) / 2),
                        Button = request.Button is null ? InputButtonValues.Left : request.Button,
                    },
                    effectiveElement,
                    ComputerUseWinTargetPolicy.RequiresRiskConfirmation(effectiveElement, ToolNames.ComputerUseWinClick));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ComputerUseWinFailureDetails failure = ComputerUseWinObservationFailureTranslator.Translate(
                    exception,
                    "Computer Use for Windows не смог пере-подтвердить target по fresh observation path.");
                return ComputerUseWinClickTargetResolution.Failure(failure);
            }
        }

        if (request.Selector is not null)
        {
            if (_semanticTargetResolver is null)
            {
                return ComputerUseWinClickTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        "Computer Use for Windows не смог выполнить bounded semantic lookup для click."));
            }

            ComputerUseWinSemanticTargetResolution resolution = await _semanticTargetResolver.ResolveAsync(
                state,
                elementIndex: null,
                request.Selector,
                CreateClickPolicy(),
                cancellationToken).ConfigureAwait(false);
            if (!resolution.IsSuccess)
            {
                return ComputerUseWinClickTargetResolution.Failure(resolution.FailureDetails!);
            }

            ComputerUseWinStoredElement effectiveElement = resolution.EffectiveElement!;
            if (effectiveElement.Bounds is not Bounds bounds)
            {
                return ComputerUseWinClickTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        "selector нашёл click target без usable bounds."));
            }

            return ComputerUseWinClickTargetResolution.Success(
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Point = new InputPoint((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2),
                    Button = request.Button is null ? InputButtonValues.Left : request.Button,
                },
                effectiveElement,
                ComputerUseWinTargetPolicy.RequiresRiskConfirmation(effectiveElement, ToolNames.ComputerUseWinClick));
        }

        if (request.Point is not InputPoint point)
        {
            return ComputerUseWinClickTargetResolution.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    "Для click требуется elementIndex, selector или point."));
        }

        string coordinateSpace = request.CoordinateSpace is null
            ? InputCoordinateSpaceValues.CapturePixels
            : request.CoordinateSpace!;
        return ComputerUseWinClickTargetResolution.Success(
            new InputAction
            {
                Type = InputActionTypeValues.Click,
                CoordinateSpace = coordinateSpace,
                Point = point,
                Button = request.Button is null ? InputButtonValues.Left : request.Button,
                CaptureReference = string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
                    ? state.CaptureReference
                    : null,
            },
            element: null,
            requiresConfirmation: true);
    }

    private static ComputerUseWinSemanticTargetPolicy CreateClickPolicy() =>
        new(
            IsActionable: ComputerUseWinActionability.IsClickActionable,
            MissingTargetFailureCode: ComputerUseWinFailureCodeValues.InvalidRequest,
            MissingTargetReason: "elementIndex {0} не существует в последнем get_app_state.",
            PreviewUnsupportedReason: "elementIndex {0} больше не является clickable target в последнем get_app_state.",
            FreshObservationFailureReason: "Computer Use for Windows не смог пере-подтвердить click target по fresh observation path.",
            FreshStaleReason: "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.",
            FreshUnsupportedReason: "live element больше не является clickable target.",
            SelectorZeroMatchesReason: "selector не нашёл clickable target в текущем live UI.",
            SelectorAmbiguousReason: "selector matched несколько clickable candidates; уточни selector перед retry.",
            SelectorBudgetExceededReason: "selector lookup для click достиг bounded node budget; уточни selector или обнови state.",
            SelectorTimeoutReason: "selector lookup для click превысил bounded timeout; уточни selector или обнови state.",
            SelectorObservationFailureReason: "Computer Use for Windows не смог выполнить bounded semantic lookup для click.",
            SelectorUnsupportedReason: "selector нашёл element, но он не является clickable target.");

}

internal sealed record ComputerUseWinClickTargetResolution(
    bool IsSuccess,
    InputAction? Action,
    ComputerUseWinStoredElement? EffectiveElement,
    bool RequiresConfirmation,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public string? FailureCode => FailureDetails?.FailureCode;

    public string? Reason => FailureDetails?.Reason;

    public static ComputerUseWinClickTargetResolution Success(
        InputAction action,
        ComputerUseWinStoredElement? element,
        bool requiresConfirmation) =>
        new(true, action, element, requiresConfirmation, null);

    public static ComputerUseWinClickTargetResolution Failure(ComputerUseWinFailureDetails failure) =>
        new(false, null, null, false, failure);
}
