namespace WinBridge.Runtime.Contracts;

public sealed record WaitRequest(
    string Condition,
    WaitElementSelector? Selector = null,
    string? ExpectedText = null,
    int TimeoutMs = WaitDefaults.TimeoutMs);
