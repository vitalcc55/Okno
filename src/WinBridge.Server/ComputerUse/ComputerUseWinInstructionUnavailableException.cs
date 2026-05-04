// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinInstructionUnavailableException : Exception
{
    public ComputerUseWinInstructionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
