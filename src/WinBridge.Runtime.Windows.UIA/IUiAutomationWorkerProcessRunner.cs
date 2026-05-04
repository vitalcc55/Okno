// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.UIA;

internal interface IUiAutomationWorkerProcessRunner
{
    Task<UiAutomationWorkerProcessResult> ExecuteAsync(
        object invocation,
        long? windowHwnd,
        TimeSpan? timeout,
        CancellationToken cancellationToken);
}
