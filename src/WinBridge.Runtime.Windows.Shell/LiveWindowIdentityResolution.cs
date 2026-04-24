using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public sealed record LiveWindowIdentityResolution(
    WindowDescriptor? Window,
    string? FailureKind,
    string? Reason)
{
    public bool IsResolved => Window is not null;

    public static LiveWindowIdentityResolution Resolved(WindowDescriptor window) =>
        new(window, FailureKind: null, Reason: null);

    public static LiveWindowIdentityResolution MissingTarget(string reason) =>
        new(Window: null, FailureKind: ActivationFailureKindValues.MissingTarget, Reason: reason);

    public static LiveWindowIdentityResolution IdentityChanged(string reason) =>
        new(Window: null, FailureKind: ActivationFailureKindValues.IdentityChanged, Reason: reason);

    public static LiveWindowIdentityResolution IdentityProofUnavailable(string reason) =>
        new(Window: null, FailureKind: ActivationFailureKindValues.IdentityProofUnavailable, Reason: reason);
}
