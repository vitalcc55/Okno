using System.Diagnostics.CodeAnalysis;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputCancellationMaterializationPolicy
{
    public static bool TryCreate(
        InputCommittedSideEffectContext? committedContext,
        InputCancellationObservationContext observationContext,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out InputCancellationMaterializationDecision? decision)
    {
        if (committedContext is null || !cancellationToken.IsCancellationRequested)
        {
            decision = null;
            return false;
        }

        bool currentActionStillInFlight = observationContext == InputCancellationObservationContext.InFlightAction;
        decision = new(
            ShouldAppendFailedAction: currentActionStillInFlight,
            FailedActionIndex: currentActionStillInFlight ? committedContext.ActionIndex : null,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: currentActionStillInFlight
                ? CreateInFlightReason(committedContext)
                : observationContext == InputCancellationObservationContext.AfterBatchCompletedBeforeSuccessReturn
                    ? "Input execution was cancelled after the full batch had already completed; runtime returns factual committed state instead of verify_needed."
                    : "Input execution was cancelled after previously committed pointer side effects; the next action was not started.");
        return true;
    }

    private static string CreateInFlightReason(InputCommittedSideEffectContext committedContext) =>
        committedContext.Phase switch
        {
            InputIrreversiblePhase.AfterMove when string.Equals(committedContext.Action.Type, InputActionTypeValues.Move, StringComparison.Ordinal)
                => "Input execution was cancelled after pointer move had already been applied.",
            InputIrreversiblePhase.AfterMove
                => "Input execution was cancelled after pointer move had already been applied; click dispatch was not executed.",
            InputIrreversiblePhase.AfterClickTap
                => "Input execution was cancelled after click dispatch had already been executed.",
            InputIrreversiblePhase.AfterDoubleClickFirstTap
                => "Input execution was cancelled after the first double_click tap had already been dispatched; second tap was not executed.",
            InputIrreversiblePhase.AfterDoubleClickSecondTap
                => "Input execution was cancelled after both double_click taps had already been dispatched.",
            InputIrreversiblePhase.AfterTypeTextDispatch
                => "Input execution was cancelled after text dispatch had already been executed; retrying without verification may duplicate or corrupt the field contents.",
            InputIrreversiblePhase.AfterKeypressDispatch
                => "Input execution was cancelled after keypress dispatch had already been executed; retrying without verification may repeat the shortcut or key effect.",
            _ => "Input execution was cancelled after pointer side effects had already started for the current action.",
        };
}
