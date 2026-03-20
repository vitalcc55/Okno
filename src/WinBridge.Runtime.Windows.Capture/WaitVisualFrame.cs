using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

public sealed record WaitVisualFrame(
    WindowDescriptor Window,
    int PixelWidth,
    int PixelHeight,
    int RowStride,
    byte[] PixelBytes);
