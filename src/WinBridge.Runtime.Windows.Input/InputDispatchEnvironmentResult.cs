// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputDispatchEnvironmentResult
{
    private InputDispatchEnvironmentResult(
        bool success,
        string? failureCode,
        string? reason,
        bool mouseButtonsSwapped)
    {
        Success = success;
        FailureCode = failureCode;
        Reason = reason;
        MouseButtonsSwapped = mouseButtonsSwapped;
    }

    public bool Success { get; }

    public string? FailureCode { get; }

    public string? Reason { get; }

    public bool MouseButtonsSwapped { get; }

    public static InputDispatchEnvironmentResult Succeeded(bool mouseButtonsSwapped) =>
        new(
            success: true,
            failureCode: null,
            reason: null,
            mouseButtonsSwapped: mouseButtonsSwapped);

    public static InputDispatchEnvironmentResult Failure(string failureCode, string reason) =>
        new(
            success: false,
            failureCode: ValidateFailureCode(failureCode),
            reason: ValidateReason(reason),
            mouseButtonsSwapped: false);

    private static string ValidateFailureCode(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return failureCode;
    }

    private static string ValidateReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return reason;
    }
}
