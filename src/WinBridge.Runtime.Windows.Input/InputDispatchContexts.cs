using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputDispatchResult(
    bool Success,
    bool CommittedSideEffects = false,
    string? FailureCode = null,
    string? Reason = null,
    string? FailureStageHint = null);

internal sealed record InputTextDispatchContext(
    string Text,
    WindowDescriptor AdmittedTargetWindow);

internal sealed record InputKeypressDispatchContext(
    string Key,
    int Repeat,
    WindowDescriptor AdmittedTargetWindow);

internal sealed record InputScrollDispatchContext(
    InputPoint ExpectedScreenPoint,
    string Direction,
    int Delta,
    WindowDescriptor AdmittedTargetWindow);

internal sealed record InputDragDispatchContext(
    IReadOnlyList<InputPoint> ScreenPath,
    WindowDescriptor AdmittedTargetWindow);
