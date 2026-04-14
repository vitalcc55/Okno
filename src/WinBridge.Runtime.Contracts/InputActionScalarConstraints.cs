namespace WinBridge.Runtime.Contracts;

public static class InputActionScalarConstraints
{
    public const int MinimumRepeat = 1;
    public const int InvalidScrollDelta = 0;
    public const int MinimumCapturePixelDimension = 1;
    public const string NonWhitespacePattern = @"\S";

    public static bool HasNonWhitespace(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}
