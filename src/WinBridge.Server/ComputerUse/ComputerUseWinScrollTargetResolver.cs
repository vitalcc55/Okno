// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinScrollTargetResolver(IUiAutomationService uiAutomationService)
{
    public async Task<ComputerUseWinScrollTargetResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        ComputerUseWinScrollPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.Equals(payload.TargetMode, "element_index", StringComparison.Ordinal))
        {
            return await ResolveElementTargetAsync(state, request, payload, cancellationToken).ConfigureAwait(false);
        }

        return ResolvePointTarget(state, request, payload);
    }

    private async Task<ComputerUseWinScrollTargetResolution> ResolveElementTargetAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        ComputerUseWinScrollPayload payload,
        CancellationToken cancellationToken)
    {
        int elementIndex = request.ElementIndex!.Value;
        try
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || !ComputerUseWinActionability.IsScrollActionable(storedElement))
            {
                return ComputerUseWinScrollTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        $"elementIndex {elementIndex} не является scrollable target в последнем get_app_state."));
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
                return ComputerUseWinScrollTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить scroll target по fresh observation path."));
            }

            IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
            if (!ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement, out ComputerUseWinStoredElement? freshElement)
                || freshElement is null
                || !ComputerUseWinActionability.IsScrollActionable(freshElement))
            {
                return ComputerUseWinScrollTargetResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.StaleState,
                        "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим scrollable live UI element."));
            }

            return ComputerUseWinScrollTargetResolution.SemanticSuccess(freshElement, payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ComputerUseWinFailureDetails failure = ComputerUseWinObservationFailureTranslator.Translate(
                exception,
                "Computer Use for Windows не смог пере-подтвердить scroll target по fresh observation path.");
            return ComputerUseWinScrollTargetResolution.Failure(failure);
        }
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
