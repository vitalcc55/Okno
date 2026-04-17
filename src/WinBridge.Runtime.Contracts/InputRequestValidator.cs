using System.Text.Json;

namespace WinBridge.Runtime.Contracts;

public static class InputRequestValidator
{
    public const int MaxActionCount = 16;

    public static bool TryValidateStructure(
        InputRequest request,
        out string? failureCode,
        out string? reason) =>
        TryValidate(request, allowedActionTypes: null, out failureCode, out reason);

    public static bool TryValidateSupportedSubset(
        InputRequest request,
        IEnumerable<string> allowedActionTypes,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(allowedActionTypes);
        return TryValidate(
            request,
            new HashSet<string>(allowedActionTypes, StringComparer.Ordinal),
            out failureCode,
            out reason);
    }

    private static bool TryValidate(
        InputRequest request,
        IReadOnlySet<string>? allowedActionTypes,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasValidObject)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Request windows.input должен быть JSON object.";
            return false;
        }

        if (request.HasHwnd && !request.HasValidHwnd)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Параметр hwnd для windows.input должен быть integer или null.";
            return false;
        }

        if (request.HasActions && !request.HasValidActions)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Параметр actions для windows.input должен быть массивом действий.";
            return false;
        }

        if (request.HasConfirm && !request.HasValidConfirm)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Параметр confirm для windows.input должен быть boolean.";
            return false;
        }

        if (TryValidateAdditionalProperties(request.AdditionalProperties, out failureCode, out reason) is false)
        {
            return false;
        }

        if (request.Actions.Count == 0)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = "Параметр actions для windows.input не должен быть пустым.";
            return false;
        }

        if (request.Actions.Count > MaxActionCount)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Текущий contract windows.input допускает не более {MaxActionCount} действий в одном пакете.";
            return false;
        }

        if (!TryValidateNonNullActionElements(request.Actions, out failureCode, out reason))
        {
            return false;
        }

        for (int index = 0; index < request.Actions.Count; index++)
        {
            if (!TryValidateAction(request.Actions[index]!, index, allowedActionTypes, out failureCode, out reason))
            {
                return false;
            }
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateAdditionalProperties(
        IDictionary<string, JsonElement>? additionalProperties,
        out string? failureCode,
        out string? reason)
    {
        if (additionalProperties is null || additionalProperties.Count == 0)
        {
            failureCode = null;
            reason = null;
            return true;
        }

        string key = additionalProperties.Keys
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .First();

        failureCode = InputFailureCodeValues.InvalidRequest;
        reason = $"Дополнительное поле '{key}' не входит в текущий request surface windows.input.";
        return false;
    }

    private static bool TryValidateAction(
        InputAction action,
        int index,
        IReadOnlySet<string>? allowedActionTypes,
        out string? failureCode,
        out string? reason)
    {
        if (action is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] не должно быть null.";
            return false;
        }

        if (!action.HasValidObject)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] должно быть JSON object.";
            return false;
        }

        if (action.AdditionalProperties is not null && action.AdditionalProperties.Count > 0)
        {
            string key = action.AdditionalProperties.Keys
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .First();
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит неподдерживаемое поле '{key}'.";
            return false;
        }

        if (!TryValidateActionBinding(action, index, out failureCode, out reason))
        {
            return false;
        }

        if (!action.HasType || string.IsNullOrWhiteSpace(action.Type))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] должно содержать непустой type.";
            return false;
        }

        if (!InputActionTypeValues.StructuralFreeze.Contains(action.Type))
        {
            failureCode = InputFailureCodeValues.UnsupportedActionType;
            reason = $"Действие actions[{index}] использует неподдерживаемый type '{action.Type}' вне замороженного structural freeze Package A.";
            return false;
        }

        if (!InputActionContractCatalog.TryGet(action.Type, out InputActionContract actionContract))
        {
            failureCode = InputFailureCodeValues.UnsupportedActionType;
            reason = $"Действие actions[{index}] использует неподдерживаемый type '{action.Type}' вне замороженного contract catalog Package A.";
            return false;
        }

        if (allowedActionTypes is not null && !allowedActionTypes.Contains(action.Type))
        {
            failureCode = InputFailureCodeValues.UnsupportedActionType;
            reason = $"Действие actions[{index}] с type '{action.Type}' не входит в текущий опубликованный subset и должно остаться deferred до следующего package.";
            return false;
        }

        if (!TryValidateActionFieldOwnership(action, actionContract, index, out failureCode, out reason))
        {
            return false;
        }

        if (!TryValidateModifierKeys(action, index, out failureCode, out reason))
        {
            return false;
        }

        return action.Type switch
        {
            InputActionTypeValues.Move => TryValidateMoveAction(action, index, out failureCode, out reason),
            InputActionTypeValues.Click => TryValidateClickAction(action, index, out failureCode, out reason),
            InputActionTypeValues.DoubleClick => TryValidateDoubleClickAction(action, index, out failureCode, out reason),
            InputActionTypeValues.Drag => TryValidateDragAction(action, index, out failureCode, out reason),
            InputActionTypeValues.Scroll => TryValidateScrollAction(action, index, out failureCode, out reason),
            InputActionTypeValues.Type => TryValidateTypeAction(action, index, out failureCode, out reason),
            InputActionTypeValues.Keypress => TryValidateKeypressAction(action, index, out failureCode, out reason),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.Type, null),
        };
    }

    private static bool TryValidateActionBinding(
        InputAction action,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (action.HasType && !action.HasValidType)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит type вне допустимого string/null surface.";
            return false;
        }

        if (action.HasPath && !action.HasValidPath)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит path не в форме массива.";
            return false;
        }

        if (action.HasCoordinateSpace && !action.HasValidCoordinateSpace)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит coordinateSpace вне допустимого string/null surface.";
            return false;
        }

        if (action.HasButton && !action.HasValidButton)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит button вне допустимого string/null surface.";
            return false;
        }

        if (action.HasKeys && !action.HasValidKeys)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит keys вне допустимого string-array/null surface.";
            return false;
        }

        if (action.HasText && !action.HasValidText)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит text вне допустимого string/null surface.";
            return false;
        }

        if (action.HasKey && !action.HasValidKey)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит key вне допустимого string/null surface.";
            return false;
        }

        if (action.HasRepeat && !action.HasValidRepeat)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит repeat вне допустимого integer/null surface.";
            return false;
        }

        if (action.HasDelta && !action.HasValidDelta)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит delta вне допустимого integer/null surface.";
            return false;
        }

        if (action.HasDirection && !action.HasValidDirection)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит direction вне допустимого string/null surface.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateActionFieldOwnership(
        InputAction action,
        InputActionContract actionContract,
        int index,
        out string? failureCode,
        out string? reason)
    {
        InputActionField presentFields = GetPresentFields(action);
        InputActionField unexpectedFields = presentFields & ~actionContract.AllowedFields;
        if (unexpectedFields != InputActionField.None)
        {
            InputActionField field = InputActionContractCatalog.EnumerateFields(unexpectedFields)[0];
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{action.Type}' не должно задавать поле {InputActionContractCatalog.GetJsonName(field)}.";
            return false;
        }

        InputActionField missingFields = actionContract.RequiredFields & ~presentFields;
        if (missingFields != InputActionField.None)
        {
            InputActionField field = InputActionContractCatalog.EnumerateFields(missingFields)[0];
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{action.Type}' должно содержать поле {InputActionContractCatalog.GetJsonName(field)}.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static InputActionField GetPresentFields(InputAction action)
    {
        InputActionField fields = InputActionField.None;

        if (action.HasType) fields |= InputActionField.Type;
        if (action.HasPoint) fields |= InputActionField.Point;
        if (action.HasPath) fields |= InputActionField.Path;
        if (action.HasCoordinateSpace) fields |= InputActionField.CoordinateSpace;
        if (action.HasButton) fields |= InputActionField.Button;
        if (action.HasKeys) fields |= InputActionField.Keys;
        if (action.HasText) fields |= InputActionField.Text;
        if (action.HasKey) fields |= InputActionField.Key;
        if (action.HasRepeat) fields |= InputActionField.Repeat;
        if (action.HasDelta) fields |= InputActionField.Delta;
        if (action.HasDirection) fields |= InputActionField.Direction;
        if (action.HasCaptureReference) fields |= InputActionField.CaptureReference;

        return fields;
    }

    private static bool TryValidateMoveAction(InputAction action, int index, out string? failureCode, out string? reason) =>
        TryValidatePointerPointAction(action, index, allowButton: false, out failureCode, out reason);

    private static bool TryValidateClickAction(InputAction action, int index, out string? failureCode, out string? reason)
    {
        if (!TryValidatePointerPointAction(action, index, allowButton: true, out failureCode, out reason))
        {
            return false;
        }

        if (action.HasButton
            && (!InputActionScalarConstraints.HasNonWhitespace(action.Button)
                || action.Button is null
                || !InputButtonValues.All.Contains(action.Button)))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] использует неподдерживаемую кнопку '{action.Button}'.";
            return false;
        }

        return true;
    }

    private static bool TryValidateDoubleClickAction(InputAction action, int index, out string? failureCode, out string? reason)
    {
        if (!TryValidatePointerPointAction(action, index, allowButton: false, out failureCode, out reason))
        {
            return false;
        }

        if (action.HasButton)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] не должно задавать button для type '{InputActionTypeValues.DoubleClick}'.";
            return false;
        }

        return true;
    }

    private static bool TryValidateDragAction(InputAction action, int index, out string? failureCode, out string? reason)
    {
        if (!action.HasPath || action.Path is null || action.Path.Count < 2)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Drag}' должно содержать path минимум из двух точек.";
            return false;
        }

        if (!TryValidateCoordinateBinding(action, index, out failureCode, out reason))
        {
            return false;
        }

        if (!TryValidateNonNullPointElements(action.Path, index, out failureCode, out reason))
        {
            return false;
        }

        if (action.HasPoint
            || action.HasButton
            || action.HasText
            || action.HasKey
            || action.HasRepeat
            || action.HasDelta
            || action.HasDirection)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Drag}' содержит поля, не входящие в contract drag.";
            return false;
        }

        if (!TryValidatePathPoints(action.Path, index, out failureCode, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateScrollAction(InputAction action, int index, out string? failureCode, out string? reason)
    {
        if (!TryValidateCoordinateBinding(action, index, out failureCode, out reason))
        {
            return false;
        }

        if (!action.HasPoint || action.Point is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Scroll}' должно содержать point.";
            return false;
        }

        if (action.Delta is null || action.Delta == InputActionScalarConstraints.InvalidScrollDelta)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Scroll}' должно содержать ненулевой delta.";
            return false;
        }

        if (!action.HasDirection || !InputActionScalarConstraints.HasNonWhitespace(action.Direction))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Scroll}' должно содержать direction.";
            return false;
        }

        if (action.HasPath
            || action.HasButton
            || action.HasText
            || action.HasKey
            || action.HasRepeat)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Scroll}' содержит поля, не входящие в contract scroll.";
            return false;
        }

        return true;
    }

    private static bool TryValidateTypeAction(InputAction action, int index, out string? failureCode, out string? reason)
    {
        if (!action.HasText || action.Text is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Type}' должно содержать text.";
            return false;
        }

        if (!TryValidateKeyboardOnlyAction(action, index, allowRepeat: false, out failureCode, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateKeypressAction(InputAction action, int index, out string? failureCode, out string? reason)
    {
        if (!action.HasKey || !InputActionScalarConstraints.HasNonWhitespace(action.Key))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Keypress}' должно содержать непустой key.";
            return false;
        }

        if (action.HasRepeat
            && (action.Repeat is null || action.Repeat.Value < InputActionScalarConstraints.MinimumRepeat))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{InputActionTypeValues.Keypress}' должно содержать repeat > 0, если repeat передан.";
            return false;
        }

        if (!TryValidateKeyboardOnlyAction(action, index, allowRepeat: true, out failureCode, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateKeyboardOnlyAction(
        InputAction action,
        int index,
        bool allowRepeat,
        out string? failureCode,
        out string? reason)
    {
        if (action.HasPoint
            || action.HasPath
            || action.HasCoordinateSpace
            || action.HasButton
            || action.HasKeys
            || action.HasDelta
            || action.HasDirection
            || action.HasCaptureReference)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит поля pointer-only, которые не входят в keyboard contract.";
            return false;
        }

        if (!allowRepeat && action.HasRepeat)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] не должно задавать repeat для type '{action.Type}'.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidatePointerPointAction(
        InputAction action,
        int index,
        bool allowButton,
        out string? failureCode,
        out string? reason)
    {
        if (!TryValidateCoordinateBinding(action, index, out failureCode, out reason))
        {
            return false;
        }

        if (!action.HasPoint || action.Point is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{action.Type}' должно содержать point.";
            return false;
        }

        if (action.HasPath
            || action.HasText
            || action.HasKey
            || action.HasRepeat
            || action.HasDelta
            || action.HasDirection)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{action.Type}' содержит поля, не входящие в contract pointer action.";
            return false;
        }

        if (!allowButton && action.HasButton)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{action.Type}' не должно задавать button.";
            return false;
        }

        return true;
    }

    private static bool TryValidateCoordinateBinding(
        InputAction action,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (!action.HasCoordinateSpace || string.IsNullOrWhiteSpace(action.CoordinateSpace))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] должно содержать coordinateSpace.";
            return false;
        }

        if (!InputCoordinateSpaceValues.All.Contains(action.CoordinateSpace))
        {
            failureCode = InputFailureCodeValues.UnsupportedCoordinateSpace;
            reason = $"Действие actions[{index}] использует неподдерживаемый coordinateSpace '{action.CoordinateSpace}'.";
            return false;
        }

        if (action.HasCaptureReference && action.CaptureReference is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference со значением null.";
            return false;
        }

        if (action.Point is not null
            && !TryValidatePointShape(action.Point, $"actions[{index}].point", out failureCode, out reason))
        {
            return false;
        }

        if (action.CaptureReference is not null
            && !action.CaptureReference.HasValidObject)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference не в форме JSON object.";
            return false;
        }

        if (action.CaptureReference is not null
            && (!action.CaptureReference.HasBounds || !action.CaptureReference.HasValidBounds || action.CaptureReference.Bounds is null))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference без bounds.";
            return false;
        }

        if (action.CaptureReference?.AdditionalProperties is { Count: > 0 })
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит расширенные поля внутри captureReference, которые не входят в текущий contract.";
            return false;
        }

        if (action.CaptureReference?.Bounds is not null
            && !TryValidateBoundsShape(action.CaptureReference.Bounds, index, out failureCode, out reason))
        {
            return false;
        }

        if (action.CaptureReference is not null
            && !TryValidateCaptureReferenceShape(action.CaptureReference, index, out failureCode, out reason))
        {
            return false;
        }

        if (string.Equals(action.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal))
        {
            if (!action.HasCaptureReference || action.CaptureReference is null)
            {
                failureCode = InputFailureCodeValues.CaptureReferenceRequired;
                reason = $"Действие actions[{index}] с coordinateSpace '{InputCoordinateSpaceValues.CapturePixels}' должно содержать captureReference.";
                return false;
            }

            failureCode = null;
            reason = null;
            return true;
        }

        if (action.HasCaptureReference)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] не должно задавать captureReference для coordinateSpace '{action.CoordinateSpace}'.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateNonNullActionElements(
        IReadOnlyList<InputAction> actions,
        out string? failureCode,
        out string? reason)
    {
        for (int index = 0; index < actions.Count; index++)
        {
            if (actions[index] is not null)
            {
                continue;
            }

            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] не должно быть null.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateNonNullPointElements(
        IReadOnlyList<InputPoint> points,
        int actionIndex,
        out string? failureCode,
        out string? reason)
    {
        for (int index = 0; index < points.Count; index++)
        {
            if (points[index] is not null)
            {
                continue;
            }

            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{actionIndex}].path[{index}] не должно быть null.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidatePathPoints(
        IReadOnlyList<InputPoint> points,
        int actionIndex,
        out string? failureCode,
        out string? reason)
    {
        for (int index = 0; index < points.Count; index++)
        {
            if (!TryValidatePointShape(points[index], $"actions[{actionIndex}].path[{index}]", out failureCode, out reason))
            {
                return false;
            }
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidatePointShape(
        InputPoint point,
        string fieldPath,
        out string? failureCode,
        out string? reason)
    {
        if (!point.HasValidObject)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Поле {fieldPath} должно быть JSON object.";
            return false;
        }

        if (point.AdditionalProperties is { Count: > 0 })
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Поле {fieldPath} содержит расширенные свойства, которые не входят в текущий contract.";
            return false;
        }

        if (!point.HasX || !point.HasY)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Поле {fieldPath} должно содержать и x, и y.";
            return false;
        }

        if (!point.HasValidX || !point.HasValidY)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Поле {fieldPath} должно задавать x и y как целые числа без null placeholder.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateBoundsShape(
        InputBounds bounds,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (!bounds.HasValidObject)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference.bounds не в форме JSON object.";
            return false;
        }

        if (bounds.AdditionalProperties is { Count: > 0 })
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит расширенные поля внутри captureReference.bounds, которые не входят в текущий contract.";
            return false;
        }

        if (!bounds.HasLeft || !bounds.HasTop || !bounds.HasRight || !bounds.HasBottom)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference.bounds без полного набора left/top/right/bottom.";
            return false;
        }

        if (!bounds.HasValidLeft || !bounds.HasValidTop || !bounds.HasValidRight || !bounds.HasValidBottom)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference.bounds с null или нецелочисленным edge value.";
            return false;
        }

        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference с некорректными bounds.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateCaptureReferenceShape(
        InputCaptureReference captureReference,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (!captureReference.HasPixelWidth || !captureReference.HasPixelHeight)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] должно задавать captureReference.pixelWidth и pixelHeight.";
            return false;
        }

        if (!captureReference.HasValidPixelWidth || !captureReference.HasValidPixelHeight)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference с null или нецелочисленной geometry.";
            return false;
        }

        if (captureReference.PixelWidth < InputActionScalarConstraints.MinimumCapturePixelDimension
            || captureReference.PixelHeight < InputActionScalarConstraints.MinimumCapturePixelDimension)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит некорректную captureReference geometry.";
            return false;
        }

        if (captureReference.HasEffectiveDpi && !captureReference.HasValidEffectiveDpi)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference.effectiveDpi вне допустимого integer/null surface.";
            return false;
        }

        if (captureReference.HasCapturedAtUtc && !captureReference.HasValidCapturedAtUtc)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит captureReference.capturedAtUtc вне допустимого datetime/null surface.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool TryValidateModifierKeys(
        InputAction action,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (!action.HasKeys)
        {
            failureCode = null;
            reason = null;
            return true;
        }

        if (action.Keys is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] должно передавать keys как массив modifier key, если поле keys задано.";
            return false;
        }

        if (action.Keys.Any(static key => !InputActionScalarConstraints.HasNonWhitespace(key)))
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит пустой modifier key.";
            return false;
        }

        string? unsupportedKey = action.Keys.FirstOrDefault(key => !InputModifierKeyValues.All.Contains(key.Trim()));
        if (unsupportedKey is not null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] содержит неподдерживаемый modifier '{unsupportedKey}'.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }
}
