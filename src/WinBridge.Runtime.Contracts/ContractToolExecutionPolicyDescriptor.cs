// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

public sealed record ContractToolExecutionPolicyDescriptor(
    [property: JsonPropertyName("policy_group")]
    string PolicyGroup,
    [property: JsonPropertyName("risk_level")]
    string RiskLevel,
    [property: JsonPropertyName("guard_capability")]
    string GuardCapability,
    [property: JsonPropertyName("supports_dry_run")]
    bool SupportsDryRun,
    [property: JsonPropertyName("confirmation_mode")]
    string ConfirmationMode,
    [property: JsonPropertyName("redaction_class")]
    string RedactionClass);
