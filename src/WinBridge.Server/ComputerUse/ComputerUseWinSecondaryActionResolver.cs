using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinSecondaryActionResolver(IUiAutomationService uiAutomationService)
{
    public async Task<ComputerUseWinSecondaryActionResolution> ResolveAsync(
        ComputerUseWinStoredState state,
        ComputerUseWinPerformSecondaryActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        int elementIndex = request.ElementIndex!.Value;
        if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
            || !ComputerUseWinActionability.IsPerformSecondaryActionActionable(storedElement))
        {
            return ComputerUseWinSecondaryActionResolution.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.UnsupportedAction,
                    $"elementIndex {elementIndex} не является supported secondary semantic target в последнем get_app_state."));
        }

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
                return ComputerUseWinSecondaryActionResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.ObservationFailed,
                        snapshot.Reason ?? "Computer Use for Windows не смог пере-подтвердить target для secondary semantic path."));
            }

            IReadOnlyDictionary<int, ComputerUseWinStoredElement> freshElements = ComputerUseWinAccessibilityProjector.Flatten(snapshot.Root);
            if (!ComputerUseWinFreshElementResolver.TryResolve(freshElements, storedElement, out ComputerUseWinStoredElement? freshElement)
                || freshElement is null)
            {
                return ComputerUseWinSecondaryActionResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.StaleState,
                        "elementIndex из stateToken больше не удаётся доказуемо сопоставить с текущим secondary semantic target."));
            }

            if (!ComputerUseWinActionability.IsPerformSecondaryActionActionable(freshElement)
                || !TryResolveActionKind(freshElement.Patterns, out string? actionKind))
            {
                return ComputerUseWinSecondaryActionResolution.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        $"elementIndex {elementIndex} больше не имеет supported secondary semantic affordance в текущем live UI state."));
            }

            bool isRisky = ComputerUseWinTargetPolicy.RequiresRiskConfirmation(freshElement, ToolNames.ComputerUseWinPerformSecondaryAction);

            return ComputerUseWinSecondaryActionResolution.Success(freshElement, actionKind!, isRisky);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ComputerUseWinSecondaryActionResolution.Failure(
                ComputerUseWinObservationFailureTranslator.Translate(
                    exception,
                    "Computer Use for Windows не смог пере-подтвердить target для secondary semantic path."));
        }
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
