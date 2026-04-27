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

    private static JsonObject CreateNullableNonBlankStringSchema() =>
        new()
        {
            ["type"] = CreateTypeSet("string", "null"),
            ["pattern"] = @".*\S.*",
        };

    private static JsonElement ParseSchema(JsonObject schema)
    {
        using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }
}
