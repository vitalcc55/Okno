// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal interface IInputService
{
    Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken);

    Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        string executionProfile,
        CancellationToken cancellationToken);
}
