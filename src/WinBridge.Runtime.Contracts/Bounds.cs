namespace WinBridge.Runtime.Contracts;

public sealed record Bounds
{
    public Bounds(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; init; }

    public int Top { get; init; }

    public int Right { get; init; }

    public int Bottom { get; init; }

    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
