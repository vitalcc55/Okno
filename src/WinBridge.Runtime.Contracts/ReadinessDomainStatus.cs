namespace WinBridge.Runtime.Contracts;

public sealed record ReadinessDomainStatus(
    string Domain,
    string Status,
    IReadOnlyList<GuardReason> Reasons);
