using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Launch;

public interface IOpenTargetService
{
    Task<OpenTargetResult> OpenAsync(OpenTargetRequest request, CancellationToken cancellationToken);
}
