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
        ?? ComputerUseWinPointContract.Validate(request.Point, "point")
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
                    ["point"] = ComputerUseWinPointContract.CreateRequiredSchema(),
                },
            },
        };

    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        out ComputerUseWinActionExecutionOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? validationFailure = ValidateResolvedRequest(request);
        if (validationFailure is not null)
        {
            outcome = ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(ComputerUseWinFailureCodeValues.InvalidRequest, validationFailure),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: false,
                riskClass: null,
                dispatchPath: null);
            return true;
        }

        if (request.ElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || !ComputerUseWinActionability.IsClickActionable(storedElement))
            {
                outcome = ComputerUseWinActionExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.InvalidRequest,
                        $"elementIndex {elementIndex} не существует или больше не является clickable target в последнем get_app_state."),
                    ComputerUseWinActionLifecyclePhase.BeforeActivation,
                    confirmationRequired: false,
                    riskClass: "semantic_target",
                    dispatchPath: "fresh_uia_revalidation_to_input");
                return true;
            }

            if (!request.Confirm
                && ComputerUseWinTargetPolicy.RequiresRiskConfirmation(storedElement, ToolNames.ComputerUseWinClick))
            {
                outcome = ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                    "Клик по выбранному элементу требует явного подтверждения.",
                    ComputerUseWinActionLifecyclePhase.BeforeActivation,
                    confirmationRequired: true,
                    riskClass: "semantic_risky",
                    dispatchPath: "fresh_uia_revalidation_to_input");
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
                    outcome = ComputerUseWinActionExecutionOutcome.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                            "Для coordinate click по screenshot coordinates нужен актуальный get_app_state со свежим capture proof."),
                        ComputerUseWinActionLifecyclePhase.BeforeActivation,
                        confirmationRequired: true,
                        riskClass: "coordinate_low_confidence",
                        dispatchPath: "capture_pixels_input");
                    return true;
                }

                InputPoint point = request.Point;
                if (point.X < 0
                    || point.Y < 0
                    || point.X >= state.CaptureReference.PixelWidth
                    || point.Y >= state.CaptureReference.PixelHeight)
                {
                    outcome = ComputerUseWinActionExecutionOutcome.Failure(
                        ComputerUseWinFailureDetails.Expected(
                            ComputerUseWinFailureCodeValues.PointOutOfBounds,
                            "Указанная capture_pixels point выходит за пределы capture raster из последнего get_app_state; скорректируй point перед retry."),
                        ComputerUseWinActionLifecyclePhase.BeforeActivation,
                        confirmationRequired: true,
                        riskClass: "coordinate_low_confidence",
                        dispatchPath: "capture_pixels_input");
                    return true;
                }
            }

            if (!request.Confirm)
            {
                outcome = ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                    CoordinateApprovalReason,
                    ComputerUseWinActionLifecyclePhase.BeforeActivation,
                    confirmationRequired: true,
                    riskClass: "coordinate_low_confidence",
                    dispatchPath: string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
                        ? "capture_pixels_input"
                        : "screen_input");
                return true;
            }

            outcome = null;
            return false;
        }

        outcome = ComputerUseWinActionExecutionOutcome.Failure(
            ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.InvalidRequest,
                "Для click требуется elementIndex или point."),
            ComputerUseWinActionLifecyclePhase.BeforeActivation,
            confirmationRequired: false,
            riskClass: null,
            dispatchPath: null);
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

    internal const string CoordinateApprovalReason =
        "Coordinate click требует явного подтверждения, потому что target не доказан через semantic element из последнего get_app_state.";
}
