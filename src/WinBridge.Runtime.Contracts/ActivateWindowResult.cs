namespace WinBridge.Runtime.Contracts;

public static class ActivationFailureKindValues
{
    public const string MissingTarget = "missing_target";
    public const string IdentityChanged = "identity_changed";
    public const string RestoreFailedStillMinimized = "restore_failed_still_minimized";
    public const string ForegroundNotConfirmed = "foreground_not_confirmed";
    public const string PreflightFailed = "activation_preflight_failed";
}

public sealed record ActivateWindowResult(
    string Status,
    string? Reason,
    WindowDescriptor? Window,
    bool WasMinimized,
    bool IsForeground,
    string? FailureKind = null);
