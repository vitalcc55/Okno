// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinDragTargetResolver(
    IUiAutomationService uiAutomationService,
    IUiAutomationSemanticLookupService? semanticLookupService = null)
{
    private readonly ComputerUseWinSemanticTargetResolver? _semanticTargetResolver = semanticLookupService is null
        ? null
        : new ComputerUseWinSemanticTargetResolver(uiAutomationService, semanticLookupService);

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

        ComputerUseWinDragEndpointResolution sourceResolution = await ResolveEndpointAsync(
                state,
                freshElements,
                payload.Source,
                payload.CoordinateSpace,
                endpointLabel: "source",
                cancellationToken).ConfigureAwait(false);
        if (!sourceResolution.IsSuccess)
        {
            return ComputerUseWinDragTargetResolution.Failure(sourceResolution.FailureDetails!);
        }

        ComputerUseWinDragEndpointResolution destinationResolution = await ResolveEndpointAsync(
                state,
                freshElements,
                payload.Destination,
                payload.CoordinateSpace,
                endpointLabel: "destination",
                cancellationToken).ConfigureAwait(false);
        if (!destinationResolution.IsSuccess)
        {
            return ComputerUseWinDragTargetResolution.Failure(destinationResolution.FailureDetails!);
        }

        ComputerUseWinStoredElement? sourceElement = sourceResolution.EffectiveElement;
        ComputerUseWinStoredElement? destinationElement = destinationResolution.EffectiveElement;
        InputPoint sourcePoint = sourceResolution.ResolvedPoint;
        InputPoint destinationPoint = destinationResolution.ResolvedPoint;
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

    private async Task<ComputerUseWinDragEndpointResolution> ResolveEndpointAsync(
        ComputerUseWinStoredState state,
        IReadOnlyDictionary<int, ComputerUseWinStoredElement>? freshElements,
        ComputerUseWinDragEndpointPayload endpointPayload,
        string? coordinateSpace,
        string endpointLabel,
        CancellationToken cancellationToken)
    {
        if (endpointPayload.ElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || !ComputerUseWinActionability.IsDragEndpointActionable(storedElement))
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"{endpointLabel} elementIndex {elementIndex} не является drag-capable target в последнем get_app_state."));
            }

            if (freshElements is null
                || !ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement, out ComputerUseWinStoredElement? freshElement)
                || freshElement is null)
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.StaleState,
                    $"{endpointLabel} elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element."));
            }

            if (!ComputerUseWinActionability.IsDragEndpointActionable(freshElement)
                || freshElement.Bounds is not Bounds freshBounds)
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"{endpointLabel} live element больше не предоставляет usable bounds для drag endpoint."));
            }

            return ComputerUseWinDragEndpointResolution.Success(
                freshElement,
                new InputPoint(
                (freshBounds.Left + freshBounds.Right) / 2,
                (freshBounds.Top + freshBounds.Bottom) / 2));
        }

        if (endpointPayload.Selector is not null)
        {
            if (_semanticTargetResolver is null)
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        $"Computer Use for Windows не смог выполнить bounded semantic lookup для drag {endpointLabel}."));
            }

            ComputerUseWinSemanticTargetResolution resolution = await _semanticTargetResolver.ResolveAsync(
                state,
                elementIndex: null,
                endpointPayload.Selector,
                CreateDragEndpointPolicy(endpointLabel),
                cancellationToken).ConfigureAwait(false);
            if (!resolution.IsSuccess)
            {
                return ComputerUseWinDragEndpointResolution.Failure(resolution.FailureDetails!);
            }

            ComputerUseWinStoredElement effectiveElement = resolution.EffectiveElement!;
            if (effectiveElement.Bounds is not Bounds bounds)
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        $"{endpointLabel} selector нашёл drag target без usable bounds."));
            }

            return ComputerUseWinDragEndpointResolution.Success(
                effectiveElement,
                new InputPoint(
                    (bounds.Left + bounds.Right) / 2,
                    (bounds.Top + bounds.Bottom) / 2));
        }

        if (endpointPayload.Point is not InputPoint point)
        {
            return ComputerUseWinDragEndpointResolution.Failure(
                ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                $"Для drag {endpointLabel} требуется elementIndex, selector или point."));
        }

        if (string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            if (state.CaptureReference is null)
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                    $"Для drag {endpointLabel} point в coordinateSpace=`capture_pixels` нужен актуальный get_app_state со свежим capture proof."));
            }

            if (point.X < 0
                || point.Y < 0
                || point.X >= state.CaptureReference.PixelWidth
                || point.Y >= state.CaptureReference.PixelHeight)
            {
                return ComputerUseWinDragEndpointResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.PointOutOfBounds,
                    $"Указанная {endpointLabel} capture_pixels point выходит за пределы capture raster из последнего get_app_state."));
            }
        }

        return ComputerUseWinDragEndpointResolution.Success(null, point);
    }

    private static ComputerUseWinSemanticTargetPolicy CreateDragEndpointPolicy(string endpointLabel) =>
        new(
            IsActionable: ComputerUseWinActionability.IsDragEndpointActionable,
            MissingTargetFailureCode: ComputerUseWinFailureCodeValues.UnsupportedAction,
            MissingTargetReason: $"{endpointLabel} elementIndex {{0}} не является drag-capable target в последнем get_app_state.",
            PreviewUnsupportedReason: $"{endpointLabel} elementIndex {{0}} не является drag-capable target в последнем get_app_state.",
            FreshObservationFailureReason: $"Computer Use for Windows не смог пере-подтвердить drag {endpointLabel} endpoint по fresh observation path.",
            FreshStaleReason: $"{endpointLabel} elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим live UI element.",
            FreshUnsupportedReason: $"{endpointLabel} live element больше не предоставляет usable bounds для drag endpoint.",
            SelectorZeroMatchesReason: $"{endpointLabel} selector не нашёл drag-capable target в текущем live UI.",
            SelectorAmbiguousReason: $"{endpointLabel} selector matched несколько drag candidates; уточни selector перед retry.",
            SelectorBudgetExceededReason: $"{endpointLabel} selector lookup для drag достиг bounded node budget; уточни selector или обнови state.",
            SelectorTimeoutReason: $"{endpointLabel} selector lookup для drag превысил bounded timeout; уточни selector или обнови state.",
            SelectorObservationFailureReason: $"Computer Use for Windows не смог выполнить bounded semantic lookup для drag {endpointLabel}.",
            SelectorUnsupportedReason: $"{endpointLabel} selector нашёл element, но он не является drag-capable target.");
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

internal sealed record ComputerUseWinDragEndpointResolution(
    bool IsSuccess,
    ComputerUseWinStoredElement? EffectiveElement,
    InputPoint ResolvedPoint,
    ComputerUseWinFailureDetails? FailureDetails)
{
    public static ComputerUseWinDragEndpointResolution Success(
        ComputerUseWinStoredElement? element,
        InputPoint point) =>
        new(true, element, point, null);

    public static ComputerUseWinDragEndpointResolution Failure(ComputerUseWinFailureDetails failure) =>
        new(false, null, new InputPoint(), failure);
}
