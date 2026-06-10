// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinSemanticTargetModeValues
{
    public const string ElementIndex = "element_index";
    public const string Selector = "selector";
}

internal static class ComputerUseWinSemanticSelectorContract
{
    private const string NonBlankJsonStringPattern = @".*\S.*";

    public static bool TryValidateElementIndexOrSelector(
        int? elementIndex,
        WaitElementSelector? selector,
        string toolName,
        out string? failure)
    {
        bool hasElementIndex = elementIndex is not null;
        bool hasSelector = selector is not null;
        if (hasElementIndex == hasSelector)
        {
            failure = $"Для {toolName} нужно передать ровно один semantic target: elementIndex или selector.";
            return false;
        }

        if (elementIndex is < 1)
        {
            failure = $"Параметр elementIndex для {toolName} должен быть >= 1, если он передан.";
            return false;
        }

        if (hasSelector && !ElementSelectorPolicy.HasCriteria(selector))
        {
            failure = $"Параметр selector для {toolName} должен содержать хотя бы одно поле: name, automationId или controlType.";
            return false;
        }

        failure = null;
        return true;
    }

    public static string ResolveTargetMode(int? elementIndex, WaitElementSelector? selector)
    {
        if (selector is not null)
        {
            return ComputerUseWinSemanticTargetModeValues.Selector;
        }

        return elementIndex is not null
            ? ComputerUseWinSemanticTargetModeValues.ElementIndex
            : "unknown";
    }

    public static JsonObject CreateSelectorSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["anyOf"] = new JsonArray
            {
                new JsonObject { ["required"] = CreateStringArray("name") },
                new JsonObject { ["required"] = CreateStringArray("automationId") },
                new JsonObject { ["required"] = CreateStringArray("controlType") },
            },
            ["properties"] = new JsonObject
            {
                ["name"] = CreateNonBlankNullableStringSchema(),
                ["automationId"] = CreateNonBlankNullableStringSchema(),
                ["controlType"] = CreateNonBlankNullableStringSchema(),
            },
        };

    public static JsonArray CreateElementIndexOrSelectorModeSchema() =>
        new()
        {
            new JsonObject
            {
                ["required"] = CreateStringArray("elementIndex"),
                ["properties"] = new JsonObject
                {
                    ["elementIndex"] = new JsonObject
                    {
                        ["type"] = "integer",
                    },
                },
            },
            new JsonObject
            {
                ["required"] = CreateStringArray("selector"),
                ["properties"] = new JsonObject
                {
                    ["selector"] = CreateSelectorSchema(),
                },
            },
        };

    private static JsonObject CreateNonBlankNullableStringSchema() =>
        new()
        {
            ["type"] = new JsonArray("string", "null"),
            ["pattern"] = NonBlankJsonStringPattern,
        };

    private static JsonArray CreateStringArray(params string[] values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
