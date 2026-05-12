// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal enum InputClickDispatchOutcomeKind
{
    Success,
    NotAttempted,
    CleanFailure,
    PartialDispatchCompensated,
    PartialDispatchUncompensated,
}

internal sealed record InputClickDispatchResult
{
    private InputClickDispatchResult(
        bool success,
        bool committedSideEffects,
        InputClickDispatchOutcomeKind outcomeKind,
        string? failureCode,
        string? reason,
        string? failureStageHint)
    {
        Success = success;
        CommittedSideEffects = committedSideEffects;
        OutcomeKind = outcomeKind;
        FailureCode = failureCode;
        Reason = reason;
        FailureStageHint = failureStageHint;
    }

    public bool Success { get; }

    public bool CommittedSideEffects { get; }

    public InputClickDispatchOutcomeKind OutcomeKind { get; }

    public string? FailureCode { get; }

    public string? Reason { get; }

    public string? FailureStageHint { get; }

    public static InputClickDispatchResult Succeeded() =>
        new(
            success: true,
            committedSideEffects: true,
            outcomeKind: InputClickDispatchOutcomeKind.Success,
            failureCode: null,
            reason: null,
            failureStageHint: null);

    public static InputClickDispatchResult CleanFailure(string failureCode, string reason) =>
        CreateFailure(
            InputClickDispatchOutcomeKind.CleanFailure,
            failureCode,
            reason);

    public static InputClickDispatchResult PreDispatchFailure(string failureCode, string reason) =>
        CreateFailure(
            InputClickDispatchOutcomeKind.NotAttempted,
            failureCode,
            reason);

    public static InputClickDispatchResult PartialDispatchCompensated(string failureCode, string reason) =>
        CreateFailure(
            InputClickDispatchOutcomeKind.PartialDispatchCompensated,
            failureCode,
            reason);

    public static InputClickDispatchResult PartialDispatchUncompensated(string failureCode, string reason) =>
        CreateFailure(
            InputClickDispatchOutcomeKind.PartialDispatchUncompensated,
            failureCode,
            reason);

    private static InputClickDispatchResult CreateFailure(
        InputClickDispatchOutcomeKind outcomeKind,
        string failureCode,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new(
            success: false,
            committedSideEffects: outcomeKind is InputClickDispatchOutcomeKind.PartialDispatchCompensated
                or InputClickDispatchOutcomeKind.PartialDispatchUncompensated,
            outcomeKind: outcomeKind,
            failureCode: failureCode,
            reason: reason,
            failureStageHint: InputFailureStagePolicy.MapClickDispatchFailureStage(outcomeKind));
    }
}
