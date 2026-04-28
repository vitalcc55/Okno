using System.Text.Json.Nodes;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinDragEndpointPayload(
    string Mode,
    int? ElementIndex,
    InputPoint? Point);

internal sealed record ComputerUseWinDragPayload(
    ComputerUseWinDragEndpointPayload Source,
    ComputerUseWinDragEndpointPayload Destination,
    string? CoordinateSpace,
    bool UsesCoordinateEndpoint,
    string PathPointCountBucket);

internal static class ComputerUseWinDragContract
{
    internal static IReadOnlyList<string> AllowedCoordinateSpaceValues { get; } =
        [InputCoordinateSpaceValues.Screen, InputCoordinateSpaceValues.CapturePixels];

    public static string? ValidateRequest(ComputerUseWinDragRequest request) =>
        TryParse(request, out _, out string? failure) ? null : failure;

    public static bool TryParse(
        ComputerUseWinDragRequest request,
        out ComputerUseWinDragPayload? payload,
        out string? failure)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(request.StateToken))
        {
            failure = "Параметр stateToken обязателен для drag.";
            return false;
        }

        if (!TryParseEndpoint(
                request.FromElementIndex,
                request.FromPoint,
                endpointLabel: "source",
                out ComputerUseWinDragEndpointPayload? source,
                out failure))
        {
            return false;
        }

        if (!TryParseEndpoint(
                request.ToElementIndex,
                request.ToPoint,
                endpointLabel: "destination",
                out ComputerUseWinDragEndpointPayload? destination,
                out failure))
        {
            return false;
        }

        if (request.FromElementIndex is not null
            && request.ToElementIndex is not null
            && request.FromElementIndex.Value == request.ToElementIndex.Value)
        {
            failure = "Для drag source и destination elementIndex должны ссылаться на разные элементы.";
            return false;
        }

        if (request.FromPoint is not null
            && request.ToPoint is not null
            && request.FromPoint.X == request.ToPoint.X
            && request.FromPoint.Y == request.ToPoint.Y)
        {
            failure = "Для drag source и destination point не должны совпадать.";
            return false;
        }

        ComputerUseWinDragEndpointPayload sourcePayload = source!;
        ComputerUseWinDragEndpointPayload destinationPayload = destination!;
        bool usesCoordinateEndpoint = string.Equals(sourcePayload.Mode, "point", StringComparison.Ordinal)
            || string.Equals(destinationPayload.Mode, "point", StringComparison.Ordinal);
        bool mixedEndpointModes = !string.Equals(sourcePayload.Mode, destinationPayload.Mode, StringComparison.Ordinal);
        string defaultCoordinateSpace = mixedEndpointModes
            ? InputCoordinateSpaceValues.Screen
            : InputCoordinateSpaceValues.CapturePixels;
        string? coordinateSpace = null;
        if (usesCoordinateEndpoint
            && !ComputerUseWinCoordinateSpaceContract.TryNormalize(
                request.CoordinateSpace,
                "coordinateSpace",
                AllowedCoordinateSpaceValues,
                defaultCoordinateSpace,
                out coordinateSpace,
                out failure))
        {
            return false;
        }

        if (usesCoordinateEndpoint && coordinateSpace is null)
        {
            failure = $"Параметр coordinateSpace для drag point path должен быть `{InputCoordinateSpaceValues.Screen}` или `{InputCoordinateSpaceValues.CapturePixels}`.";
            return false;
        }

        if (!usesCoordinateEndpoint && request.CoordinateSpace is not null)
        {
            failure = "element-to-element drag не должен задавать coordinateSpace без point endpoint.";
            return false;
        }

        if (mixedEndpointModes
            && string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            failure = "mixed drag path element<->point сейчас поддерживает только coordinateSpace=`screen`.";
            return false;
        }

        payload = new(
            Source: sourcePayload,
            Destination: destinationPayload,
            CoordinateSpace: coordinateSpace,
            UsesCoordinateEndpoint: usesCoordinateEndpoint,
            PathPointCountBucket: "two_points");
        failure = null;
        return true;
    }

    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinDragRequest request,
        ComputerUseWinDragPayload payload,
        out ComputerUseWinActionExecutionOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.UsesCoordinateEndpoint)
        {
            outcome = null;
            return false;
        }

        if (string.Equals(payload.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            if (state.CaptureReference is null)
            {
                outcome = ComputerUseWinActionExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
                        "Для coordinate drag по screenshot coordinates нужен актуальный get_app_state со свежим capture proof."),
                    ComputerUseWinActionLifecyclePhase.BeforeActivation,
                    confirmationRequired: true,
                    riskClass: "coordinate_drag",
                    dispatchPath: DetermineDispatchPath(payload));
                return true;
            }

            if (!ValidateCapturePointBounds(request.FromPoint, state.CaptureReference, "source", out outcome)
                || !ValidateCapturePointBounds(request.ToPoint, state.CaptureReference, "destination", out outcome))
            {
                return true;
            }
        }

        if (!request.Confirm)
        {
            outcome = ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                "Coordinate drag требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: "coordinate_drag",
                dispatchPath: DetermineDispatchPath(payload));
            return true;
        }

        outcome = null;
        return false;
    }

    public static JsonArray CreateSourceSelectorModeSchema() =>
        CreateEndpointSelectorModeSchema("fromElementIndex", "fromPoint");

    public static JsonArray CreateDestinationSelectorModeSchema() =>
        CreateEndpointSelectorModeSchema("toElementIndex", "toPoint");

    private static bool TryParseEndpoint(
        int? elementIndex,
        InputPoint? point,
        string endpointLabel,
        out ComputerUseWinDragEndpointPayload? endpoint,
        out string? failure)
    {
        endpoint = null;

        bool hasElementTarget = elementIndex is not null;
        bool hasPointTarget = point is not null;
        if (hasElementTarget == hasPointTarget)
        {
            failure = $"Для drag {endpointLabel} нужно передать ровно один selector mode: {(endpointLabel == "source" ? "fromElementIndex или fromPoint" : "toElementIndex или toPoint")}.";
            return false;
        }

        if (elementIndex is < 1)
        {
            failure = $"Параметр {(endpointLabel == "source" ? "fromElementIndex" : "toElementIndex")} для drag должен быть >= 1, если он передан.";
            return false;
        }

        string pointParameterName = endpointLabel == "source" ? "fromPoint" : "toPoint";
        string? pointFailure = ComputerUseWinPointContract.Validate(point, pointParameterName);
        if (pointFailure is not null)
        {
            failure = pointFailure;
            return false;
        }

        endpoint = hasElementTarget
            ? new("element_index", elementIndex, null)
            : new("point", null, point);
        failure = null;
        return true;
    }

    private static JsonArray CreateEndpointSelectorModeSchema(string elementPropertyName, string pointPropertyName) =>
        new()
        {
            new JsonObject
            {
                ["required"] = CreateStringArray(elementPropertyName),
                ["properties"] = new JsonObject
                {
                    [elementPropertyName] = new JsonObject
                    {
                        ["type"] = "integer",
                    },
                },
            },
            new JsonObject
            {
                ["required"] = CreateStringArray(pointPropertyName),
                ["properties"] = new JsonObject
                {
                    [pointPropertyName] = ComputerUseWinPointContract.CreateRequiredSchema(),
                },
            },
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

    private static bool ValidateCapturePointBounds(
        InputPoint? point,
        InputCaptureReference captureReference,
        string endpointLabel,
        out ComputerUseWinActionExecutionOutcome? outcome)
    {
        outcome = null;
        if (point is null)
        {
            return true;
        }

        if (point.X < 0
            || point.Y < 0
            || point.X >= captureReference.PixelWidth
            || point.Y >= captureReference.PixelHeight)
        {
            outcome = ComputerUseWinActionExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(
                    ComputerUseWinFailureCodeValues.PointOutOfBounds,
                    $"Указанная {endpointLabel} capture_pixels point выходит за пределы capture raster из последнего get_app_state."),
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: "coordinate_drag",
                dispatchPath: DetermineDispatchPath(capturePixels: true));
            return false;
        }

        return true;
    }

    public static string DetermineDispatchPath(ComputerUseWinDragPayload payload) =>
        !payload.UsesCoordinateEndpoint
            ? "fresh_uia_revalidation_to_input_drag"
            : DetermineDispatchPath(string.Equals(payload.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal));

    public static string DetermineDispatchPath(bool capturePixels) =>
        capturePixels ? "capture_pixels_drag_input" : "screen_drag_input";
}
