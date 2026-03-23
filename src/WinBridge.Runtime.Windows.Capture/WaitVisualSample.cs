using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public sealed record WaitVisualSample(
    WindowDescriptor Window,
    int PixelWidth,
    int PixelHeight,
    WaitVisualComparisonData ComparisonData,
    WaitVisualEvidenceFrame? EvidenceFrame);
