// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class InputContractAndPolicyTests
{
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void InputRequestUsesExpectedDefaults()
    {
        InputRequest request = new();

        Assert.Null(request.Hwnd);
        Assert.Empty(request.Actions);
        Assert.False(request.Confirm);
        Assert.Null(request.AdditionalProperties);
    }

    [Fact]
    public void InputResultUsesExpectedDefaults()
    {
        InputResult result = new(InputStatusValues.Failed, InputStatusValues.Failed);

        Assert.Equal(InputStatusValues.Failed, result.Status);
        Assert.Equal(InputStatusValues.Failed, result.Decision);
        Assert.Null(result.ResultMode);
        Assert.Null(result.FailureCode);
        Assert.Null(result.Reason);
        Assert.Null(result.TargetHwnd);
        Assert.Null(result.TargetSource);
        Assert.Equal(0, result.CompletedActionCount);
        Assert.Null(result.FailedActionIndex);
        Assert.Null(result.Actions);
        Assert.Null(result.ArtifactPath);
        Assert.Null(result.RiskLevel);
        Assert.Null(result.GuardCapability);
        Assert.False(result.RequiresConfirmation);
        Assert.False(result.DryRunSupported);
        Assert.Null(result.Reasons);
    }

    [Fact]
    public void BoundsWireShapePreservesDerivedWidthAndHeightForObserveContracts()
    {
        JsonElement bounds = JsonSerializer.SerializeToElement(new Bounds(10, 20, 210, 220), CamelCaseJsonOptions);

        Assert.Equal(200, bounds.GetProperty("width").GetInt32());
        Assert.Equal(200, bounds.GetProperty("height").GetInt32());
    }

    [Fact]
    public void CaptureMetadataPreservesObserveBoundsWidthAndHeightAndUsesEdgeOnlyCaptureReference()
    {
        CaptureMetadata metadata = new(
            Scope: "window",
            TargetKind: "window",
            Hwnd: 100,
            Title: "Captured",
            ProcessName: "okno-tests",
            Bounds: new Bounds(10, 20, 210, 220),
            CoordinateSpace: CaptureCoordinateSpaceValues.PhysicalPixels,
            PixelWidth: 200,
            PixelHeight: 200,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            ArtifactPath: @"C:\artifacts\capture.png",
            MimeType: "image/png",
            ByteSize: 42,
            SessionRunId: "capture-tests",
            EffectiveDpi: 96,
            DpiScale: 1.0,
            CaptureReference: new InputCaptureReference(
                new InputBounds(10, 20, 210, 220),
                pixelWidth: 200,
                pixelHeight: 200,
                effectiveDpi: 96,
                capturedAtUtc: DateTimeOffset.UtcNow,
                frameBounds: new InputBounds(10, 20, 226, 232),
                targetIdentity: new InputTargetIdentity(100, 123, 456, "OknoWindow")),
            FrameBounds: new Bounds(10, 20, 226, 232));

        JsonElement metadataElement = JsonSerializer.SerializeToElement(
            metadata,
            CamelCaseJsonOptions);

        JsonElement bounds = metadataElement.GetProperty("bounds");
        Assert.Equal(
            ["bottom", "height", "left", "right", "top", "width"],
            bounds.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        JsonElement captureReferenceBounds = metadataElement.GetProperty("captureReference").GetProperty("bounds");
        Assert.Equal(
            ["bottom", "left", "right", "top"],
            captureReferenceBounds.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
        JsonElement targetIdentity = metadataElement.GetProperty("captureReference").GetProperty("targetIdentity");
        Assert.Equal(100, targetIdentity.GetProperty("hwnd").GetInt64());
        Assert.Equal(123, targetIdentity.GetProperty("processId").GetInt32());
        Assert.Equal(456, targetIdentity.GetProperty("threadId").GetInt32());
        Assert.Equal("OknoWindow", targetIdentity.GetProperty("className").GetString());
    }

    [Fact]
    public void InputActionPreservesWhitespaceForTextPayload()
    {
        InputAction action = new()
        {
            Type = InputActionTypeValues.Type,
            Text = "  hello \n",
        };

        Assert.Equal("  hello \n", action.Text);
    }

    [Fact]
    public void InputValueSetsExposeExpectedPackageALiterals()
    {
        Assert.Equal("move", InputActionTypeValues.Move);
        Assert.Equal("click", InputActionTypeValues.Click);
        Assert.Equal("double_click", InputActionTypeValues.DoubleClick);
        Assert.Equal("drag", InputActionTypeValues.Drag);
        Assert.Equal("scroll", InputActionTypeValues.Scroll);
        Assert.Equal("type", InputActionTypeValues.Type);
        Assert.Equal("keypress", InputActionTypeValues.Keypress);
        Assert.Equal("left", InputButtonValues.Left);
        Assert.Equal("right", InputButtonValues.Right);
        Assert.Equal("middle", InputButtonValues.Middle);
        Assert.Equal("capture_pixels", InputCoordinateSpaceValues.CapturePixels);
        Assert.Equal("screen", InputCoordinateSpaceValues.Screen);
        Assert.Equal("ctrl", InputModifierKeyValues.Ctrl);
        Assert.Equal("alt", InputModifierKeyValues.Alt);
        Assert.Equal("shift", InputModifierKeyValues.Shift);
        Assert.Equal("win", InputModifierKeyValues.Win);
        Assert.Equal("verify_needed", InputStatusValues.VerifyNeeded);
        Assert.Equal("dispatch_only", InputResultModeValues.DispatchOnly);
        Assert.Equal("postcondition_verified", InputResultModeValues.PostconditionVerified);
        Assert.Equal(
            [
                "invalid_request",
                "unsupported_action_type",
                "unsupported_coordinate_space",
                "unsupported_key",
                "unsupported_keyboard_layout",
                "missing_target",
                "target_preflight_failed",
                "stale_explicit_target",
                "stale_attached_target",
                "target_not_foreground",
                "target_minimized",
                "target_integrity_blocked",
                "capture_reference_required",
                "capture_reference_stale",
                "point_out_of_bounds",
                "cursor_move_failed",
                "input_dispatch_failed",
            ],
            GetPublicStringConstants(typeof(InputFailureCodeValues)));
    }

    [Fact]
    public void InputRequestValidatorRejectsEmptyActions()
    {
        bool isValid = InputRequestValidator.TryValidateStructure(
            new InputRequest(),
            out string? failureCode,
            out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("actions", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsUnexpectedAdditionalProperty()
    {
        InputRequest request = new()
        {
            Actions =
            [
                CreatePointerAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.Screen),
            ],
            AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["dryRun"] = JsonDocument.Parse("true").RootElement.Clone(),
            },
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("dryRun", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void InputRequestValidatorRejectsNullActionElementAsInvalidRequest()
    {
        InputRequest request = DeserializeRequest("""{"actions":[null]}""");

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("actions[0]", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"bad\"")]
    [InlineData("123")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void InputRequestValidatorRejectsRequestNonObjectTokenVariants(string requestJson)
    {
        InputRequest request = DeserializeRequest(requestJson);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("request", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"bad\"")]
    [InlineData("123")]
    [InlineData("{}")]
    [InlineData("null")]
    public void InputRequestValidatorRejectsActionsNonArrayTokenVariants(string actionsJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": {{actionsJson}}
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("actions", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"bad\"")]
    [InlineData("123")]
    [InlineData("[1,2]")]
    public void InputRequestValidatorRejectsActionNonObjectTokenVariants(string actionJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {{actionJson}}
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("actions[0]", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("{}")]
    [InlineData("[\"click\"]")]
    public void InputRequestValidatorRejectsActionTypeNonStringTokenVariants(string typeJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {
                  "type": {{typeJson}}
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("type", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsTokenLikeFieldNonStringToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": 123,
                  "point": { "x": 10, "y": 20 }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("coordinateSpace", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsPayloadTextNonStringToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "type",
                  "text": { "value": "hello" }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("text", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsKeysNonArrayToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": { "x": 10, "y": 20 },
                  "keys": "ctrl"
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("keys", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsKeysElementNonStringToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": { "x": 10, "y": 20 },
                  "keys": [123]
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("keys", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsActionOutsideStructuralFreeze()
    {
        InputRequest request = new()
        {
            Actions =
            [
                new InputAction
                {
                    Type = "hotkey",
                    Key = "F5",
                },
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.UnsupportedActionType, failureCode);
        Assert.Contains("hotkey", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void InputRequestValidatorAcceptsDeferredStructuralActionWhenSubsetPolicyIsNotApplied()
    {
        InputRequest request = new()
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Scroll,
                    Point = new InputPoint(120, 240),
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Delta = 120,
                    Direction = "vertical",
                    Keys = [InputModifierKeyValues.Shift],
                },
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void InputRequestValidatorRejectsDeferredActionWhenSubsetPolicyIsApplied()
    {
        InputRequest request = new()
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Scroll,
                    Point = new InputPoint(120, 240),
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Delta = 120,
                    Direction = "vertical",
                },
            ],
        };

        bool isValid = InputRequestValidator.TryValidateSupportedSubset(
            request,
            [InputActionTypeValues.Move, InputActionTypeValues.Click, InputActionTypeValues.DoubleClick],
            out string? failureCode,
            out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.UnsupportedActionType, failureCode);
        Assert.Contains("subset", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsCapturePixelsWithoutCaptureReference()
    {
        InputRequest request = new()
        {
            Actions =
            [
                CreatePointerAction(InputActionTypeValues.Click, InputCoordinateSpaceValues.CapturePixels),
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.CaptureReferenceRequired, failureCode);
        Assert.Contains("captureReference", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsScreenActionWithCaptureReference()
    {
        InputRequest request = new()
        {
            Actions =
            [
                CreatePointerAction(
                    InputActionTypeValues.Click,
                    InputCoordinateSpaceValues.Screen,
                    CreateCaptureReference()),
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("captureReference", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorAllowsWhitespaceOnlyTypePayload()
    {
        InputRequest request = new()
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Type,
                    Text = " \n\t ",
                },
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void InputRequestValidatorRejectsTypeActionWithKeyField()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "type",
                  "text": "hello",
                  "key": "Enter"
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("key", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsTypeActionWithExplicitNullKeyField()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "type",
                  "text": "hello",
                  "key": null
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("key", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsKeypressActionWithTextField()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "keypress",
                  "key": "Enter",
                  "text": "ignored"
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("text", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsKeypressActionWithExplicitNullTextField()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "keypress",
                  "key": "Enter",
                  "text": null
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("text", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsForbiddenPointerTextFieldWhenWhitespacePayloadIsPresent()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": { "x": 10, "y": 20 },
                  "text": " \n"
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("text", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsForbiddenKeyboardPointFieldWhenNullIsExplicitlyProvided()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "type",
                  "text": "hello",
                  "point": null
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsNullDragPathElementAsInvalidRequest()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "drag",
                  "coordinateSpace": "screen",
                  "path": [
                    { "x": 10, "y": 20 },
                    null
                  ]
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("path[1]", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void InputRequestValidatorRejectsPointMissingRequiredCoordinateMember()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": { "x": 10 }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsPointExplicitNullCoordinateMember()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": { "x": null, "y": 20 }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsPointNonNumericCoordinateToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": { "x": "10", "y": 20 }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsPointNonObjectToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": "bad"
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void InputRequestValidatorRejectsPointNonObjectTokenVariants(string pointJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "screen",
                  "point": {{pointJson}}
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("point", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsBoundsMissingRequiredEdge()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {
                      "left": 10,
                      "right": 210,
                      "bottom": 220
                    },
                    "pixelWidth": 200,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("bounds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsBoundsExplicitNullEdge()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {
                      "left": null,
                      "top": 20,
                      "right": 210,
                      "bottom": 220
                    },
                    "pixelWidth": 200,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("bounds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsCaptureReferenceExplicitNullPixelWidth()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {
                      "left": 10,
                      "top": 20,
                      "right": 210,
                      "bottom": 220
                    },
                    "pixelWidth": null,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("captureReference", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsCaptureReferenceNonObjectToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": "bad"
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("captureReference", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void InputRequestValidatorRejectsCaptureReferenceNonObjectTokenVariants(string captureReferenceJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {{captureReferenceJson}}
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("captureReference", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsCaptureReferenceBoundsNonObjectToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": "bad",
                    "pixelWidth": 200,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("bounds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void InputRequestValidatorRejectsCaptureReferenceBoundsNonObjectTokenVariants(string boundsJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {{boundsJson}},
                    "pixelWidth": 200,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("bounds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsCaptureReferenceInvalidEffectiveDpiToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {
                      "left": 10,
                      "top": 20,
                      "right": 210,
                      "bottom": 220
                    },
                    "pixelWidth": 200,
                    "pixelHeight": 200,
                    "effectiveDpi": "96"
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("effectiveDpi", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsDragPathPointMissingRequiredCoordinateMember()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "drag",
                  "coordinateSpace": "screen",
                  "path": [
                    { "x": 10, "y": 20 },
                    { "x": 30 }
                  ]
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("path[1]", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsDragPathPointNonObjectToken()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "drag",
                  "coordinateSpace": "screen",
                  "path": [
                    { "x": 10, "y": 20 },
                    "bad"
                  ]
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("path[1]", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void InputRequestValidatorRejectsDragPathPointNonObjectTokenVariants(string pathPointJson)
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {
                  "type": "drag",
                  "coordinateSpace": "screen",
                  "path": [
                    { "x": 10, "y": 20 },
                    {{pathPointJson}}
                  ]
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("path[1]", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorRejectsBoundsWithUnexpectedAdditionalProperty()
    {
        InputRequest request = DeserializeRequest(
            """
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {
                      "left": 10,
                      "top": 20,
                      "right": 210,
                      "bottom": 220,
                      "padding": 1
                    },
                    "pixelWidth": 200,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("bounds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputRequestValidatorAllowsExplicitZeroPointCoordinates()
    {
        InputRequest request = new()
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = InputCoordinateSpaceValues.Screen,
                    Point = new InputPoint(0, 0),
                },
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void InputRequestValidatorAllowsCaptureBoundsAnchoredAtOrigin()
    {
        InputRequest request = new()
        {
            Actions =
            [
                new InputAction
                {
                    Type = InputActionTypeValues.Click,
                    CoordinateSpace = InputCoordinateSpaceValues.CapturePixels,
                    Point = new InputPoint(0, 0),
                    CaptureReference = new InputCaptureReference(
                        new InputBounds(0, 0, 200, 200),
                        200,
                        200,
                        targetIdentity: new InputTargetIdentity(101, 123, 456, "OknoWindow")),
                },
            ],
        };

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.True(isValid);
        Assert.Null(failureCode);
        Assert.Null(reason);
    }

    [Fact]
    public void InputRequestValidatorRejectsBoundsWithOverflowingDerivedWidth()
    {
        InputRequest request = DeserializeRequest(
            $$"""
            {
              "actions": [
                {
                  "type": "click",
                  "coordinateSpace": "capture_pixels",
                  "point": { "x": 10, "y": 20 },
                  "captureReference": {
                    "bounds": {
                      "left": {{int.MaxValue}},
                      "top": 20,
                      "right": {{int.MinValue}},
                      "bottom": 220
                    },
                    "pixelWidth": 200,
                    "pixelHeight": 200
                  }
                }
              ]
            }
            """);

        bool isValid = InputRequestValidator.TryValidateStructure(request, out string? failureCode, out string? reason);

        Assert.False(isValid);
        Assert.Equal(InputFailureCodeValues.InvalidRequest, failureCode);
        Assert.Contains("bounds", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveInputTargetPrefersExplicitWindowOverAttached()
    {
        WindowDescriptor explicitWindow = CreateWindow(hwnd: 101, title: "Explicit", isForeground: false);
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([explicitWindow, attachedWindow]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitWindow.Hwnd, attachedWindow);

        Assert.Equal(explicitWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(InputTargetSourceValues.Explicit, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveInputTargetReturnsStaleExplicitWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: true);
        WindowTargetResolver resolver = new(new FakeWindowManager([attachedWindow]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitHwnd: 404, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(InputTargetFailureValues.StaleExplicitTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveInputTargetRejectsExplicitWindowWithoutStableIdentity()
    {
        WindowDescriptor explicitWindow = new(
            Hwnd: 101,
            Title: "Explicit",
            ProcessName: "okno-tests",
            ProcessId: null,
            ThreadId: null,
            ClassName: null,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: true,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");
        WindowTargetResolver resolver = new(new FakeWindowManager([explicitWindow]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitWindow.Hwnd, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(InputTargetFailureValues.StaleExplicitTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveInputTargetRejectsExplicitWindowWhenAttachedIdentityForSameHwndNoLongerMatches()
    {
        WindowDescriptor attachedWindow = CreateWindow(
            hwnd: 101,
            title: "Observed",
            isForeground: true,
            processId: 123,
            threadId: 456,
            className: "ObservedWindow");
        WindowDescriptor recycledWindow = CreateWindow(
            hwnd: 101,
            title: "Recycled",
            isForeground: true,
            processId: 999,
            threadId: 777,
            className: "RecycledWindow");
        WindowTargetResolver resolver = new(new FakeWindowManager([recycledWindow]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitHwnd: 101, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(InputTargetFailureValues.StaleExplicitTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveExplicitOrAttachedWindowAllowsExplicitHwndWithoutStableIdentity()
    {
        WindowDescriptor explicitWindow = new(
            Hwnd: 101,
            Title: "Explicit",
            ProcessName: "okno-tests",
            ProcessId: null,
            ThreadId: null,
            ClassName: null,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: true,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");
        WindowTargetResolver resolver = new(new FakeWindowManager([explicitWindow]));

        WindowDescriptor? resolution = resolver.ResolveExplicitOrAttachedWindow(explicitWindow.Hwnd, attachedWindow: null);

        Assert.NotNull(resolution);
        Assert.Equal(explicitWindow.Hwnd, resolution!.Hwnd);
    }

    [Fact]
    public void ResolveInputTargetUsesAttachedWindowWhenExplicitIsMissing()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: true);
        WindowDescriptor renamedLiveWindow = attachedWindow with { Title = "Attached renamed" };
        WindowTargetResolver resolver = new(new FakeWindowManager([renamedLiveWindow]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitHwnd: null, attachedWindow);

        Assert.Equal(attachedWindow.Hwnd, resolution.Window?.Hwnd);
        Assert.Equal(InputTargetSourceValues.Attached, resolution.Source);
        Assert.Null(resolution.FailureCode);
    }

    [Fact]
    public void ResolveInputTargetReturnsStaleAttachedWithoutFallback()
    {
        WindowDescriptor attachedWindow = CreateWindow(hwnd: 202, title: "Attached", isForeground: true);
        WindowDescriptor reusedLiveWindow = CreateWindow(hwnd: 202, title: "Different", isForeground: true, threadId: 999);
        WindowTargetResolver resolver = new(new FakeWindowManager([reusedLiveWindow]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitHwnd: null, attachedWindow);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(InputTargetFailureValues.StaleAttachedTarget, resolution.FailureCode);
    }

    [Fact]
    public void ResolveInputTargetReturnsMissingTargetWhenRequestDoesNotProvideTarget()
    {
        WindowTargetResolver resolver = new(new FakeWindowManager([]));

        InputTargetResolution resolution = resolver.ResolveInputTarget(explicitHwnd: null, attachedWindow: null);

        Assert.Null(resolution.Window);
        Assert.Null(resolution.Source);
        Assert.Equal(InputTargetFailureValues.MissingTarget, resolution.FailureCode);
    }

    private static InputAction CreatePointerAction(
        string type,
        string coordinateSpace,
        InputCaptureReference? captureReference = null)
    {
        if (captureReference is null)
        {
            return new InputAction
            {
                Type = type,
                Point = new InputPoint(120, 240),
                CoordinateSpace = coordinateSpace,
            };
        }

        return new InputAction
        {
            Type = type,
            Point = new InputPoint(120, 240),
            CoordinateSpace = coordinateSpace,
            CaptureReference = captureReference,
        };
    }

    private static InputCaptureReference CreateCaptureReference() =>
        new(
            new InputBounds(10, 20, 210, 220),
            200,
            200,
            96,
            DateTimeOffset.Parse("2026-04-10T12:00:00Z", CultureInfo.InvariantCulture),
            targetIdentity: new InputTargetIdentity(101, 123, 456, "OknoWindow"));

    private static InputRequest DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<InputRequest>(json)
        ?? throw new InvalidOperationException("JSON did not deserialize to InputRequest.");

    private static string[] GetPublicStringConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

    private static WindowDescriptor CreateWindow(
        long hwnd,
        string title,
        bool isForeground,
        int processId = 123,
        int threadId = 456,
        string className = "OknoWindow") =>
        new(
            Hwnd: hwnd,
            Title: title,
            ProcessName: "okno-tests",
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
            Bounds: new Bounds(10, 20, 210, 220),
            IsForeground: isForeground,
            IsVisible: true,
            WindowState: WindowStateValues.Normal,
            MonitorId: "display-source:0000000100000000:1",
            MonitorFriendlyName: "Primary monitor");

    private sealed class FakeWindowManager(IReadOnlyList<WindowDescriptor> windows) : IWindowManager
    {
        public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false) => windows;

        public WindowDescriptor? FindWindow(WindowSelector selector)
        {
            selector.Validate();
            return windows.FirstOrDefault(window => window.Hwnd == selector.Hwnd);
        }

        public bool TryFocus(long hwnd) => windows.Any(window => window.Hwnd == hwnd);
    }
}
