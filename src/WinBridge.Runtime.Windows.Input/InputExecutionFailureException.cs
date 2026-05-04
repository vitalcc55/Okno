// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class InputExecutionFailureException : Exception
{
    public InputExecutionFailureException(InputResult result, Exception innerException)
        : base("Runtime materialized factual windows.input failure after unexpected exception.", innerException)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public InputResult Result { get; }
}
