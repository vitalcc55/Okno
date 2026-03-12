namespace WinBridge.Runtime.Contracts;

public sealed record Bounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
