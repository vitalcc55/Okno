namespace WinBridge.Runtime.Contracts;

public static class InputModifierKeyValues
{
    public const string Ctrl = "ctrl";
    public const string Alt = "alt";
    public const string Shift = "shift";
    public const string Win = "win";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Ctrl,
            Alt,
            Shift,
            Win,
        };
}
