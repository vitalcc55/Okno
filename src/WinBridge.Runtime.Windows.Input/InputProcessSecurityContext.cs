namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputProcessSecurityContext(
    int? SessionId,
    bool SessionResolved,
    InputIntegrityLevel? IntegrityLevel,
    bool IntegrityResolved,
    bool HasUiAccess,
    bool UiAccessResolved,
    string? Reason);
