using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public interface IWaitVisualProbe
{
    Task<WaitVisualSample> CaptureVisualSampleAsync(
        WindowDescriptor targetWindow,
        CancellationToken cancellationToken);

    Task WriteVisualEvidenceAsync(
        WaitVisualEvidenceFrame frame,
        string path,
        CancellationToken cancellationToken);
}
