namespace WinBridge.Runtime.Contracts;

public sealed record WaitTargetResolution(
    WindowDescriptor? Window = null,
    string? Source = null,
    string? FailureCode = null);
