using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal interface IInputService
{
    Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken);
}
