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
                    || storedElement.Bounds is null)
                {
                    return ComputerUseWinClickTargetResolution.Failure(
                        ComputerUseWinFailureCodeValues.InvalidRequest,
                        $"elementIndex {elementIndex} не существует или не даёт кликабельных bounds.");
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
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить target по fresh observation path.");
                }

                IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
                if (!TryResolveFreshElement(freshElements, storedElement, out ComputerUseWinStoredElement? effectiveElement)
                    || effectiveElement?.Bounds is not Bounds freshBounds)
                {
                    return ComputerUseWinClickTargetResolution.Failure(
                        ComputerUseWinFailureCodeValues.StaleState,
                        "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.");
                }

                return ComputerUseWinClickTargetResolution.Success(
                    new InputAction
                    {
                        Type = InputActionTypeValues.Click,
                        CoordinateSpace = InputCoordinateSpaceValues.Screen,
                        Point = new InputPoint((freshBounds.Left + freshBounds.Right) / 2, (freshBounds.Top + freshBounds.Bottom) / 2),
                        Button = string.IsNullOrWhiteSpace(request.Button) ? InputButtonValues.Left : request.Button,
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
                ComputerUseWinObservationFailure failure = ComputerUseWinObservationFailureTranslator.Translate(
                    exception,
                    "Computer Use for Windows не смог пере-подтвердить target по fresh observation path.");
                return ComputerUseWinClickTargetResolution.Failure(failure.FailureCode, failure.Reason);
            }
        }

        if (request.Point is not InputPoint point)
        {
            return ComputerUseWinClickTargetResolution.Failure(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                "Для click требуется elementIndex или point.");
        }

        string coordinateSpace = string.IsNullOrWhiteSpace(request.CoordinateSpace)
            ? InputCoordinateSpaceValues.CapturePixels
            : request.CoordinateSpace!;
        return ComputerUseWinClickTargetResolution.Success(
            new InputAction
            {
                Type = InputActionTypeValues.Click,
                CoordinateSpace = coordinateSpace,
                Point = point,
                Button = string.IsNullOrWhiteSpace(request.Button) ? InputButtonValues.Left : request.Button,
                CaptureReference = string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
                    ? state.CaptureReference
                    : null,
            },
            element: null,
            requiresConfirmation: true);
    }

    private static bool TryResolveFreshElement(
        IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements,
        ComputerUseWinStoredElement storedElement,
        out ComputerUseWinStoredElement? effectiveElement)
    {
        effectiveElement = freshElements.Values.FirstOrDefault(item =>
            string.Equals(item.ElementId, storedElement.ElementId, StringComparison.Ordinal));
        if (effectiveElement is not null)
        {
            return true;
        }

        ComputerUseWinStoredElement[] fallbackMatches = freshElements.Values
            .Where(item =>
                string.Equals(item.ControlType, storedElement.ControlType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, storedElement.Name, StringComparison.Ordinal)
                && string.Equals(item.AutomationId, storedElement.AutomationId, StringComparison.Ordinal))
            .ToArray();

        if (fallbackMatches.Length == 1)
        {
            effectiveElement = fallbackMatches[0];
            return true;
        }

        effectiveElement = null;
        return false;
    }
}

internal sealed record ComputerUseWinClickTargetResolution(
    bool IsSuccess,
    InputAction? Action,
    ComputerUseWinStoredElement? EffectiveElement,
    bool RequiresConfirmation,
    string? FailureCode,
    string? Reason)
{
    public static ComputerUseWinClickTargetResolution Success(
        InputAction action,
        ComputerUseWinStoredElement? element,
        bool requiresConfirmation) =>
        new(true, action, element, requiresConfirmation, null, null);

    public static ComputerUseWinClickTargetResolution Failure(string failureCode, string reason) =>
        new(false, null, null, false, failureCode, reason);
}
