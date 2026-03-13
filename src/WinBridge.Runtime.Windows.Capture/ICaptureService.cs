using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public interface ICaptureService
{
    Task<CaptureResult> CaptureAsync(CaptureTarget target, CancellationToken cancellationToken);
}
