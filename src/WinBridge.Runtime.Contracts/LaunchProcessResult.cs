// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record LaunchProcessResult(
    string Status,
    string Decision,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    string? ExecutableIdentity = null,
    int? ProcessId = null,
    DateTimeOffset? StartedAtUtc = null,
    bool? HasExited = null,
    int? ExitCode = null,
    bool MainWindowObserved = false,
    long? MainWindowHandle = null,
    string? MainWindowObservationStatus = null,
    string? ArtifactPath = null,
    LaunchProcessPreview? Preview = null,
    string? RiskLevel = null,
    string? GuardCapability = null,
    bool RequiresConfirmation = false,
    bool DryRunSupported = false,
    IReadOnlyList<GuardReason>? Reasons = null);
