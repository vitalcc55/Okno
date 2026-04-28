using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputDragDispatchPlan(
    InputAction Action,
    IReadOnlyList<InputPoint> ResolvedScreenPath);
