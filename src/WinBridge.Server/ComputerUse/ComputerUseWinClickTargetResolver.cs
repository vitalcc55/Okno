using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinClickTargetResolver(IUiAutomationService uiAutomationService)
{
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

        if (request.Point is not InputPoint point)
        {
            return ComputerUseWinClickTargetResolution.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.InvalidRequest,
                    "Для click требуется elementIndex или point."));
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
