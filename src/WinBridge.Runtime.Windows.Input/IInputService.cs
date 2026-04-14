using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

public interface IInputService
{
    Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken);
}
