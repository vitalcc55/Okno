using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputPointerDispatchPlan(
    InputAction Action,
    InputPoint ResolvedScreenPoint);
