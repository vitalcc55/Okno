// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinObservationFailureTranslator
{
    public static ComputerUseWinFailureDetails Translate(Exception exception, string unexpectedReason)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(unexpectedReason);

        return exception switch
        {
            CaptureOperationException captureException => ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                captureException.Message),
            _ => ComputerUseWinFailureDetails.Unexpected(
                ComputerUseWinFailureCodeValues.ObservationFailed,
                unexpectedReason,
                exception),
        };
    }
}
