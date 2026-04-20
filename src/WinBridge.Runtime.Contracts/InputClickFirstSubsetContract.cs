namespace WinBridge.Runtime.Contracts;

public static class InputClickFirstSubsetContract
{
    public static IReadOnlyList<InputActionContract> Actions { get; } =
    [
        new(
            InputActionTypeValues.Move,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.Click,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.Button | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.DoubleClick,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.CaptureReference),
    ];

    public static IReadOnlyList<string> SupportedActionTypes { get; } =
        Actions.Select(item => item.ActionType).ToArray();

    public static bool TryValidateRequest(
        InputRequest request,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (int index = 0; index < request.Actions.Count; index++)
        {
            if (!TryValidateAction(request.Actions[index], index, out failureCode, out reason))
            {
                return false;
            }
        }

        failureCode = null;
        reason = null;
        return true;
    }

    public static bool TryGetActionContract(string? actionType, out InputActionContract contract)
    {
        foreach (InputActionContract item in Actions)
        {
            if (string.Equals(item.ActionType, actionType, StringComparison.Ordinal))
            {
                contract = item;
                return true;
            }
        }

        contract = null!;
        return false;
    }

    public static InputActionField GetAllActionFields()
    {
        InputActionField result = InputActionField.None;
        foreach (InputActionContract contract in Actions)
        {
            result |= contract.AllowedFields;
        }

        return result;
    }

    public static object CreateAuditRequestSummary(InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int actionCount = request.Actions.Count;

        return new
        {
            hwnd = request.HasHwnd ? request.Hwnd : null,
            confirm = request.HasConfirm ? request.Confirm : (bool?)null,
            actionCount,
            truncated = actionCount > InputRequestValidator.MaxActionCount,
            actions = request.Actions
                .Take(InputRequestValidator.MaxActionCount)
                .Select(CreateAuditActionSummary)
                .ToArray(),
        };
    }

    private static bool TryValidateAction(
        InputAction? action,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (action is null)
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] не должно быть null.";
            return false;
        }

        if (!TryGetActionContract(action.Type, out InputActionContract contract))
        {
            failureCode = InputFailureCodeValues.UnsupportedActionType;
            reason = $"Действие actions[{index}] с type '{action.Type}' не входит в текущий click-first subset windows.input.";
            return false;
        }

        InputActionField presentFields = GetPresentFields(action);
        InputActionField unexpectedFields = presentFields & ~contract.AllowedFields;
        if (unexpectedFields != InputActionField.None)
        {
            InputActionField field = InputActionContractCatalog.EnumerateFields(unexpectedFields)[0];
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] с type '{action.Type}' не должно задавать поле {InputActionContractCatalog.GetJsonName(field)} в current click-first subset windows.input.";
            return false;
        }

        if (string.Equals(action.Type, InputActionTypeValues.Click, StringComparison.Ordinal))
        {
            string button = string.IsNullOrWhiteSpace(action.Button)
                ? InputButtonValues.Left
                : action.Button!;
            if (!string.Equals(button, InputButtonValues.Left, StringComparison.Ordinal)
                && !string.Equals(button, InputButtonValues.Right, StringComparison.Ordinal))
            {
                failureCode = InputFailureCodeValues.InvalidRequest;
                reason = $"Действие actions[{index}] использует click(button={button}), который не входит в текущий click-first subset windows.input.";
                return false;
            }
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

    private static IReadOnlyDictionary<string, object?> CreateAuditActionSummary(InputAction? action)
    {
        string? safeActionType = TryGetSafeActionType(action);
        Dictionary<string, object?> summary = new(StringComparer.Ordinal)
        {
            ["type"] = safeActionType,
        };

        if (action is null || !TryGetActionContract(safeActionType, out InputActionContract contract))
        {
            return summary;
        }

        foreach (InputActionField field in InputActionContractCatalog.EnumerateFields(contract.AllowedFields))
        {
            switch (field)
            {
                case InputActionField.Point when action.HasPoint:
                    summary["point"] = CreateAuditPointSummary(action.Point);
                    break;
                case InputActionField.CoordinateSpace when action.HasCoordinateSpace:
                    summary["coordinateSpace"] = TryGetSafeCoordinateSpace(action);
                    break;
                case InputActionField.Button when action.HasButton:
                    summary["button"] = TryGetSafeButton(action);
                    break;
                case InputActionField.CaptureReference when action.HasCaptureReference:
                    summary["captureReference"] = CreateAuditCaptureReferenceSummary(action.CaptureReference);
                    break;
            }
        }

        return summary;
    }

    private static string? TryGetSafeActionType(InputAction? action)
    {
        if (action is null || !action.HasType || !action.HasValidType)
        {
            return null;
        }

        return TryGetActionContract(action.Type, out _)
            ? action.Type
            : null;
    }

    private static string? TryGetSafeCoordinateSpace(InputAction action)
    {
        if (!action.HasCoordinateSpace
            || !action.HasValidCoordinateSpace
            || string.IsNullOrWhiteSpace(action.CoordinateSpace))
        {
            return null;
        }

        return InputCoordinateSpaceValues.All.Contains(action.CoordinateSpace)
            ? action.CoordinateSpace
            : null;
    }

    private static string? TryGetSafeButton(InputAction action)
    {
        if (!action.HasButton
            || !action.HasValidButton
            || string.IsNullOrWhiteSpace(action.Button))
        {
            return null;
        }

        return string.Equals(action.Button, InputButtonValues.Left, StringComparison.Ordinal)
            || string.Equals(action.Button, InputButtonValues.Right, StringComparison.Ordinal)
            ? action.Button
            : null;
    }

    private static object? CreateAuditPointSummary(InputPoint? point)
    {
        if (point is null)
        {
            return null;
        }

        return new
        {
            x = point.HasX && point.HasValidX ? (int?)point.X : null,
            y = point.HasY && point.HasValidY ? (int?)point.Y : null,
        };
    }

    private static object? CreateAuditCaptureReferenceSummary(InputCaptureReference? captureReference)
    {
        if (captureReference is null)
        {
            return null;
        }

        return new
        {
            bounds = CreateAuditBoundsSummary(captureReference.Bounds),
            pixelWidth = captureReference.HasPixelWidth && captureReference.HasValidPixelWidth ? (int?)captureReference.PixelWidth : null,
            pixelHeight = captureReference.HasPixelHeight && captureReference.HasValidPixelHeight ? (int?)captureReference.PixelHeight : null,
            effectiveDpi = captureReference.HasEffectiveDpi && captureReference.HasValidEffectiveDpi ? captureReference.EffectiveDpi : null,
            capturedAtUtc = captureReference.HasCapturedAtUtc && captureReference.HasValidCapturedAtUtc ? captureReference.CapturedAtUtc : null,
            frameBounds = captureReference.HasFrameBounds && captureReference.HasValidFrameBounds ? CreateAuditBoundsSummary(captureReference.FrameBounds) : null,
            targetIdentity = captureReference.HasTargetIdentity && captureReference.HasValidTargetIdentity ? CreateAuditTargetIdentitySummary(captureReference.TargetIdentity) : null,
        };
    }

    private static object? CreateAuditBoundsSummary(InputBounds? bounds)
    {
        if (bounds is null)
        {
            return null;
        }

        return new
        {
            left = bounds.HasLeft && bounds.HasValidLeft ? (int?)bounds.Left : null,
            top = bounds.HasTop && bounds.HasValidTop ? (int?)bounds.Top : null,
            right = bounds.HasRight && bounds.HasValidRight ? (int?)bounds.Right : null,
            bottom = bounds.HasBottom && bounds.HasValidBottom ? (int?)bounds.Bottom : null,
        };
    }

    private static object? CreateAuditTargetIdentitySummary(InputTargetIdentity? targetIdentity)
    {
        if (targetIdentity is null)
        {
            return null;
        }

        return new
        {
            hwnd = targetIdentity.HasHwnd && targetIdentity.HasValidHwnd ? (long?)targetIdentity.Hwnd : null,
            processId = targetIdentity.HasProcessId && targetIdentity.HasValidProcessId ? (int?)targetIdentity.ProcessId : null,
            threadId = targetIdentity.HasThreadId && targetIdentity.HasValidThreadId ? (int?)targetIdentity.ThreadId : null,
            className = targetIdentity.HasClassName && targetIdentity.HasValidClassName ? targetIdentity.ClassName : null,
        };
    }
}
