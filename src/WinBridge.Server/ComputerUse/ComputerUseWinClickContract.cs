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
                ComputerUseWinFailureDetails.Expected(ComputerUseWinFailureCodeValues.InvalidRequest, validationFailure));
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
                        $"elementIndex {elementIndex} не существует или больше не является clickable target в последнем get_app_state."));
                return true;
            }

            if (!request.Confirm
                && ComputerUseWinTargetPolicy.RequiresRiskConfirmation(storedElement, ToolNames.ComputerUseWinClick))
            {
                outcome = ComputerUseWinClickExecutionOutcome.ApprovalRequired("Клик по выбранному элементу требует явного подтверждения.");
                return true;
            }

            outcome = null;
            return false;
        }

        if (request.Point is not null)
        {
            string coordinateSpace = string.IsNullOrWhiteSpace(request.CoordinateSpace)
                ? InputCoordinateSpaceValues.CapturePixels
                : request.CoordinateSpace!;
            if (string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
                && state.CaptureReference is null)
            {
                outcome = ComputerUseWinClickExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                        "Для coordinate click по screenshot coordinates нужен актуальный get_app_state со свежим capture proof."));
                return true;
            }

            if (!request.Confirm)
            {
                outcome = ComputerUseWinClickExecutionOutcome.ApprovalRequired(CoordinateApprovalReason);
                return true;
            }

            outcome = null;
            return false;
        }

        outcome = ComputerUseWinClickExecutionOutcome.Failure(
            ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                "Для click требуется elementIndex или point."));
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
        if (string.IsNullOrWhiteSpace(coordinateSpace))
        {
            return null;
        }

        if (!AllowedCoordinateSpaceValues.Contains(coordinateSpace, StringComparer.Ordinal))
        {
            return $"Параметр coordinateSpace использует неподдерживаемое значение '{coordinateSpace}'.";
        }

        return null;
    }

    private static string? ValidateButton(string? button)
    {
        if (string.IsNullOrWhiteSpace(button))
        {
            return null;
        }

        if (!AllowedButtonValues.Contains(button, StringComparer.Ordinal))
        {
            return $"Параметр button использует неподдерживаемое значение '{button}'.";
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
