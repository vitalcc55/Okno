// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinPublicFailureMaterializer
{
    public static ComputerUseWinFailureDetails MaterializeStateFailure(ComputerUseWinFailureDetails failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        ComputerUseWinFailureTranslation publicFailure = ComputerUseWinFailureCodeMapper.ToPublicFailure(
            failure.FailureCode,
            failure.Reason);
        return failure with
        {
            FailureCode = publicFailure.FailureCode ?? failure.FailureCode,
            Reason = publicFailure.Reason ?? failure.Reason,
        };
    }

    public static ComputerUseWinFailureTranslation MaterializeActionFailure(string failureCode, string reason) =>
        ShouldSanitizeStructuredActionReason(failureCode)
            ? ComputerUseWinFailureCodeMapper.ToPublicFailure(failureCode, reason)
            : new ComputerUseWinFailureTranslation(failureCode, reason);

    public static ComputerUseWinFailureTranslation MaterializeRuntimeFailure(string? failureCode, string? reason) =>
        ComputerUseWinFailureCodeMapper.ToPublicFailure(failureCode, reason);

    public static ComputerUseWinActionSuccessorStateFailure MaterializeSuccessorStateFailure(
        string? failureCode,
        string? reason,
        string fallbackFailureCode,
        string fallbackReason)
    {
        ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(failureCode, reason);
        return new ComputerUseWinActionSuccessorStateFailure(
            failure.FailureCode ?? fallbackFailureCode,
            failure.Reason ?? fallbackReason);
    }

    public static ComputerUseWinActionSuccessorStateFailure MaterializeSuccessorStateFailure(
        ComputerUseWinFailureDetails failure,
        string fallbackFailureCode,
        string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return MaterializeSuccessorStateFailure(
            failure.FailureCode,
            failure.Reason,
            fallbackFailureCode,
            fallbackReason);
    }

    private static bool ShouldSanitizeStructuredActionReason(string failureCode) =>
        failureCode is
            ComputerUseWinFailureCodeValues.ObservationFailed or
            ComputerUseWinFailureCodeValues.UnexpectedInternalFailure or
            ComputerUseWinFailureCodeValues.InputDispatchFailed;
}
