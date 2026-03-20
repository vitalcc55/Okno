using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Waiting;

public interface IWaitService
{
    Task<WaitResult> WaitAsync(
        WindowDescriptor targetWindow,
        WaitRequest request,
        CancellationToken cancellationToken);
}
