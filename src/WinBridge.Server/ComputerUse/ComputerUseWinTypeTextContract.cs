using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinTypeTextPayload(
    string Text,
    InputPoint? Point,
    string? CoordinateSpace,
    int TextLength,
    string TextBucket,
    bool ContainsNewline,
    bool WhitespaceOnly)
{
    public bool UsesCoordinateConfirmedFallback => Point is not null;
}

internal static class ComputerUseWinTypeTextContract
{
    internal static IReadOnlyList<string> AllowedCoordinateSpaceValues { get; } =
        [InputCoordinateSpaceValues.CapturePixels];

    public static string? ValidateRequest(ComputerUseWinTypeTextRequest request) =>
        TryParse(request, out _, out string? failure) ? null : failure;

    public static bool TryParse(
        ComputerUseWinTypeTextRequest request,
        out ComputerUseWinTypeTextPayload? payload,
        out string? failure)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(request.StateToken))
        {
            failure = "Параметр stateToken обязателен для type_text.";
            return false;
        }

        if (request.ElementIndex is < 1)
        {
            failure = "Параметр elementIndex для type_text должен быть >= 1, если он передан.";
            return false;
        }

        string? pointFailure = ComputerUseWinPointContract.Validate(request.Point, "point");
        if (pointFailure is not null)
        {
            failure = pointFailure;
            return false;
        }

        if (request.Point is not null && request.CoordinateSpace is string rawCoordinateSpace)
        {
            string coordinateSpaceValue = rawCoordinateSpace.Trim();
            if (coordinateSpaceValue.Length > 0
                && !string.Equals(coordinateSpaceValue, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
            {
                failure = "Coordinate-confirmed type_text fallback поддерживает только coordinateSpace=capture_pixels.";
                return false;
            }
        }

        if (!ComputerUseWinCoordinateSpaceContract.TryNormalize(
                request.CoordinateSpace,
                "coordinateSpace",
                AllowedCoordinateSpaceValues,
                request.Point is null ? null : InputCoordinateSpaceValues.CapturePixels,
                out string? coordinateSpace,
                out string? coordinateSpaceFailure))
        {
            failure = coordinateSpaceFailure;
            return false;
        }

        if (request.ElementIndex is not null && request.Point is not null)
        {
            failure = "Для type_text нужно передать либо elementIndex, либо point, но не оба селектора сразу.";
            return false;
        }

        if (request.Point is null && request.CoordinateSpace is not null)
        {
            failure = "Параметр coordinateSpace для type_text допустим только вместе с point.";
            return false;
        }

        if (request.Point is not null && !request.AllowFocusedFallback)
        {
            failure = "Coordinate-confirmed type_text fallback требует allowFocusedFallback=true.";
            return false;
        }

        if (request.Text is null || request.Text.Length == 0)
        {
            failure = "Параметр text обязателен для type_text и не должен быть пустой строкой.";
            return false;
        }

        if (request.AllowFocusedFallback && !request.Confirm)
        {
            failure = "Параметр allowFocusedFallback для type_text требует confirm=true.";
            return false;
        }

        payload = new(
            Text: request.Text,
            Point: request.Point,
            CoordinateSpace: coordinateSpace,
            TextLength: request.Text.Length,
            TextBucket: ClassifyTextBucket(request.Text.Length),
            ContainsNewline: request.Text.Contains('\r') || request.Text.Contains('\n'),
            WhitespaceOnly: request.Text.All(char.IsWhiteSpace));
        failure = null;
        return true;
    }

    private static string ClassifyTextBucket(int valueLength) =>
        valueLength switch
        {
            <= 16 => "short",
            <= 64 => "medium",
            _ => "long",
        };
}
