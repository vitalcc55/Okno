namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputTargetSecurityInfo(
    int? ProcessId,
    int? SessionId,
    bool SessionResolved,
    InputIntegrityLevel? IntegrityLevel,
    bool IntegrityResolved,
    string? Reason);
