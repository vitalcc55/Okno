using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinRequestContractValidator
{
    public static string? Validate<T>(T request) =>
        request switch
        {
            ComputerUseWinGetAppStateRequest value => Validate(value),
            ComputerUseWinClickRequest value => Validate(value),
            ComputerUseWinTypeTextRequest value => Validate(value),
            ComputerUseWinPressKeyRequest value => Validate(value),
            ComputerUseWinScrollRequest value => Validate(value),
            ComputerUseWinDragRequest value => Validate(value),
            _ => null,
        };

    private static string? Validate(ComputerUseWinGetAppStateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AppId) && request.Hwnd is not null)
        {
            return "Для get_app_state нужно передать либо appId, либо hwnd, но не оба селектора сразу.";
        }

        if (request.MaxNodes < 1 || request.MaxNodes > UiaSnapshotRequestValidator.MaxNodesCeiling)
        {
            return $"Параметр maxNodes для get_app_state должен быть в диапазоне 1..{UiaSnapshotRequestValidator.MaxNodesCeiling}.";
        }

        return null;
    }

    private static string? Validate(ComputerUseWinClickRequest request) =>
        ComputerUseWinClickContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinTypeTextRequest request) => null;

    private static string? Validate(ComputerUseWinPressKeyRequest request)
    {
        if (request.Repeat is < 1)
        {
            return "Параметр repeat для press_key должен быть >= 1.";
        }

        return null;
    }

    private static string? Validate(ComputerUseWinScrollRequest request)
    {
        string? pointFailure = ValidatePoint(request.Point, "point");
        if (pointFailure is not null)
        {
            return pointFailure;
        }

        if (request.Pages is < 1)
        {
            return "Параметр pages для scroll должен быть >= 1.";
        }

        return null;
    }

    private static string? Validate(ComputerUseWinDragRequest request) =>
        ValidatePoint(request.FromPoint, "fromPoint")
        ?? ValidatePoint(request.ToPoint, "toPoint");

    private static string? ValidatePoint(InputPoint? point, string parameterName)
    {
        if (point is null)
        {
            return null;
        }

        if (!point.HasValidObject)
        {
            return $"Параметр {parameterName} должен быть JSON object с integer x/y.";
        }

        if (!point.HasX || !point.HasValidX || !point.HasY || !point.HasValidY)
        {
            return $"Параметр {parameterName} должен содержать integer поля x и y.";
        }

        if (point.AdditionalProperties is { Count: > 0 })
        {
            string unexpectedKeys = string.Join(", ", point.AdditionalProperties.Keys.OrderBy(static key => key, StringComparer.Ordinal));
            return $"Параметр {parameterName} не поддерживает дополнительные поля: {unexpectedKeys}.";
        }

        return null;
    }
}
