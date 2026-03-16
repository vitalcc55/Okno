using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowActivationService
{
    Task<ActivateWindowResult> ActivateAsync(WindowDescriptor targetWindow, CancellationToken cancellationToken);
}
