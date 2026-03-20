namespace WinBridge.Runtime.Contracts;

public sealed record WaitResult(
    string Status,
    string Condition,
    string? Reason = null,
    int TimeoutMs = WaitDefaults.TimeoutMs,
    int ElapsedMs = 0,
    int AttemptCount = 0);
