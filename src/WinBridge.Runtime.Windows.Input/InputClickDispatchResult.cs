namespace WinBridge.Runtime.Windows.Input;

internal enum InputClickDispatchOutcomeKind
{
    Success,
    CleanFailure,
    PartialDispatchCompensated,
    PartialDispatchUncompensated,
}

internal sealed record InputClickDispatchResult(
    bool Success,
    InputClickDispatchOutcomeKind OutcomeKind = InputClickDispatchOutcomeKind.Success,
    string? FailureCode = null,
    string? Reason = null);
