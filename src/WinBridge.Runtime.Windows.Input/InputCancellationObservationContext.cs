namespace WinBridge.Runtime.Windows.Input;

internal enum InputCancellationObservationContext
{
    InFlightAction,
    BetweenActions,
    AfterBatchCompletedBeforeSuccessReturn,
}
