// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class ReadinessDomainValues
{
    public const string DesktopSession = "desktop_session";
    public const string SessionAlignment = "session_alignment";
    public const string Integrity = "integrity";
    public const string UiAccess = "uiaccess";
}

public sealed record RuntimeReadinessSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ReadinessDomainStatus> Domains,
    IReadOnlyList<CapabilityGuardSummary> Capabilities);
