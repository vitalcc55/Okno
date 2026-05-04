// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiaSnapshotBackendResult(
    bool Success,
    string? Reason,
    string? FailureStage,
    DateTimeOffset CapturedAtUtc,
    ObservedWindowDescriptor? ObservedWindow = null,
    UiaElementSnapshot? Root = null,
    int RealizedDepth = 0,
    int NodeCount = 0,
    bool Truncated = false,
    bool DepthBoundaryReached = false,
    bool NodeBudgetBoundaryReached = false,
    string? DiagnosticArtifactPath = null);
