// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class ComputerUseWinStatusValues
{
    public const string Ok = "ok";
    public const string Done = "done";
    public const string VerifyNeeded = "verify_needed";
    public const string Failed = "failed";
    public const string ApprovalRequired = "approval_required";
    public const string Blocked = "blocked";
}

public static class ComputerUseWinFailureCodeValues
{
    public const string InvalidRequest = "invalid_request";
    public const string MissingTarget = "missing_target";
    public const string AmbiguousTarget = "ambiguous_target";
    public const string ApprovalRequired = "approval_required";
    public const string BlockedTarget = "blocked_target";
    public const string IdentityProofUnavailable = "identity_proof_unavailable";
    public const string StateRequired = "state_required";
    public const string StaleState = "stale_state";
    public const string ObservationFailed = "observation_failed";
    public const string UnsupportedAction = "unsupported_action";
    public const string UnexpectedInternalFailure = "unexpected_internal_failure";
    public const string CaptureReferenceRequired = InputFailureCodeValues.CaptureReferenceRequired;
    public const string TargetPreflightFailed = InputFailureCodeValues.TargetPreflightFailed;
    public const string TargetNotForeground = InputFailureCodeValues.TargetNotForeground;
    public const string TargetMinimized = InputFailureCodeValues.TargetMinimized;
    public const string TargetIntegrityBlocked = InputFailureCodeValues.TargetIntegrityBlocked;
    public const string PointOutOfBounds = InputFailureCodeValues.PointOutOfBounds;
    public const string CursorMoveFailed = InputFailureCodeValues.CursorMoveFailed;
    public const string InputDispatchFailed = InputFailureCodeValues.InputDispatchFailed;
}

public sealed record ComputerUseWinWindowDescriptor(
    string WindowId,
    long Hwnd,
    string Title,
    string? ProcessName,
    int? ProcessId,
    bool IsForeground,
    bool IsVisible);

public sealed record ComputerUseWinAppDescriptor(
    string AppId,
    IReadOnlyList<ComputerUseWinWindowDescriptor> Windows,
    bool IsApproved,
    bool IsBlocked,
    string? BlockReason = null);

public sealed record ComputerUseWinListAppsResult(
    string Status,
    IReadOnlyList<ComputerUseWinAppDescriptor> Apps,
    int Count,
    string? FailureCode = null,
    string? Reason = null);

public sealed record ComputerUseWinGetAppStateRequest(
    string? WindowId = null,
    long? Hwnd = null,
    bool Confirm = false,
    int MaxNodes = 128);

public sealed record ComputerUseWinAppSession(
    string AppId,
    string? WindowId,
    long Hwnd,
    string Title,
    string? ProcessName,
    int? ProcessId);

public sealed record ComputerUseWinAccessibilityElement(
    int Index,
    string ElementId,
    string? Name,
    string? AutomationId,
    string ControlType,
    Bounds? Bounds,
    bool HasKeyboardFocus,
    IReadOnlyList<string> Actions);

public sealed record ComputerUseWinGetAppStateResult(
    string Status,
    ComputerUseWinAppSession? Session = null,
    string? StateToken = null,
    CaptureMetadata? Capture = null,
    IReadOnlyList<ComputerUseWinAccessibilityElement>? AccessibilityTree = null,
    IReadOnlyList<string>? Instructions = null,
    IReadOnlyList<string>? Warnings = null,
    bool ApprovalRequired = false,
    string? FailureCode = null,
    string? Reason = null);

public sealed record ComputerUseWinClickRequest(
    string? StateToken = null,
    int? ElementIndex = null,
    InputPoint? Point = null,
    string? CoordinateSpace = null,
    string? Button = null,
    bool Confirm = false,
    bool ObserveAfter = false);

public sealed record ComputerUseWinTypeTextRequest(
    string? StateToken = null,
    int? ElementIndex = null,
    InputPoint? Point = null,
    string? CoordinateSpace = null,
    string? Text = null,
    bool Confirm = false,
    bool AllowFocusedFallback = false,
    bool ObserveAfter = false);

public sealed record ComputerUseWinPressKeyRequest(
    string? StateToken = null,
    string? Key = null,
    int? Repeat = null,
    bool Confirm = false,
    bool ObserveAfter = false);

public sealed record ComputerUseWinSetValueRequest(
    string? StateToken = null,
    int? ElementIndex = null,
    string? ValueKind = null,
    string? TextValue = null,
    double? NumberValue = null,
    bool Confirm = false);

public sealed record ComputerUseWinScrollRequest(
    string? StateToken = null,
    int? ElementIndex = null,
    InputPoint? Point = null,
    string? CoordinateSpace = null,
    string? Direction = null,
    int? Pages = null,
    bool Confirm = false,
    bool ObserveAfter = false);

public sealed record ComputerUseWinPerformSecondaryActionRequest(
    string? StateToken = null,
    int? ElementIndex = null,
    bool Confirm = false);

public sealed record ComputerUseWinDragRequest(
    string? StateToken = null,
    int? FromElementIndex = null,
    InputPoint? FromPoint = null,
    int? ToElementIndex = null,
    InputPoint? ToPoint = null,
    string? CoordinateSpace = null,
    bool Confirm = false,
    bool ObserveAfter = false);

public sealed record ComputerUseWinActionSuccessorStateFailure(
    string FailureCode,
    string Reason);

public sealed record ComputerUseWinActionResult(
    string Status,
    string? StateToken = null,
    bool RefreshStateRecommended = true,
    string? FailureCode = null,
    string? Reason = null,
    long? TargetHwnd = null,
    int? ElementIndex = null,
    ComputerUseWinGetAppStateResult? SuccessorState = null,
    ComputerUseWinActionSuccessorStateFailure? SuccessorStateFailure = null);
