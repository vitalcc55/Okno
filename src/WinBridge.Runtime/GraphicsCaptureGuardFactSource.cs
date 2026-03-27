using Windows.Graphics.Capture;
using WinBridge.Runtime.Guards;

namespace WinBridge.Runtime;

internal sealed class GraphicsCaptureGuardFactSource : ICaptureGuardFactSource
{
    public CaptureCapabilityProbeResult GetFacts() =>
        new(
            FactResolved: true,
            WindowsGraphicsCaptureSupported: GraphicsCaptureSession.IsSupported());
}
