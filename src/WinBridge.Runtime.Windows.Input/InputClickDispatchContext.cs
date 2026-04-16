using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputClickDispatchContext(
    InputPoint ExpectedScreenPoint,
    string LogicalButton,
    WindowDescriptor AdmittedTargetWindow);
