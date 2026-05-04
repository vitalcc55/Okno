// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace WinBridge.Runtime.Contracts;

internal static class InputJsonBindingHelpers
{
    public static JsonElement CloneValue(ref Utf8JsonReader reader)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public static void SkipNestedValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            using JsonDocument _ = JsonDocument.ParseValue(ref reader);
        }
    }
}
