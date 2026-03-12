namespace WinBridge.Runtime.Contracts;

public sealed record AttachWindowResult(
    string Status,
    string? Reason,
    AttachedWindow? AttachedWindow,
    SessionSnapshot Session);
