using System.Text.Json.Nodes;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinClickContract
{
    internal static IReadOnlyList<string> AllowedCoordinateSpaceValues { get; } =
        [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels];

    internal static IReadOnlyList<string> AllowedButtonValues { get; } =
        [InputButtonValues.Left, InputButtonValues.Right];

    public static string? ValidateRequest(ComputerUseWinClickRequest request) =>
        ValidateStateToken(request.StateToken)
        ?? ValidateResolvedRequest(request);

    private static string? ValidateResolvedRequest(ComputerUseWinClickRequest request) =>
        ValidateSelectorPresence(request)
        ?? ValidateSelectorMode(request)
        ?? ValidatePoint(request.Point, "point")
        ?? ValidateCoordinateSpace(request.CoordinateSpace)
        ?? ValidateButton(request.Button);

    public static JsonObject CreateRequiredStateTokenSchema() =>
        new()
        {
            ["type"] = "string",
            ["minLength"] = 1,
            ["pattern"] = @".*\S.*",
        };

    public static JsonArray CreateSelectorModeSchema() =>
        new()
        {
            new JsonObject
            {
                ["required"] = new JsonArray("elementIndex"),
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
                ["required"] = new JsonArray("point"),
                ["properties"] = new JsonObject
                {
                    ["point"] = CreateRequiredPointSchema(),
                },
            },
        };

    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        out ComputerUseWinClickExecutionOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? validationFailure = ValidateResolvedRequest(request);
        if (validationFailure is not null)
        {
            outcome = ComputerUseWinClickExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(ComputerUseWinFailureCodeValues.InvalidRequest, validationFailure),
                ComputerUseWinActionLifecyclePhase.BeforeActivation);
            return true;
        }

        if (request.ElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || !ComputerUseWinActionability.IsClickActionable(storedElement))
            {
                outcome = ComputerUseWinClickExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.InvalidRequest,
                        $"elementIndex {elementIndex} не существует или больше не является clickable target в последнем get_app_state."),
                    ComputerUseWinActionLifecyclePhase.BeforeActivation);
                return true;
            }

            if (!request.Confirm
                && ComputerUseWinTargetPolicy.RequiresRiskConfirmation(storedElement, ToolNames.ComputerUseWinClick))
            {
                outcome = ComputerUseWinClickExecutionOutcome.ApprovalRequired(
                    "Клик по выбранному элементу требует явного подтверждения.",
                    ComputerUseWinActionLifecyclePhase.BeforeActivation);
                return true;
            }

            outcome = null;
            return false;
        }

        if (request.Point is not null)
        {
            string coordinateSpace = request.CoordinateSpace is null
                ? InputCoordinateSpaceValues.CapturePixels
                : request.CoordinateSpace!;
            if (string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
            {
                if (state.CaptureReference is null)
                {
                    outcome = ComputerUseWinClickExecutionOutcome.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                            "Для coordinate click по screenshot coordinates нужен актуальный get_app_state со свежим capture proof."),
                        ComputerUseWinActionLifecyclePhase.BeforeActivation);
                    return true;
                }

                InputPoint point = request.Point;
                if (point.X < 0
                    || point.Y < 0
                    || point.X >= state.CaptureReference.PixelWidth
                    || point.Y >= state.CaptureReference.PixelHeight)
                {
                    outcome = ComputerUseWinClickExecutionOutcome.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.PointOutOfBounds,
                            "Указанная capture_pixels point выходит за пределы capture raster из последнего get_app_state; скорректируй point перед retry."),
                        ComputerUseWinActionLifecyclePhase.BeforeActivation);
                    return true;
                }
            }

            if (!request.Confirm)
            {
                outcome = ComputerUseWinClickExecutionOutcome.ApprovalRequired(
                    CoordinateApprovalReason,
                    ComputerUseWinActionLifecyclePhase.BeforeActivation);
                return true;
            }

            outcome = null;
            return false;
        }

        outcome = ComputerUseWinClickExecutionOutcome.Failure(
            ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                "Для click требуется elementIndex или point."),
            ComputerUseWinActionLifecyclePhase.BeforeActivation);
        return true;
    }

    private static string? ValidateSelectorMode(ComputerUseWinClickRequest request)
    {
        if (request.ElementIndex is not null && request.Point is not null)
        {
            return "Для click нужно передать либо elementIndex, либо point, но не оба селектора сразу.";
        }

        return null;
    }

    private static string? ValidateSelectorPresence(ComputerUseWinClickRequest request)
    {
        if (request.ElementIndex is null && request.Point is null)
        {
            return "Для click требуется elementIndex или point.";
        }

        return null;
    }

    private static string? ValidateStateToken(string? stateToken) =>
        string.IsNullOrWhiteSpace(stateToken)
            ? "Параметр stateToken обязателен для click."
            : null;

    private static string? ValidateCoordinateSpace(string? coordinateSpace)
    {
        return ValidateOptionalEnumToken(
            coordinateSpace,
            "coordinateSpace",
            AllowedCoordinateSpaceValues);
    }

    private static string? ValidateButton(string? button) =>
        ValidateOptionalEnumToken(
            button,
            "button",
            AllowedButtonValues);

    private static string? ValidateOptionalEnumToken(
        string? value,
        string parameterName,
        IReadOnlyList<string> allowedValues)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return $"Параметр {parameterName} не поддерживает пустую строку.";
        }

        if (!allowedValues.Contains(value, StringComparer.Ordinal))
        {
            return $"Параметр {parameterName} использует неподдерживаемое значение '{value}'.";
        }

        return null;
    }

    private static string? ValidatePoint(InputPoint? point, string parameterName)
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

    private static JsonObject CreateRequiredPointSchema() =>
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

    internal const string CoordinateApprovalReason =
        "Coordinate click требует явного подтверждения, потому что target не доказан через semantic element из последнего get_app_state.";
}
