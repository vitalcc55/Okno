namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputCancellationMaterializationDecision(
    bool ShouldAppendFailedAction,
    int? FailedActionIndex,
    string FailureCode,
    string Reason);
