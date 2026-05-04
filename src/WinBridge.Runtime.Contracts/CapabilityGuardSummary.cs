// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class CapabilitySummaryValues
{
    public const string Capture = "capture";
    public const string Uia = "uia";
    public const string Wait = "wait";
    public const string Input = "input";
    public const string Clipboard = "clipboard";
    public const string Launch = "launch";
}

public sealed record CapabilityGuardSummary(
    string Capability,
    string Status,
    IReadOnlyList<GuardReason> Reasons);
