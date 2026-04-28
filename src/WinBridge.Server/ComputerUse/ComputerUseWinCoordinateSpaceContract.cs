using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinCoordinateSpaceContract
{
    public static bool TryNormalize(
        string? rawValue,
        string parameterName,
        IEnumerable<string> allowedValues,
        string? defaultValue,
        out string? normalizedValue,
        out string? failure)
    {
        normalizedValue = defaultValue;

        if (rawValue is null)
        {
            failure = null;
            return true;
        }

        string trimmedValue = rawValue.Trim();
        if (trimmedValue.Length == 0)
        {
            failure = $"Параметр {parameterName} не поддерживает пустую строку.";
            return false;
        }

        if (!allowedValues.Contains(trimmedValue, StringComparer.Ordinal))
        {
            failure = $"Параметр {parameterName} использует неподдерживаемое значение '{trimmedValue}'.";
            return false;
        }

        normalizedValue = trimmedValue;
        failure = null;
        return true;
    }

    public static string DetermineValidationModeCoordinateSpace(
        string? rawValue,
        IEnumerable<string> allowedValues,
        string defaultValue)
    {
        return TryNormalize(
            rawValue,
            "coordinateSpace",
            allowedValues,
            defaultValue,
            out string? normalizedValue,
            out _)
            ? normalizedValue!
            : InputCoordinateSpaceValues.Screen;
    }
}
