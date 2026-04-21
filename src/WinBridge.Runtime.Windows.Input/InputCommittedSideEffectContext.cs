using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputCommittedSideEffectContext(
    int ActionIndex,
    InputAction Action,
    InputIrreversiblePhase Phase,
    InputPoint? ResolvedScreenPoint,
    string? Button,
    long? TargetHwnd);
