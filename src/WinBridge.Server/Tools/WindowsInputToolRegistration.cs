using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.Tools;

internal static class WindowsInputToolRegistration
{
    public static McpServerTool Create(Func<WindowTools> getWindowTools)
    {
        ArgumentNullException.ThrowIfNull(getWindowTools);

        ValueTask<CallToolResult> Handler(
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken) =>
            new(getWindowTools().Input(requestContext, cancellationToken));

        McpServerTool tool = McpServerTool.Create(
            (Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>)Handler,
            new McpServerToolCreateOptions
            {
                Name = ToolNames.WindowsInput,
                Description = ToolDescriptions.WindowsInputTool,
                ReadOnly = false,
                Destructive = true,
                Idempotent = false,
                OpenWorld = true,
                UseStructuredContent = true,
            });

        tool.ProtocolTool.InputSchema = CreateInputSchema();
        return tool;
    }

    private static JsonElement CreateInputSchema()
    {
        JsonObject schema = new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["actions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = 16,
                    ["description"] = ToolDescriptions.InputActionsParameter,
                    ["items"] = CreateActionItemSchema(),
                },
                ["hwnd"] = new JsonObject
                {
                    ["type"] = CreateTypeSet("integer", "null"),
                    ["description"] = ToolDescriptions.InputHwndParameter,
                },
                ["confirm"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = ToolDescriptions.InputConfirmParameter,
                },
            },
            ["required"] = CreateStringArray("actions"),
        };

        using JsonDocument document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonObject CreateActionItemSchema()
    {
        JsonArray branches = [];
        foreach (InputActionContract contract in InputClickFirstSubsetContract.Actions)
        {
            branches.Add(CreateActionBranch(contract));
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = CreateActionProperties(AllActionFields()),
            ["oneOf"] = branches,
        };
    }

    private static JsonObject CreateActionBranch(InputActionContract contract)
    {
        JsonObject properties = CreateActionProperties(contract.AllowedFields);
        properties["type"] = CreateTypePropertySchema(contract.ActionType);

        JsonObject branch = new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = CreateFieldNameArray(contract.RequiredFields),
        };

        JsonArray coordinateBindingRules = CreateCoordinateBindingRules(contract.AllowedFields);
        if (coordinateBindingRules.Count > 0)
        {
            branch["allOf"] = coordinateBindingRules;
        }

        return branch;
    }

    private static JsonObject CreateActionProperties(InputActionField fields)
    {
        JsonObject properties = [];

        foreach (InputActionField field in InputActionContractCatalog.EnumerateFields(fields))
        {
            properties[InputActionContractCatalog.GetJsonName(field)] = CreateFieldSchema(field);
        }

        return properties;
    }

    private static JsonObject CreateFieldSchema(InputActionField field) =>
        field switch
        {
            InputActionField.Type => CreateTypePropertySchema(),
            InputActionField.Point => CreatePointSchema(nullable: false),
            InputActionField.CoordinateSpace => new JsonObject
            {
                ["type"] = "string",
                ["enum"] = CreateStringArray(InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels),
                ["description"] = "Coordinate space: capture_pixels or screen.",
            },
            InputActionField.Button => new JsonObject
            {
                ["type"] = "string",
                ["enum"] = CreateStringArray(InputButtonValues.Left, InputButtonValues.Right),
                ["description"] = "Pointer button: left or right. Right click is click(button=right), not a separate action literal.",
            },
            InputActionField.CaptureReference => CreateCaptureReferenceSchema(nullable: false),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private static JsonObject CreateTypePropertySchema(string? actionType = null)
    {
        JsonObject schema = new()
        {
            ["type"] = "string",
            ["description"] = "Action literal: move, click, or double_click.",
        };

        schema["enum"] = actionType is null
            ? CreateStringArray(
                InputActionTypeValues.Move,
                InputActionTypeValues.Click,
                InputActionTypeValues.DoubleClick)
            : CreateStringArray(actionType);

        return schema;
    }

    private static JsonObject CreatePointSchema(bool nullable)
    {
        JsonObject schema = new()
        {
            ["type"] = nullable ? CreateTypeSet("object", "null") : "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "integer" },
                ["y"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = CreateStringArray("x", "y"),
        };

        return schema;
    }

    private static JsonObject CreateCaptureReferenceSchema(bool nullable) =>
        new()
        {
            ["type"] = nullable ? CreateTypeSet("object", "null") : "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["bounds"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["left"] = new JsonObject { ["type"] = "integer" },
                        ["top"] = new JsonObject { ["type"] = "integer" },
                        ["right"] = new JsonObject { ["type"] = "integer" },
                        ["bottom"] = new JsonObject { ["type"] = "integer" },
                    },
                    ["required"] = CreateStringArray("left", "top", "right", "bottom"),
                },
                ["pixelWidth"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = InputActionScalarConstraints.MinimumCapturePixelDimension,
                },
                ["pixelHeight"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = InputActionScalarConstraints.MinimumCapturePixelDimension,
                },
                ["effectiveDpi"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                ["capturedAtUtc"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
                ["frameBounds"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["left"] = new JsonObject { ["type"] = "integer" },
                        ["top"] = new JsonObject { ["type"] = "integer" },
                        ["right"] = new JsonObject { ["type"] = "integer" },
                        ["bottom"] = new JsonObject { ["type"] = "integer" },
                    },
                    ["required"] = CreateStringArray("left", "top", "right", "bottom"),
                    ["description"] = "Optional capture-time live window frame bounds used to distinguish WGC content/frame delta from post-capture resize.",
                },
                ["targetIdentity"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["hwnd"] = new JsonObject { ["type"] = "integer" },
                        ["processId"] = new JsonObject { ["type"] = "integer" },
                        ["threadId"] = new JsonObject { ["type"] = "integer" },
                        ["className"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = CreateStringArray("hwnd", "processId", "threadId", "className"),
                    ["description"] = "Optional captured window provenance. Windows.capture publishes it for copy-through safety; windows.input fails closed if live target no longer matches it.",
                },
            },
            ["required"] = CreateStringArray("bounds", "pixelWidth", "pixelHeight"),
        };

    private static JsonArray CreateCoordinateBindingRules(InputActionField allowedFields)
    {
        if ((allowedFields & InputActionField.CoordinateSpace) != InputActionField.CoordinateSpace)
        {
            return [];
        }

        JsonArray rules = [];

        if ((allowedFields & InputActionField.CaptureReference) == InputActionField.CaptureReference)
        {
            rules.Add(new JsonObject
            {
                ["if"] = new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["coordinateSpace"] = new JsonObject
                        {
                            ["enum"] = CreateStringArray(InputCoordinateSpaceValues.CapturePixels),
                        },
                    },
                    ["required"] = CreateStringArray("coordinateSpace"),
                },
                ["then"] = new JsonObject
                {
                    ["required"] = CreateStringArray("captureReference"),
                },
            });
        }

        rules.Add(new JsonObject
        {
            ["if"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["coordinateSpace"] = new JsonObject
                    {
                        ["enum"] = CreateStringArray(InputCoordinateSpaceValues.Screen),
                    },
                },
                ["required"] = CreateStringArray("coordinateSpace"),
            },
            ["then"] = new JsonObject
            {
                ["not"] = new JsonObject
                {
                    ["required"] = CreateStringArray("captureReference"),
                },
            },
        });

        return rules;
    }

    private static JsonArray CreateFieldNameArray(InputActionField fields)
    {
        JsonArray result = [];
        foreach (InputActionField field in InputActionContractCatalog.EnumerateFields(fields))
        {
            result.Add(InputActionContractCatalog.GetJsonName(field));
        }

        return result;
    }

    private static JsonArray CreateStringArray(params string?[] values)
    {
        JsonArray result = [];
        foreach (string? value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static JsonArray CreateTypeSet(params string[] values)
    {
        JsonArray result = [];
        foreach (string value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static InputActionField AllActionFields()
    {
        InputActionField result = InputActionField.None;
        foreach (InputActionContract contract in InputClickFirstSubsetContract.Actions)
        {
            result |= contract.AllowedFields;
        }

        return result;
    }
}
