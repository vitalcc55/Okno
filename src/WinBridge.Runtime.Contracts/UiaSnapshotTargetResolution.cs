namespace WinBridge.Runtime.Contracts;

public sealed record UiaSnapshotTargetResolution(
    WindowDescriptor? Window = null,
    string? Source = null,
    string? FailureCode = null);
