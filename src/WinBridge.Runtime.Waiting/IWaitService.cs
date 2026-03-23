using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Waiting;

public interface IWaitService
{
    Task<WaitResult> WaitAsync(
        WaitTargetResolution target,
        WaitRequest request,
        CancellationToken cancellationToken);
}
