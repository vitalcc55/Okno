using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinClickContract
{
    public static string? ValidateRequest(ComputerUseWinClickRequest request) =>
        ValidatePoint(request.Point, "point")
        ?? ValidateCoordinateSpace(request.CoordinateSpace)
        ?? ValidateButton(request.Button);

    public static bool TryClassifyBeforeActivation(
        ComputerUseWinStoredState state,
        ComputerUseWinClickRequest request,
        out ComputerUseWinClickExecutionOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!TryValidateRequest(request, out string? failureCode, out string? reason))
        {
            outcome = ComputerUseWinClickExecutionOutcome.Failure(
                ComputerUseWinFailureDetails.Expected(failureCode!, reason!));
            return true;
        }

        if (request.ElementIndex is int elementIndex)
        {
            if (!state.Elements.TryGetValue(elementIndex, out ComputerUseWinStoredElement? storedElement)
                || storedElement.Bounds is null)
            {
                outcome = ComputerUseWinClickExecutionOutcome.Failure(
                    ComputerUseWinFailureDetails.Expected(
                        ComputerUseWinFailureCodeValues.InvalidRequest,
                        $"elementIndex {elementIndex} не существует или не даёт кликабельных bounds."));
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
                        InputFailureCodeValues.CaptureReferenceRequired,
                        $"Действие actions[0] с coordinateSpace '{InputCoordinateSpaceValues.CapturePixels}' должно содержать captureReference."));
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

    private static bool TryValidateRequest(
        ComputerUseWinClickRequest request,
        out string? failureCode,
        out string? reason)
    {
        string? validationFailure = ValidateRequest(request);
        if (validationFailure is null)
        {
            failureCode = null;
            reason = null;
            return true;
        }

        failureCode = validationFailure.Contains("coordinateSpace", StringComparison.OrdinalIgnoreCase)
            ? InputFailureCodeValues.UnsupportedCoordinateSpace
            : ComputerUseWinFailureCodeValues.InvalidRequest;
        reason = validationFailure;
        return false;
    }

    private static string? ValidateCoordinateSpace(string? coordinateSpace)
    {
        if (string.IsNullOrWhiteSpace(coordinateSpace))
        {
            return null;
        }

        if (!string.Equals(coordinateSpace, InputCoordinateSpaceValues.Screen, StringComparison.Ordinal)
            && !string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
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

        if (!string.Equals(button, InputButtonValues.Left, StringComparison.Ordinal)
            && !string.Equals(button, InputButtonValues.Right, StringComparison.Ordinal))
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

    internal const string CoordinateApprovalReason =
        "Coordinate click требует явного подтверждения, потому что target не доказан через semantic element из последнего get_app_state.";
}
