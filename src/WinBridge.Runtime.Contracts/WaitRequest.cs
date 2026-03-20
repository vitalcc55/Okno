namespace WinBridge.Runtime.Contracts;

public sealed record WaitRequest(
    string Condition,
    string? Selector = null,
    int TimeoutMs = WaitDefaults.TimeoutMs);
