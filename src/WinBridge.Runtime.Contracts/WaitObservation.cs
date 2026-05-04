// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public sealed record WaitObservation(
    int? MatchCount = null,
    bool? TargetIsForeground = null,
    string? MatchedText = null,
    string? MatchedTextSource = null,
    string? DiagnosticArtifactPath = null,
    string? Detail = null,
    double? VisualDifferenceRatio = null,
    double? VisualDifferenceThreshold = null,
    string? VisualEvidenceStatus = null,
    string? VisualBaselineArtifactPath = null,
    string? VisualCurrentArtifactPath = null);
