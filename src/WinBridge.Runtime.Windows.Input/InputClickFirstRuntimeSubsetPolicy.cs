using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputClickFirstRuntimeSubsetPolicy
{
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

    private static bool TryValidateAction(
        InputAction action,
        int index,
        out string? failureCode,
        out string? reason)
    {
        if (action.Keys is { Count: > 0 })
        {
            failureCode = InputFailureCodeValues.InvalidRequest;
            reason = $"Действие actions[{index}] использует keys[], которые не входят в click-first runtime subset Package B.";
            return false;
        }

        if (string.Equals(action.Type, InputActionTypeValues.Click, StringComparison.Ordinal))
        {
            string button = string.IsNullOrWhiteSpace(action.Button)
                ? InputButtonValues.Left
                : action.Button!;
            if (string.Equals(button, InputButtonValues.Middle, StringComparison.Ordinal))
            {
                failureCode = InputFailureCodeValues.InvalidRequest;
                reason = $"Действие actions[{index}] использует click(button=middle), который не входит в click-first runtime subset Package B.";
                return false;
            }
        }

        failureCode = null;
        reason = null;
        return true;
    }
}
