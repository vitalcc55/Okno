using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public interface IWaitVisualProbe
{
    Task<WaitVisualFrame> CaptureVisualAsync(
        WindowDescriptor targetWindow,
        CancellationToken cancellationToken);

    Task WriteVisualArtifactAsync(
        WaitVisualFrame frame,
        string path,
        CancellationToken cancellationToken);
}
