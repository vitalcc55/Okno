namespace WinBridge.Runtime.Contracts;

public static class ActivateWindowStatusValues
{
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Ambiguous = "ambiguous";
}

public static class ActivationFailureKindValues
{
    public const string MissingTarget = "missing_target";
    public const string IdentityChanged = "identity_changed";
    public const string IdentityProofUnavailable = "identity_proof_unavailable";
    public const string RestoreFailedStillMinimized = "restore_failed_still_minimized";
    public const string ForegroundNotConfirmed = "foreground_not_confirmed";
    public const string PreflightFailed = "activation_preflight_failed";

    public static bool IsKnown(string? failureKind) =>
        failureKind is
            MissingTarget or
            IdentityChanged or
            IdentityProofUnavailable or
            RestoreFailedStillMinimized or
            ForegroundNotConfirmed or
            PreflightFailed;
}

public sealed record ActivateWindowResult
{
    private ActivateWindowResult(
        string status,
        string? reason,
        WindowDescriptor? window,
        bool wasMinimized,
        bool isForeground,
        string? failureKind)
    {
        if (status is not (ActivateWindowStatusValues.Done or ActivateWindowStatusValues.Failed or ActivateWindowStatusValues.Ambiguous))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Неизвестный activation status.");
        }

        if (status == ActivateWindowStatusValues.Done)
        {
            if (window is null)
            {
                throw new ArgumentException("Успешный activation result должен содержать resolved window.", nameof(window));
            }

            if (failureKind is not null)
            {
                throw new ArgumentException("Успешный activation result не должен содержать failureKind.", nameof(failureKind));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Non-success activation result должен содержать reason.", nameof(reason));
            }

            if (!ActivationFailureKindValues.IsKnown(failureKind))
            {
                throw new ArgumentException("Non-success activation result должен содержать известный failureKind.", nameof(failureKind));
            }
        }

        Status = status;
        Reason = reason;
        Window = window;
        WasMinimized = wasMinimized;
        IsForeground = isForeground;
        FailureKind = failureKind;
    }

    public string Status { get; }

    public string? Reason { get; }

    public WindowDescriptor? Window { get; }

    public bool WasMinimized { get; }

    public bool IsForeground { get; }

    public string? FailureKind { get; }

    public static ActivateWindowResult Done(WindowDescriptor window, bool wasMinimized, bool isForeground) =>
        new(
            ActivateWindowStatusValues.Done,
            reason: null,
            window,
            wasMinimized,
            isForeground,
            failureKind: null);

    public static ActivateWindowResult Failed(string reason, bool wasMinimized, string failureKind) =>
        Failed(reason, window: null, wasMinimized: wasMinimized, isForeground: false, failureKind: failureKind);

    public static ActivateWindowResult Failed(
        string reason,
        WindowDescriptor? window,
        bool wasMinimized,
        bool isForeground,
        string failureKind) =>
        new(
            ActivateWindowStatusValues.Failed,
            reason,
            window,
            wasMinimized,
            isForeground,
            failureKind);

    public static ActivateWindowResult Ambiguous(
        string reason,
        WindowDescriptor? window,
        bool wasMinimized,
        bool isForeground,
        string failureKind) =>
        new(
            ActivateWindowStatusValues.Ambiguous,
            reason,
            window,
            wasMinimized,
            isForeground,
            failureKind);
}
