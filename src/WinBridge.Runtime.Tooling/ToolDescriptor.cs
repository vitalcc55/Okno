// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Tooling;

public sealed record ToolDescriptor(
    string Name,
    string Capability,
    ToolLifecycle Lifecycle,
    ToolSafetyClass SafetyClass,
    string Summary,
    string? PlannedPhase,
    string? SuggestedAlternative,
    bool SmokeRequired,
    ToolExecutionPolicyDescriptor? ExecutionPolicy = null);
