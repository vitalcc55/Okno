// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record InputResult(
    string Status,
    string Decision,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    long? TargetHwnd = null,
    string? TargetSource = null,
    int CompletedActionCount = 0,
    int? FailedActionIndex = null,
    IReadOnlyList<InputActionResult>? Actions = null,
    string? ArtifactPath = null,
    string? RiskLevel = null,
    string? GuardCapability = null,
    bool RequiresConfirmation = false,
    bool DryRunSupported = false,
    IReadOnlyList<GuardReason>? Reasons = null);
