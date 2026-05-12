// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputTargetPreflightResult
{
    private InputTargetPreflightResult(
        bool isAllowed,
        string? failureCode,
        string? reason)
    {
        IsAllowed = isAllowed;
        FailureCode = failureCode;
        Reason = reason;
    }

    public bool IsAllowed { get; }

    public string? FailureCode { get; }

    public string? Reason { get; }

    public static InputTargetPreflightResult Allowed() =>
        new(
            isAllowed: true,
            failureCode: null,
            reason: null);

    public static InputTargetPreflightResult Failure(string failureCode, string reason) =>
        new(
            isAllowed: false,
            failureCode: ValidateFailureCode(failureCode),
            reason: ValidateReason(reason));

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
