// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Guards;

public sealed record RuntimeGuardAssessment(
    DisplayTopologySnapshot Topology,
    RuntimeReadinessSnapshot Readiness,
    IReadOnlyList<CapabilityGuardSummary> BlockedCapabilities,
    IReadOnlyList<GuardReason> Warnings);
