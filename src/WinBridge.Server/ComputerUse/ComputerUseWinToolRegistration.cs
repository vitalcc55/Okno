using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

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
        ];
    }

    private static McpServerTool CreateListAppsTool(Func<ComputerUseWinTools> getTools) =>
        McpServerTool.Create(
            (Func<CallToolResult>)(() => getTools().ListApps()),
            new McpServerToolCreateOptions
            {
                Name = ToolNames.ComputerUseWinListApps,
                Description = ToolDescriptions.ComputerUseWinListAppsTool,
                ReadOnly = true,
                Destructive = false,
                Idempotent = true,
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
                ReadOnly = true,
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
                ["properties"] = new JsonObject
                {
                    ["appId"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
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
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["elementIndex"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                    ["point"] = CreatePointSchema(),
                    ["coordinateSpace"] = CreateNullableStringEnumSchema(ComputerUseWinClickContract.AllowedCoordinateSpaceValues),
                    ["button"] = CreateNullableStringEnumSchema(ComputerUseWinClickContract.AllowedButtonValues),
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateTypeTextSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["text"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreatePressKeySchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["key"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["repeat"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null"), ["minimum"] = 1 },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateScrollSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["elementIndex"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                    ["point"] = CreatePointSchema(),
                    ["coordinateSpace"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["direction"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["pages"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null"), ["minimum"] = 1 },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonElement CreateDragSchema() =>
        ParseSchema(
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["stateToken"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["fromElementIndex"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                    ["fromPoint"] = CreatePointSchema("fromPoint"),
                    ["toElementIndex"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                    ["toPoint"] = CreatePointSchema("toPoint"),
                    ["coordinateSpace"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                    ["confirm"] = new JsonObject { ["type"] = "boolean" },
                },
            });

    private static JsonObject CreatePointSchema(string propertyName = "point") =>
        new()
        {
            ["type"] = CreateTypeSet("object", "null"),
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "integer" },
                ["y"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = CreateStringArray("x", "y"),
            ["additionalProperties"] = false,
        };

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

    private static JsonElement ParseSchema(JsonObject schema)
    {
        using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }
}
