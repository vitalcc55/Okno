namespace WinBridge.Runtime.Contracts;

public sealed record InputTargetResolution(
    WindowDescriptor? Window = null,
    string? Source = null,
    string? FailureCode = null);
