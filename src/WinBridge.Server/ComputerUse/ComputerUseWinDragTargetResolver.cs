using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinDragTargetResolver(IUiAutomationService uiAutomationService)
{
    public async Task<ComputerUseWinDragTargetResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinDragRequest request,
        ComputerUseWinDragPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);

        IReadOnlyDictionary<int, ComputerUseWinStoredElement>? freshElements = null;
        if (request.FromElementIndex is not null || request.ToElementIndex is not null)
        {
            try
            {
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
                    return ComputerUseWinDragTargetResolution.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.ObservationFailed,
                            snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить drag endpoints по fresh observation path."));
                }

                freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ComputerUseWinFailureDetails failure = ComputerUseWinObservationFailureTranslator.Translate(
                    exception,
                    "Computer Use for Windows не смог пере-подтвердить drag endpoints по fresh observation path.");
                return ComputerUseWinDragTargetResolution.Failure(failure);
            }
        }

        if (!TryResolveEndpoint(
                state,
                freshElements,
                request.FromElementIndex,
                request.FromPoint,
                payload.Source,
                payload.CoordinateSpace,
                endpointLabel: "source",
                out ComputerUseWinStoredElement? sourceElement,
                out InputPoint sourcePoint,
                out ComputerUseWinFailureDetails? sourceFailure))
        {
            return ComputerUseWinDragTargetResolution.Failure(sourceFailure!);
        }

        if (!TryResolveEndpoint(
                state,
                freshElements,
                request.ToElementIndex,
                request.ToPoint,
                payload.Destination,
                payload.CoordinateSpace,
                endpointLabel: "destination",
                out ComputerUseWinStoredElement? destinationElement,
                out InputPoint destinationPoint,
                out ComputerUseWinFailureDetails? destinationFailure))
        {
            return ComputerUseWinDragTargetResolution.Failure(destinationFailure!);
        }

        if (sourcePoint.X == destinationPoint.X && sourcePoint.Y == destinationPoint.Y)
        {
            return ComputerUseWinDragTargetResolution.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    "Drag не может использовать один и тот же resolved endpoint для source и destination."));
        }

        InputAction action = string.Equals(payload.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
            ? new InputAction
            {
                Type = InputActionTypeValues.Drag,
                CoordinateSpace = payload.CoordinateSpace,
                Path = [sourcePoint, destinationPoint],
                CaptureReference = state.CaptureReference,
            }
            : new InputAction
            {
                Type = InputActionTypeValues.Drag,
                CoordinateSpace = payload.CoordinateSpace ?? InputCoordinateSpaceValues.Screen,
                Path = [sourcePoint, destinationPoint],
            };

        return ComputerUseWinDragTargetResolution.Success(
            action,
            sourceElement,
            destinationElement,
            payload);
    }

    private static bool TryResolveEndpoint(
        ComputerUseWinStoredState state,
        IReadOnlyDictionary<int, ComputerUseWinStoredElement>? freshElements,
        int? requestedElementIndex,
        InputPoint? requestedPoint,
        ComputerUseWinDragEndpointPayload endpointPayload,
        string? coordinateSpace,
        string endpointLabel,
        out ComputerUseWinStoredElement? effectiveElement,
        out InputPoint resolvedPoint,
        out ComputerUseWinFailureDetails? failure)
    {
        if (requestedElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || !ComputerUseWinActionability.IsDragEndpointActionable(storedElement))
            {
                effectiveElement = null!;
                resolvedPoint = new InputPoint();
                failure = ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"{endpointLabel} elementIndex {elementIndex} не является drag-capable target в последнем get_app_state.");
                return false;
            }

            if (freshElements is null
                || !ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement, out ComputerUseWinStoredElement? freshElement)
                || freshElement is null)
            {
                effectiveElement = null!;
                resolvedPoint = new InputPoint();
                failure = ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.StaleState,
                    $"{endpointLabel} elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.");
                return false;
            }

            if (!ComputerUseWinActionability.IsDragEndpointActionable(freshElement)
                || freshElement.Bounds is not Bounds freshBounds)
            {
                effectiveElement = null!;
                resolvedPoint = new InputPoint();
                failure = ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"{endpointLabel} live element больше не предоставляет usable bounds для drag endpoint.");
                return false;
            }

            effectiveElement = freshElement;
            resolvedPoint = new InputPoint(
                (freshBounds.Left + freshBounds.Right) / 2,
                (freshBounds.Top + freshBounds.Bottom) / 2);
            failure = null;
            return true;
        }

        if (requestedPoint is not InputPoint point)
        {
                effectiveElement = null!;
                resolvedPoint = new InputPoint();
            failure = ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                $"Для drag {endpointLabel} требуется elementIndex или point.");
            return false;
        }

        effectiveElement = null!;
        resolvedPoint = point;
        failure = null;

        if (string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            if (state.CaptureReference is null)
            {
                failure = ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                    $"Для drag {endpointLabel} point в coordinateSpace=`capture_pixels` нужен актуальный get_app_state со свежим capture proof.");
                return false;
            }

            if (point.X < 0
                || point.Y < 0
                || point.X >= state.CaptureReference.PixelWidth
                || point.Y >= state.CaptureReference.PixelHeight)
            {
                failure = ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.PointOutOfBounds,
                    $"Указанная {endpointLabel} capture_pixels point выходит за пределы capture raster из последнего get_app_state.");
                return false;
            }
        }

        return true;
    }
}

internal sealed record ComputerUseWinDragTargetResolution(
    bool IsSuccess,
    InputAction? Action,
    ComputerUseWinStoredElement? SourceElement,
    ComputerUseWinStoredElement? DestinationElement,
    ComputerUseWinDragPayload? Payload,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public static ComputerUseWinDragTargetResolution Success(
        InputAction action,
        ComputerUseWinStoredElement? sourceElement,
        ComputerUseWinStoredElement? destinationElement,
        ComputerUseWinDragPayload payload) =>
        new(true, action, sourceElement, destinationElement, payload, null);

    public static ComputerUseWinDragTargetResolution Failure(ComputerUseWinFailureDetails failure) =>
        new(false, null, null, null, null, failure);
}
