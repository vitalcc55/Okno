// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinPointContract
{
    public static string? Validate(InputPoint? point, string parameterName)
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

    public static JsonObject CreateRequiredSchema() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "integer" },
                ["y"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = new JsonArray("x", "y"),
            ["additionalProperties"] = false,
        };
}
