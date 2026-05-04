// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public interface IUiAutomationService
{
    Task<UiaSnapshotResult> SnapshotAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken);
}
