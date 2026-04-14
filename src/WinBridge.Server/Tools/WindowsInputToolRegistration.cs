using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.Tools;

internal static class WindowsInputToolRegistration
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static McpServerTool Create(Func<WindowTools> getWindowTools)
    {
        ArgumentNullException.ThrowIfNull(getWindowTools);

        ValueTask<CallToolResult> Handler(
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CreateToolResult(getWindowTools().Input()));

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

    private static CallToolResult CreateToolResult(DeferredToolResult payload)
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(payload, PayloadJsonOptions);

        return new CallToolResult
        {
            IsError = false,
            StructuredContent = structuredContent,
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, PayloadJsonOptions),
                },
            ],
        };
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
                    ["description"] = "Ordered batch of frozen Package A input actions. Runtime dispatch is deferred; schema is frozen for Package B/C.",
                    ["items"] = CreateActionItemSchema(),
                },
                ["hwnd"] = new JsonObject
                {
                    ["type"] = CreateTypeSet("integer", "null"),
                    ["description"] = "Optional explicit HWND. Input target policy is explicit -> attached; no active fallback.",
                },
                ["confirm"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Reserved confirmation flag for the shared execution gate; supports_dry_run=false.",
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
        foreach (InputActionContract contract in InputActionContractCatalog.All)
        {
            branches.Add(CreateActionBranch(contract));
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = CreateActionProperties(AllActionFields(), requiredFields: InputActionField.None),
            ["oneOf"] = branches,
        };
    }

    private static JsonObject CreateActionBranch(InputActionContract contract)
    {
        JsonObject properties = CreateActionProperties(contract.AllowedFields, contract.RequiredFields);
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

    private static JsonObject CreateActionProperties(InputActionField fields, InputActionField requiredFields)
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
            InputActionField.Path => new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 2,
                ["items"] = CreatePointSchema(nullable: false),
            },
            InputActionField.CoordinateSpace => new JsonObject
            {
                ["type"] = "string",
                ["enum"] = CreateStringArray(InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels),
                ["description"] = "Coordinate space: capture_pixels or screen.",
            },
            InputActionField.Button => new JsonObject
            {
                ["type"] = "string",
                ["enum"] = CreateStringArray(InputButtonValues.Left, InputButtonValues.Right, InputButtonValues.Middle),
                ["description"] = "Pointer button: left, right, or middle. Right click is click(button=right), not a separate action literal.",
            },
            InputActionField.Keys => new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = CreateStringArray(InputModifierKeyValues.Ctrl, InputModifierKeyValues.Alt, InputModifierKeyValues.Shift, InputModifierKeyValues.Win),
                },
                ["description"] = "Optional modifier keys for pointer actions.",
            },
            InputActionField.Text => new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Literal text payload for type actions; whitespace is preserved.",
            },
            InputActionField.Key => new JsonObject
            {
                ["type"] = "string",
                ["minLength"] = 1,
                ["pattern"] = InputActionScalarConstraints.NonWhitespacePattern,
                ["description"] = "Key identifier for keypress actions.",
            },
            InputActionField.Repeat => new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = InputActionScalarConstraints.MinimumRepeat,
                ["description"] = "Optional repeat count for keypress actions.",
            },
            InputActionField.Delta => new JsonObject
            {
                ["type"] = "integer",
                ["not"] = new JsonObject { ["const"] = InputActionScalarConstraints.InvalidScrollDelta },
                ["description"] = "Scroll delta for scroll actions.",
            },
            InputActionField.Direction => new JsonObject
            {
                ["type"] = "string",
                ["minLength"] = 1,
                ["pattern"] = InputActionScalarConstraints.NonWhitespacePattern,
                ["description"] = "Scroll direction for scroll actions.",
            },
            InputActionField.CaptureReference => CreateCaptureReferenceSchema(nullable: false),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private static JsonObject CreateTypePropertySchema(string? actionType = null)
    {
        JsonObject schema = new()
        {
            ["type"] = "string",
            ["description"] = "Action literal: move, click, double_click, drag, scroll, type, or keypress.",
        };

        schema["enum"] = actionType is null
            ? CreateStringArray(InputActionTypeValues.Move, InputActionTypeValues.Click, InputActionTypeValues.DoubleClick, InputActionTypeValues.Drag, InputActionTypeValues.Scroll, InputActionTypeValues.Type, InputActionTypeValues.Keypress)
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
                ["pixelWidth"] = new JsonObject { ["type"] = "integer", ["minimum"] = InputActionScalarConstraints.MinimumCapturePixelDimension },
                ["pixelHeight"] = new JsonObject { ["type"] = "integer", ["minimum"] = InputActionScalarConstraints.MinimumCapturePixelDimension },
                ["effectiveDpi"] = new JsonObject { ["type"] = CreateTypeSet("integer", "null") },
                ["capturedAtUtc"] = new JsonObject { ["type"] = CreateTypeSet("string", "null") },
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
                        ["coordinateSpace"] = new JsonObject { ["enum"] = CreateStringArray(InputCoordinateSpaceValues.CapturePixels) },
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
                    ["coordinateSpace"] = new JsonObject { ["enum"] = CreateStringArray(InputCoordinateSpaceValues.Screen) },
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
        foreach (InputActionContract contract in InputActionContractCatalog.All)
        {
            result |= contract.AllowedFields;
        }

        return result;
    }
}
