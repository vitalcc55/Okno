// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
            ComputerUseWinSetValueRequest value => Validate(value),
            ComputerUseWinScrollRequest value => Validate(value),
            ComputerUseWinPerformSecondaryActionRequest value => Validate(value),
            ComputerUseWinDragRequest value => Validate(value),
            _ => null,
        };

    private static string? Validate(ComputerUseWinGetAppStateRequest request)
    {
        string? windowIdFailure = ValidateOptionalNonBlankString(request.WindowId, "windowId");
        if (windowIdFailure is not null)
        {
            return windowIdFailure;
        }

        if (!string.IsNullOrWhiteSpace(request.WindowId) && request.Hwnd is not null)
        {
            return "Для get_app_state нужно передать либо windowId, либо hwnd, но не оба селектора сразу.";
        }

        if (request.MaxNodes < 1 || request.MaxNodes > UiaSnapshotRequestValidator.MaxNodesCeiling)
        {
            return $"Параметр maxNodes для get_app_state должен быть в диапазоне 1..{UiaSnapshotRequestValidator.MaxNodesCeiling}.";
        }

        return null;
    }

    private static string? Validate(ComputerUseWinClickRequest request) =>
        ComputerUseWinClickContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinTypeTextRequest request) =>
        ComputerUseWinTypeTextContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinPressKeyRequest request) =>
        ComputerUseWinPressKeyContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinSetValueRequest request) =>
        ComputerUseWinSetValueContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinScrollRequest request) =>
        ComputerUseWinScrollContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinPerformSecondaryActionRequest request) =>
        ComputerUseWinPerformSecondaryActionContract.ValidateRequest(request);

    private static string? Validate(ComputerUseWinDragRequest request) =>
        ComputerUseWinDragContract.ValidateRequest(request);

    private static string? ValidateOptionalNonBlankString(string? value, string parameterName) =>
        value is not null && string.IsNullOrWhiteSpace(value)
            ? $"Параметр {parameterName} не поддерживает пустое или whitespace-only значение."
            : null;
}
