using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowActivationService
{
    Task<ActivateWindowResult> ActivateAsync(long hwnd, CancellationToken cancellationToken);
}
