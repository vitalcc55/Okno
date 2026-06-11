// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiAutomationWorkerOperationValues
{
    public const string Snapshot = "snapshot";
    public const string WaitProbe = "wait_probe";
    public const string SemanticLookup = "semantic_lookup";
    public const string SetValue = "set_value";
    public const string Scroll = "scroll";
    public const string SecondaryAction = "secondary_action";
}

internal sealed record UiAutomationWorkerInvocation(
    string Operation,
    WindowDescriptor TargetWindow,
    UiaSnapshotRequest? SnapshotRequest = null,
    WaitRequest? WaitProbeRequest = null,
    UiaSemanticLookupRequest? SemanticLookupRequest = null,
    UiaSetValueRequest? SetValueRequest = null,
    UiaScrollRequest? ScrollRequest = null,
    UiaSecondaryActionRequest? SecondaryActionRequest = null);
