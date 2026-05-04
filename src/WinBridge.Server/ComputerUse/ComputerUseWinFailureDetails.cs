// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinFailureDetails(
    string FailureCode,
    string Reason,
    Exception? AuditException = null)
{
    public static ComputerUseWinFailureDetails Expected(string failureCode, string reason) =>
        new(failureCode, reason);

    public static ComputerUseWinFailureDetails Unexpected(string failureCode, string reason, Exception exception) =>
        new(failureCode, reason, exception);
}
