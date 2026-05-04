// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public static class UiaScrollDirectionValues
{
    public const string Up = "up";
    public const string Down = "down";
    public const string Left = "left";
    public const string Right = "right";
}

public static class UiaScrollFailureKindValues
{
    public const string MissingElement = "missing_element";
    public const string UnsupportedPattern = "unsupported_pattern";
    public const string NoMovement = "no_movement";
    public const string DispatchFailed = "dispatch_failed";
}

public sealed record UiaScrollRequest(
    string ElementId,
    string Direction,
    int Pages);

public sealed record UiaScrollResult(
    bool Success,
    bool MovementObserved,
    string? FailureKind = null,
    string? Reason = null,
    string? ResolvedPattern = null)
{
    public static UiaScrollResult SuccessResult(string resolvedPattern, bool movementObserved) =>
        new(true, movementObserved, null, null, resolvedPattern);

    public static UiaScrollResult FailureResult(
        string failureKind,
        string reason,
        string? resolvedPattern = null) =>
        new(false, false, failureKind, reason, resolvedPattern);
}

public interface IUiAutomationScrollService
{
    Task<UiaScrollResult> ScrollAsync(
        WindowDescriptor targetWindow,
        UiaScrollRequest request,
        CancellationToken cancellationToken);
}
