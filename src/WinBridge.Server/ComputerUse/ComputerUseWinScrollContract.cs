// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinScrollPayload(
    string TargetMode,
    string Direction,
    int Pages,
    int Delta,
    string DeltaBucket,
    string? CoordinateSpace);

internal static class ComputerUseWinScrollContract
{
    internal static readonly IReadOnlyList<string> AllowedDirectionValues =
    [
        UiaScrollDirectionValues.Up,
        UiaScrollDirectionValues.Down,
        UiaScrollDirectionValues.Left,
        UiaScrollDirectionValues.Right,
    ];

    public static string? ValidateRequest(ComputerUseWinScrollRequest request) =>
        TryParse(request, out _, out string? failure) ? null : failure;

    public static bool TryParse(
        ComputerUseWinScrollRequest request,
        out ComputerUseWinScrollPayload? payload,
        out string? failure)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(request.StateToken))
        {
            failure = "Параметр stateToken обязателен для scroll.";
            return false;
        }

        bool hasElementTarget = request.ElementIndex is not null;
        bool hasPointTarget = request.Point is not null;
        if (hasElementTarget == hasPointTarget)
        {
            failure = "Для scroll нужно передать ровно один target mode: elementIndex или point.";
            return false;
        }

        if (request.ElementIndex is < 1)
        {
            failure = "Параметр elementIndex для scroll должен быть >= 1, если он передан.";
            return false;
        }

        string? pointFailure = ComputerUseWinPointContract.Validate(request.Point, "point");
        if (pointFailure is not null)
        {
            failure = pointFailure;
            return false;
        }

        string? direction = NormalizeDirection(request.Direction);
        if (direction is null)
        {
            failure = "Параметр direction для scroll должен быть `up`, `down`, `left` или `right`.";
            return false;
        }

        int pages = request.Pages ?? 1;
        if (pages < 1 || pages > InputActionScalarConstraints.MaximumScrollPages)
        {
            failure = $"Параметр pages для scroll должен быть в диапазоне 1..{InputActionScalarConstraints.MaximumScrollPages}.";
            return false;
        }

        if (hasElementTarget)
        {
            if (request.Point is not null || request.CoordinateSpace is not null)
            {
                failure = "semantic scroll по elementIndex не должен задавать point или coordinateSpace.";
                return false;
            }

            payload = new(
                TargetMode: "element_index",
                Direction: direction,
                Pages: pages,
                Delta: 0,
                DeltaBucket: ClassifyPagesBucket(pages),
                CoordinateSpace: null);
            failure = null;
            return true;
        }

        if (!ComputerUseWinCoordinateSpaceContract.TryNormalize(
                request.CoordinateSpace,
                "coordinateSpace",
                InputCoordinateSpaceValues.All,
                InputCoordinateSpaceValues.CapturePixels,
                out string? coordinateSpace,
                out failure))
        {
            return false;
        }

        payload = new(
            TargetMode: "point",
            Direction: direction,
            Pages: pages,
            Delta: ResolveWheelDelta(direction, pages),
            DeltaBucket: ClassifyPagesBucket(pages),
            CoordinateSpace: coordinateSpace);
        failure = null;
        return true;
    }

    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinScrollRequest request,
        ComputerUseWinScrollPayload payload,
        out ComputerUseWinActionExecutionOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(payload);

        if (string.Equals(payload.TargetMode, "element_index", StringComparison.Ordinal))
        {
            int elementIndex = request.ElementIndex!.Value;
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || !ComputerUseWinActionability.IsScrollActionable(storedElement))
            {
                outcome = ComputerUseWinActionExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.UnsupportedAction,
                        $"elementIndex {elementIndex} не является scrollable target в последнем get_app_state."),
                    ComputerUseWinActionLifecyclePhase.BeforeActivation,
                    confirmationRequired: false,
                    riskClass: "semantic_scroll",
                    dispatchPath: "uia_scroll_pattern");
                return true;
            }

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
                        "Для coordinate scroll по screenshot coordinates нужен актуальный get_app_state со свежим capture proof."),
                    ComputerUseWinActionLifecyclePhase.BeforeActivation,
                    confirmationRequired: true,
                    riskClass: "coordinate_scroll",
                    dispatchPath: "win32_sendinput_wheel");
                return true;
            }

            InputPoint point = request.Point!;
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
                    riskClass: "coordinate_scroll",
                    dispatchPath: "win32_sendinput_wheel");
                return true;
            }
        }

        if (!request.Confirm)
        {
            outcome = ComputerUseWinActionExecutionOutcome.ApprovalRequired(
                "Coordinate scroll fallback требует явного подтверждения.",
                ComputerUseWinActionLifecyclePhase.BeforeActivation,
                confirmationRequired: true,
                riskClass: "coordinate_scroll",
                dispatchPath: "win32_sendinput_wheel");
            return true;
        }

        outcome = null;
        return false;
    }

    public static JsonArray CreateSelectorModeSchema() =>
        new()
        {
            new JsonObject
            {
                ["required"] = CreateStringArray("elementIndex"),
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
                ["required"] = CreateStringArray("point"),
                ["properties"] = new JsonObject
                {
                    ["point"] = ComputerUseWinPointContract.CreateRequiredSchema(),
                },
            },
        };

    private static string? NormalizeDirection(string? value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return AllowedDirectionValues.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }

    private static int ResolveWheelDelta(string direction, int pages)
    {
        int baseDelta = direction switch
        {
            UiaScrollDirectionValues.Up => 120,
            UiaScrollDirectionValues.Down => -120,
            UiaScrollDirectionValues.Right => 120,
            UiaScrollDirectionValues.Left => -120,
            _ => 0,
        };
        return checked(baseDelta * pages);
    }

    private static string ClassifyPagesBucket(int pages) =>
        pages switch
        {
            1 => "single_page",
            <= 3 => "few_pages",
            _ => "many_pages",
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
}
