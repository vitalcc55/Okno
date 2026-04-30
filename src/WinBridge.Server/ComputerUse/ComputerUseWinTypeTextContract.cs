using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinTypeTextPayload(
    string Text,
    int TextLength,
    string TextBucket,
    bool ContainsNewline,
    bool WhitespaceOnly);

internal static class ComputerUseWinTypeTextContract
{
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
