// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinSetValuePayload(
    string ValueKind,
    string? TextValue,
    double? NumberValue,
    int? ValueLength,
    string ValueBucket);

internal static class ComputerUseWinSetValueContract
{
    public static string? ValidateRequest(ComputerUseWinSetValueRequest request) =>
        TryParse(request, out _, out string? failure) ? null : failure;

    public static bool TryParse(
        ComputerUseWinSetValueRequest request,
        out ComputerUseWinSetValuePayload? payload,
        out string? failure)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(request.StateToken))
        {
            failure = "Параметр stateToken обязателен для set_value.";
            return false;
        }

        if (request.ElementIndex is null or < 1)
        {
            failure = "Параметр elementIndex обязателен для set_value и должен быть >= 1.";
            return false;
        }

        string? valueKind = NormalizeValueKind(request.ValueKind);
        if (valueKind is null)
        {
            failure = "Параметр valueKind для set_value должен быть `text` или `number`.";
            return false;
        }

        if (string.Equals(valueKind, UiaSetValueKindValues.Text, StringComparison.Ordinal))
        {
            if (request.TextValue is null || request.NumberValue is not null)
            {
                failure = "Для set_value(valueKind=text) нужно передать только textValue.";
                return false;
            }

            payload = new(
                ValueKind: valueKind,
                TextValue: request.TextValue,
                NumberValue: null,
                ValueLength: request.TextValue.Length,
                ValueBucket: ClassifyTextBucket(request.TextValue.Length));
            failure = null;
            return true;
        }

        if (request.NumberValue is null || request.TextValue is not null)
        {
            failure = "Для set_value(valueKind=number) нужно передать только numberValue.";
            return false;
        }

        payload = new(
            ValueKind: valueKind,
            TextValue: null,
            NumberValue: request.NumberValue,
            ValueLength: null,
            ValueBucket: Math.Abs(request.NumberValue.Value % 1d) <= 0.000001d ? "integer" : "fractional");
        failure = null;
        return true;
    }

    private static string? NormalizeValueKind(string? valueKind)
    {
        string normalized = valueKind?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            UiaSetValueKindValues.Text => UiaSetValueKindValues.Text,
            UiaSetValueKindValues.Number => UiaSetValueKindValues.Number,
            _ => null,
        };
    }

    private static string ClassifyTextBucket(int valueLength) =>
        valueLength switch
        {
            0 => "empty",
            <= 16 => "short",
            <= 64 => "medium",
            _ => "long",
        };
}
