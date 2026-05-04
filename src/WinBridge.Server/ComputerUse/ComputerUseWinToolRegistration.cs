// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinToolRegistration
{
    public static IReadOnlyList<McpServerTool> Create(Func<ComputerUseWinTools> getTools)
    {
        ArgumentNullException.ThrowIfNull(getTools);
        return
        [
            CreateListAppsTool(getTools),
            CreateGetAppStateTool(getTools),
            CreateClickTool(getTools),
            CreateDragTool(getTools),
            CreatePerformSecondaryActionTool(getTools),
            CreatePressKeyTool(getTools),
            CreateScrollTool(getTools),
            CreateSetValueTool(getTools),
            CreateTypeTextTool(getTools),
        ];
    }

    private static McpServerTool CreateListAppsTool(Func<ComputerUseWinTools> getTools) =>
        McpServerTool.Create(
            (Func<CallToolResult>)(() => getTools().ListApps()),
            new McpServerToolCreateOptions
            {
                Name = ToolNames.ComputerUseWinListApps,
                Description = ToolDescriptions.ComputerUseWinListAppsTool,
                ReadOnly = false,
                Destructive = true,
                Idempotent = false,
                OpenWorld = true,
                UseStructuredContent = true,
            });

    private static McpServerTool CreateGetAppStateTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = McpServerTool.Create(
            (Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>)((requestContext, cancellationToken) =>
                new(getTools().GetAppState(requestContext, cancellationToken))),
            new McpServerToolCreateOptions
            {
                Name = ToolNames.ComputerUseWinGetAppState,
                Description = ToolDescriptions.ComputerUseWinGetAppStateTool,
                ReadOnly = false,
                Destructive = false,
                Idempotent = false,
                OpenWorld = true,
                UseStructuredContent = true,
            });
        tool.ProtocolTool.InputSchema = CreateGetAppStateSchema();
        return tool;
    }

    private static McpServerTool CreateClickTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinClick,
            ToolDescriptions.ComputerUseWinClickTool,
            static (tools, requestContext, cancellationToken) => tools.Click(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreateClickSchema();
        return tool;
    }

    private static McpServerTool CreateDragTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinDrag,
            ToolDescriptions.ComputerUseWinDragTool,
            static (tools, requestContext, cancellationToken) => tools.Drag(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreateDragSchema();
        return tool;
    }

    private static McpServerTool CreatePressKeyTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinPressKey,
            ToolDescriptions.ComputerUseWinPressKeyTool,
            static (tools, requestContext, cancellationToken) => tools.PressKey(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreatePressKeySchema();
        return tool;
    }

    private static McpServerTool CreatePerformSecondaryActionTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinPerformSecondaryAction,
            ToolDescriptions.ComputerUseWinPerformSecondaryActionTool,
            static (tools, requestContext, cancellationToken) => tools.PerformSecondaryAction(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreatePerformSecondaryActionSchema();
        return tool;
    }

    private static McpServerTool CreateSetValueTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinSetValue,
            ToolDescriptions.ComputerUseWinSetValueTool,
            static (tools, requestContext, cancellationToken) => tools.SetValue(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreateSetValueSchema();
        return tool;
    }

    private static McpServerTool CreateScrollTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinScroll,
            ToolDescriptions.ComputerUseWinScrollTool,
            static (tools, requestContext, cancellationToken) => tools.Scroll(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreateScrollSchema();
        return tool;
    }

    private static McpServerTool CreateTypeTextTool(Func<ComputerUseWinTools> getTools)
    {
        McpServerTool tool = CreateActionTool(
            getTools,
            ToolNames.ComputerUseWinTypeText,
            ToolDescriptions.ComputerUseWinTypeTextTool,
            static (tools, requestContext, cancellationToken) => tools.TypeText(requestContext, cancellationToken));
        tool.ProtocolTool.InputSchema = CreateTypeTextSchema();
        return tool;
    }

    private static McpServerTool CreateActionTool(
        Func<ComputerUseWinTools> getTools,
        string name,
        string description,
        Func<ComputerUseWinTools, RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResult>> handler)
    {
        return McpServerTool.Create(
            (Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>)((requestContext, cancellationToken) =>
                new(handler(getTools(), requestContext, cancellationToken))),
            new McpServerToolCreateOptions
            {
                Name = name,
                Description = description,
                ReadOnly = false,
                Destructive = true,
                Idempotent = false,
                OpenWorld = true,
                UseStructuredContent = true,
            });
    }

    private static JsonElement CreateGetAppStateSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["not"] = new JsonObject
                {
                    ["required"] = CreateStringArray("windowId", "hwnd"),
                    ["properties"] = new JsonObject
                    {
                        ["windowId"] = new JsonObject { ["type"] = "string" },
                        ["hwnd"] = new JsonObject { ["type"] = "integer" },
                    },
                },
                ["properties"] = new JsonObject
                {
                    ["windowId"] = CreateNullableNonBlankStringSchema(),
                    ["hwnd"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                    ["maxNodes"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = UiaSnapshotRequestValidator.MaxNodesCeiling },
                },
            });

    private static JsonElement CreateClickSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken"),
                ["oneOf"] = ComputerUseWinClickContract.CreateSelectorModeSchema(),
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["elementIndex"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                    ["point"] = CreatePointSchema(),
                    ["coordinateSpace"] = CreateNullableStringEnumSchema(ComputerUseWinClickContract.AllowedCoordinateSpaceValues),
                    ["button"] = CreateNullableStringEnumSchema(ComputerUseWinClickContract.AllowedButtonValues),
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                    ["observeAfter"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonObject CreatePointSchema(string propertyName = "point") =>
        ComputerUseWinPointContract.CreateRequiredSchema();

    private static JsonArray CreateTypeSet(params string[] values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray CreateStringArray(params string[] values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonObject CreateNullableStringEnumSchema(IReadOnlyList<string> values)
    {
        JsonArray enumValues = [];
        foreach (string value in values)
        {
            enumValues.Add(value);
        }

        enumValues.Add(null);
        return new JsonObject
        {
            ["type"] = CreateTypeSet("string", "null"),
            ["enum"] = enumValues,
        };
    }

    private static JsonObject CreateNullableNonBlankStringSchema() =>
        new()
        {
            ["type"] = CreateTypeSet("string", "null"),
            ["pattern"] = @".*\S.*",
        };

    private static JsonElement CreatePressKeySchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken", "key"),
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["key"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                        ["pattern"] = @".*\S.*",
                    },
                    ["repeat"] = new JsonObject
                    {
                        ["type"] = CreateTypeSet("integer", "null"),
                        ["minimum"] = 1,
                        ["maximum"] = InputActionScalarConstraints.MaximumKeypressRepeat,
                    },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                    ["observeAfter"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateSetValueSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken", "elementIndex", "valueKind"),
                ["not"] = new JsonObject
                {
                    ["required"] = CreateStringArray("textValue", "numberValue"),
                },
                ["oneOf"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["valueKind"] = new JsonObject { ["const"] = UiaSetValueKindValues.Text },
                        },
                        ["required"] = CreateStringArray("textValue"),
                    },
                    new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["valueKind"] = new JsonObject { ["const"] = UiaSetValueKindValues.Number },
                        },
                        ["required"] = CreateStringArray("numberValue"),
                    },
                },
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["elementIndex"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 1,
                    },
                    ["valueKind"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = CreateStringArray(UiaSetValueKindValues.Text, UiaSetValueKindValues.Number),
                    },
                    ["textValue"] = new JsonObject { ["type"] = "string" },
                    ["numberValue"] = new JsonObject { ["type"] = "number" },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateTypeTextSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken", "text"),
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["elementIndex"] = new JsonObject
                    {
                        ["type"] = CreateTypeSet("integer", "null"),
                        ["minimum"] = 1,
                    },
                    ["point"] = CreatePointSchema(),
                    ["coordinateSpace"] = CreateNullableStringEnumSchema(ComputerUseWinTypeTextContract.AllowedCoordinateSpaceValues),
                    ["text"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                    },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                    ["allowFocusedFallback"] = new JsonObject { ["type"] = "boolean" },
                    ["observeAfter"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateScrollSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken", "direction"),
                ["oneOf"] = ComputerUseWinScrollContract.CreateSelectorModeSchema(),
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["elementIndex"] = new JsonObject
                    {
                        ["type"] = CreateTypeSet("integer", "null"),
                        ["minimum"] = 1,
                    },
                    ["point"] = CreatePointSchema(),
                    ["coordinateSpace"] = CreateNullableStringEnumSchema(
                        [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels]),
                    ["direction"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = CreateStringArray(
                            UiaScrollDirectionValues.Up,
                            UiaScrollDirectionValues.Down,
                            UiaScrollDirectionValues.Left,
                            UiaScrollDirectionValues.Right),
                    },
                    ["pages"] = new JsonObject
                    {
                        ["type"] = CreateTypeSet("integer", "null"),
                        ["minimum"] = 1,
                        ["maximum"] = InputActionScalarConstraints.MaximumScrollPages,
                    },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                    ["observeAfter"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateDragSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken"),
                ["allOf"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["oneOf"] = ComputerUseWinDragContract.CreateSourceSelectorModeSchema(),
                    },
                    new JsonObject
                    {
                        ["oneOf"] = ComputerUseWinDragContract.CreateDestinationSelectorModeSchema(),
                    },
                },
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["fromElementIndex"] = new JsonObject
                    {
                        ["type"] = CreateTypeSet("integer", "null"),
                        ["minimum"] = 1,
                    },
                    ["fromPoint"] = CreatePointSchema("fromPoint"),
                    ["toElementIndex"] = new JsonObject
                    {
                        ["type"] = CreateTypeSet("integer", "null"),
                        ["minimum"] = 1,
                    },
                    ["toPoint"] = CreatePointSchema("toPoint"),
                    ["coordinateSpace"] = CreateNullableStringEnumSchema(
                        [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels]),
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                    ["observeAfter"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreatePerformSecondaryActionSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = CreateStringArray("stateToken", "elementIndex"),
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = ComputerUseWinClickContract.CreateRequiredStateTokenSchema(),
                    ["elementIndex"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 1,
                    },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement ParseSchema(JsonObject schema)
    {
        using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }
}
