// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record WaitResult(
    string Status,
    string Condition,
    string? TargetSource = null,
    string? TargetFailureCode = null,
    string? Reason = null,
    ObservedWindowDescriptor? Window = null,
    UiaElementSnapshot? MatchedElement = null,
    WaitObservation? LastObserved = null,
    string? ArtifactPath = null,
    int TimeoutMs = WaitDefaults.TimeoutMs,
    int ElapsedMs = 0,
    int AttemptCount = 0);
