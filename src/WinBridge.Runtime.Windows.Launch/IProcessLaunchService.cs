using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Launch;

public interface IProcessLaunchService
{
    Task<LaunchProcessResult> LaunchAsync(LaunchProcessRequest request, CancellationToken cancellationToken);
}
