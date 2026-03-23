namespace WinBridge.Runtime.Contracts;

public sealed record WaitElementSelector(
    string? Name = null,
    string? AutomationId = null,
    string? ControlType = null);
