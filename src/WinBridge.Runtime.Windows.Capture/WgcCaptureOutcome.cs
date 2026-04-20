using Windows.Graphics.Imaging;

namespace WinBridge.Runtime.Windows.Capture;

internal sealed class WgcCaptureOutcome : IDisposable
{
    private SoftwareBitmap? _bitmap;

    public WgcCaptureOutcome(SoftwareBitmap bitmap, WgcFrameSize acceptedContentSize)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        AcceptedContentSize = acceptedContentSize;
    }

    public SoftwareBitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(WgcCaptureOutcome));

    public WgcFrameSize AcceptedContentSize { get; }

    public SoftwareBitmap DetachBitmap()
    {
        SoftwareBitmap bitmap = Bitmap;
        _bitmap = null;
        return bitmap;
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
