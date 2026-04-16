using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputActionExecutionState(
    int ActionIndex,
    InputAction Action,
    InputIrreversiblePhase Phase,
    InputPoint? ResolvedScreenPoint,
    string? EffectiveButton,
    long? TargetHwnd);
