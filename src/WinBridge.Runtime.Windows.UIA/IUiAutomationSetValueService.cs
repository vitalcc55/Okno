// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public static class UiaSetValueKindValues
{
    public const string Text = "text";
    public const string Number = "number";
}

public static class UiaSetValueFailureKindValues
{
    public const string MissingElement = "missing_element";
    public const string UnsupportedPattern = "unsupported_pattern";
    public const string ReadOnly = "read_only";
    public const string ValueOutOfRange = "value_out_of_range";
    public const string InvalidValue = "invalid_value";
    public const string DispatchFailed = "dispatch_failed";
}

public sealed record UiaSetValueRequest(
    string ElementId,
    string ValueKind,
    string? TextValue = null,
    double? NumberValue = null);

public sealed record UiaSetValueResult(
    bool Success,
    string? FailureKind = null,
    string? Reason = null,
    string? ResolvedPattern = null)
{
    public static UiaSetValueResult SuccessResult(string resolvedPattern) =>
        new(true, null, null, resolvedPattern);

    public static UiaSetValueResult FailureResult(
        string failureKind,
        string reason,
        string? resolvedPattern = null) =>
        new(false, failureKind, reason, resolvedPattern);
}

public interface IUiAutomationSetValueService
{
    Task<UiaSetValueResult> SetValueAsync(
        WindowDescriptor targetWindow,
        UiaSetValueRequest request,
        CancellationToken cancellationToken);
}
