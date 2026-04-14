namespace WinBridge.Runtime.Contracts;

public sealed record InputActionResult(
    string Type,
    string Status,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    string? CoordinateSpace = null,
    InputPoint? RequestedPoint = null,
    InputPoint? ResolvedScreenPoint = null,
    string? Button = null,
    IReadOnlyList<string>? Keys = null);
