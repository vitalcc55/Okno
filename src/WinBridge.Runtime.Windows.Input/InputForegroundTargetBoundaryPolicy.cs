using WinBridge.Runtime.Contracts;

using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputForegroundTargetBoundaryPolicy
{
    public static bool TryValidate(
        long? foregroundHwnd,
        ActivatedWindowVerificationSnapshot foregroundSnapshot,
        WindowDescriptor admittedTargetWindow,
        out int? validatedForegroundOwnerProcessId,
        out string? failureCode,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(admittedTargetWindow);

        validatedForegroundOwnerProcessId = null;
        if (foregroundHwnd is not long liveForegroundHwnd)
        {
            failureCode = InputFailureCodeValues.TargetNotForeground;
            reason = "Runtime не смог подтвердить foreground window непосредственно перед click dispatch.";
            return false;
        }

        if (liveForegroundHwnd != admittedTargetWindow.Hwnd)
        {
            failureCode = InputFailureCodeValues.TargetNotForeground;
            reason = "Admitted target больше не является foreground window на final dispatch boundary.";
            return false;
        }

        if (!WindowIdentityValidator.MatchesStableIdentity(foregroundSnapshot, admittedTargetWindow))
        {
            failureCode = InputFailureCodeValues.TargetNotForeground;
            reason = "Foreground window на final dispatch boundary больше не совпадает со stable identity admitted target.";
            return false;
        }

        validatedForegroundOwnerProcessId = foregroundSnapshot.ProcessId;
        failureCode = null;
        reason = null;
        return true;
    }
}
