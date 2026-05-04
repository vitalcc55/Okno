// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiAutomationWorkerOperationValues
{
    public const string Snapshot = "snapshot";
    public const string WaitProbe = "wait_probe";
}

internal sealed record UiAutomationWorkerInvocation(
    string Operation,
    WindowDescriptor TargetWindow,
    UiaSnapshotRequest? SnapshotRequest = null,
    WaitRequest? WaitProbeRequest = null);
