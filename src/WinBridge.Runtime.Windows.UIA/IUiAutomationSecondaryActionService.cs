// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public static class UiaSecondaryActionKindValues
{
    public const string Toggle = "toggle";
    public const string ExpandCollapse = "expand_collapse";
}

public static class UiaSecondaryActionFailureKindValues
{
    public const string MissingElement = "missing_element";
    public const string UnsupportedPattern = "unsupported_pattern";
    public const string NoStateChange = "no_state_change";
    public const string DispatchFailed = "dispatch_failed";
}

public sealed record UiaSecondaryActionRequest(
    string ElementId,
    string ActionKind);

public sealed record UiaSecondaryActionResult(
    bool Success,
    string ActionKind,
    string? FailureKind = null,
    string? Reason = null,
    string? ResolvedPattern = null)
{
    public static UiaSecondaryActionResult SuccessResult(string actionKind, string resolvedPattern) =>
        new(true, actionKind, null, null, resolvedPattern);

    public static UiaSecondaryActionResult FailureResult(
        string actionKind,
        string failureKind,
        string reason,
        string? resolvedPattern = null) =>
        new(false, actionKind, failureKind, reason, resolvedPattern);
}

public interface IUiAutomationSecondaryActionService
{
    Task<UiaSecondaryActionResult> ExecuteAsync(
        WindowDescriptor targetWindow,
        UiaSecondaryActionRequest request,
        CancellationToken cancellationToken);
}
