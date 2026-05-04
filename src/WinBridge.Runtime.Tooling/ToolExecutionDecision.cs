// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Guards;

public enum ToolExecutionDecisionKind
{
    Allowed,
    Blocked,
    NeedsConfirmation,
    DryRunOnly,
}

public enum ToolExecutionMode
{
    Live,
    DryRun,
}

public sealed record ToolExecutionDecision(
    ToolExecutionDecisionKind Kind,
    ToolExecutionMode Mode,
    ToolExecutionRiskLevel RiskLevel,
    IReadOnlyList<GuardReason> Reasons,
    bool RequiresConfirmation,
    bool DryRunSupported,
    string GuardCapability)
{
    public bool IsAllowed => Kind == ToolExecutionDecisionKind.Allowed;
}
