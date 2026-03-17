using Windows.Graphics;

namespace WinBridge.Runtime.Windows.Capture;

internal enum WgcFrameSizingDecision
{
    Accept,
    RecreateAndRetry,
    Fail,
}

internal readonly record struct WgcFrameSize(int Width, int Height)
{
    public bool IsValid =>
        Width > 0 && Height > 0;

    public SizeInt32 ToSizeInt32() =>
        new()
        {
            Width = Width,
            Height = Height,
        };

    public static WgcFrameSize FromSizeInt32(SizeInt32 size) =>
        new(size.Width, size.Height);

    public override string ToString() =>
        $"{Width}x{Height}";
}

internal static class WgcFrameSizingPolicy
{
    public static WgcFrameSizingDecision Evaluate(
        WgcFrameSize expectedSize,
        WgcFrameSize contentSize,
        bool recreateAttempted)
    {
        if (!contentSize.IsValid)
        {
            return WgcFrameSizingDecision.Fail;
        }

        if (contentSize == expectedSize)
        {
            return WgcFrameSizingDecision.Accept;
        }

        return recreateAttempted
            ? WgcFrameSizingDecision.Fail
            : WgcFrameSizingDecision.RecreateAndRetry;
    }
}
