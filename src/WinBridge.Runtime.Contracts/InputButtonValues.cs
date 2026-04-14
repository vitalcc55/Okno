namespace WinBridge.Runtime.Contracts;

public static class InputButtonValues
{
    public const string Left = "left";
    public const string Right = "right";
    public const string Middle = "middle";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Left,
            Right,
            Middle,
        };
}
